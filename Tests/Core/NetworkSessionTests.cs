using NUnit.Framework;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net;
using System.Collections.Generic;
using System;
using System.Net;
using System.Threading;
using System.Linq;
using System.Reflection;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class NetworkSessionTests
    {
        private sealed class FakeNetworkTransport : INetworkTransport
        {
            private readonly Queue<(byte[] data, IPEndPoint sender)> receiveQueue = new Queue<(byte[] data, IPEndPoint sender)>();
            private readonly object gate = new object();

            public List<(byte[] data, int length, IPEndPoint endpoint)> SentPackets { get; } = new List<(byte[] data, int length, IPEndPoint endpoint)>();

            public bool IsBound { get; private set; }
            public IPEndPoint LocalEndPoint => null; // Not needed for tests

            public void Bind()
            {
                IsBound = true;
            }

            public void Bind(int port)
            {
                IsBound = true;
            }

            public void Send(byte[] data, IPEndPoint endpoint)
            {
                Send(data, data.Length, endpoint);
            }

            public void Send(byte[] data, int length, IPEndPoint endpoint)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
                if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                lock (gate)
                {
                    SentPackets.Add((copy, length, endpoint));
                }
            }

            public Task SendAsync(byte[] data, IPEndPoint endpoint)
            {
                Send(data, endpoint);
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] data, int length, IPEndPoint endpoint)
            {
                Send(data, length, endpoint);
                return Task.CompletedTask;
            }

            public (byte[] data, IPEndPoint sender) Receive()
            {
                lock (gate)
                {
                    if (receiveQueue.Count == 0)
                    {
                        return (Array.Empty<byte>(), new IPEndPoint(IPAddress.Loopback, 0));
                    }

                    return receiveQueue.Dequeue();
                }
            }

            public Task<(byte[] data, IPEndPoint sender)> ReceiveAsync()
            {
                return Task.FromResult(Receive());
            }

            public void EnqueueInbound(byte[] data, IPEndPoint sender)
            {
                lock (gate)
                {
                    receiveQueue.Enqueue((data, sender));
                }
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public async Task MessageReceived_EventIsFired_ThroughDispatch()
        {
            var session = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var tcs = new TaskCompletionSource<MessageReceivedEventArgs>();

            session.MessageReceived += (sender, args) =>
            {
                tcs.TrySetResult(args);
            };

            session.DispatchIncomingMessage(new MessageReceivedEventArgs(new HeartbeatMessage
            {
                GamerId = "remote",
                SequenceNumber = 1,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, null));

            var result = await tcs.Task;
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<HeartbeatMessage>(result.Message);

            await session.DisposeAsync();
        }

        [Test]
        public async Task HostProcessesJoinRequest_AndSendsAcceptance()
        {
            var hostSession = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            hostSession.NetworkTransport = transport;

            var joinRequest = new JoinRequestMessage
            {
                GamerId = "Player2",
                Gamertag = "PlayerTwo"
            };

            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
            hostSession.DispatchIncomingMessage(new MessageReceivedEventArgs(joinRequest, remoteEndpoint));

            Assert.IsTrue(hostSession.AllGamers.Any(g => g.Gamertag == "PlayerTwo"));
            Assert.That(transport.SentPackets.Count, Is.EqualTo(1));

            await hostSession.DisposeAsync();
        }

        static JoinRequestMessage JoinRequest(string id)
        {
            return new JoinRequestMessage { GamerId = id, Gamertag = "Player " + id };
        }

        static JoinRejectedMessage LastRejection(FakeNetworkTransport transport)
        {
            // parsed the way the session parses framework messages: type id, then the body
            var sent = transport.SentPackets.Last().data;
            var reader = new PacketReader(sent);
            var message = NetworkMessageRegistry.CreateMessage(reader.ReadByte());
            message.Deserialize(reader);
            return message as JoinRejectedMessage;
        }

        [Test]
        public async Task Host_RefusesNewPlayers_AfterStart_WhenJoinInProgressIsNotAllowed()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            host.NetworkTransport = transport;
            host.AllowJoinInProgress = false;
            host.StartGame();
            transport.SentPackets.Clear();

            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("late"), new IPEndPoint(IPAddress.Loopback, 12345)));

            Assert.IsFalse(host.AllGamers.Any(g => g.Id == "late"), "the latecomer must not be added");
            var rejection = LastRejection(transport);
            Assert.IsNotNull(rejection);
            Assert.That(rejection.ErrorCode, Is.EqualTo(NetworkSessionJoinError.SessionNotJoinable));

            await host.DisposeAsync();
        }

        [Test]
        public async Task Host_AcceptsNewPlayers_AfterStart_ByDefault()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            host.NetworkTransport = new FakeNetworkTransport();
            host.StartGame();

            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("late"), new IPEndPoint(IPAddress.Loopback, 12345)));

            Assert.IsTrue(host.AllGamers.Any(g => g.Id == "late"));
            await host.DisposeAsync();
        }

        [Test]
        public async Task Host_AcceptsNewPlayers_InTheLobby_EvenWhenJoinInProgressIsNotAllowed()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            host.NetworkTransport = new FakeNetworkTransport();
            host.AllowJoinInProgress = false;

            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("early"), new IPEndPoint(IPAddress.Loopback, 12345)));

            Assert.IsTrue(host.AllGamers.Any(g => g.Id == "early"));
            await host.DisposeAsync();
        }

        [Test]
        public async Task Host_StillAnswersARepeatedRequest_FromSomebodyAlreadyIn_AfterStart()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            host.NetworkTransport = transport;
            host.AllowJoinInProgress = false;
            var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);

            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("early"), endpoint));
            host.StartGame();
            transport.SentPackets.Clear();
            int gamersBefore = host.AllGamers.Count;

            // the acceptance was lost, so the client asks again
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("early"), endpoint));

            Assert.That(host.AllGamers.Count, Is.EqualTo(gamersBefore), "no duplicate gamer");
            Assert.IsNull(LastRejection(transport), "it must be accepted again, not refused");
            await host.DisposeAsync();
        }

        [Test]
        public void Client_RemembersWhyTheHostRefusedIt_WhileJoining()
        {
            var client = new NetworkSession(NetworkSessionType.SystemLink, 4, 0, false, "session", new FakeNetworkTransport());
            client.sessionState = NetworkSessionState.Joining;

            client.DispatchIncomingMessage(new MessageReceivedEventArgs(new JoinRejectedMessage
            {
                ErrorCode = NetworkSessionJoinError.SessionNotJoinable,
                Reason = "The game has already started"
            }, null));

            Assert.That(client.joinRejectedReason, Is.EqualTo("The game has already started"));
            Assert.That(client.joinRejectedError, Is.EqualTo(NetworkSessionJoinError.SessionNotJoinable));
            client.Dispose();
        }

        // a host with one remote gamer ("p2") who has joined from the given endpoint
        private static async Task<(NetworkSession host, FakeNetworkTransport transport, NetworkGamer gamer, IPEndPoint endpoint)> HostWithGamer()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            host.NetworkTransport = transport;
            var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);

            host.DispatchIncomingMessage(new MessageReceivedEventArgs(JoinRequest("p2"), endpoint));
            transport.SentPackets.Clear();

            return (host, transport, host.RemoteGamers.Single(g => g.Id == "p2"), endpoint);
        }

        [Test]
        public async Task ReliableData_IsSentOnTheReliableChannel()
        {
            var (host, transport, gamer, _) = await HostWithGamer();

            host.SendDataToGamer(gamer, new byte[] { 0x50, 1, 2 }, SendDataOptions.Reliable);

            Assert.That(transport.SentPackets.First().data, Is.EqualTo(new byte[] { ReliableChannel.ChannelReliable, 0, 0, PayloadKind.Application, 0x50, 1, 2 }));
            await host.DisposeAsync();
        }

        [Test]
        public async Task UnreliableData_IsSentOnTheUnreliableChannel()
        {
            var (host, transport, gamer, _) = await HostWithGamer();

            host.SendDataToGamer(gamer, new byte[] { 0x50, 1, 2 }, SendDataOptions.None);

            Assert.That(transport.SentPackets.Single().data, Is.EqualTo(new byte[] { ReliableChannel.ChannelUnreliable, PayloadKind.Application, 0x50, 1, 2 }));
            await host.DisposeAsync();
        }

        [Test]
        public async Task ReliableData_IsResentUntilAcknowledged()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();

            host.SendDataToGamer(gamer, new byte[] { 0x50 }, SendDataOptions.Reliable);
            Thread.Sleep(500);          // several resend intervals

            int reliableSends() => transport.SentPackets.Count(p => p.data[0] == ReliableChannel.ChannelReliable);
            Assert.That(reliableSends(), Is.GreaterThan(1), "it should have been resent");
            Assert.That(gamer.UnacknowledgedAge, Is.GreaterThan(TimeSpan.Zero));

            // the peer acknowledges: it expects sequence 1 next
            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelAck, 1, 0 }, endpoint);
            int afterAck = reliableSends();
            Thread.Sleep(300);

            Assert.That(reliableSends(), Is.EqualTo(afterAck), "an acknowledged message is not resent");
            Assert.That(gamer.UnacknowledgedAge, Is.EqualTo(TimeSpan.Zero));
            await host.DisposeAsync();
        }

        [Test]
        public async Task ReliableData_FromAPeer_IsDeliveredOnceEvenIfDuplicated()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            byte[] datagram = { ReliableChannel.ChannelReliable, 0, 0, PayloadKind.Application, 0x50, 9, 9 };

            host.ProcessIncomingDatagram(datagram, endpoint);
            host.ProcessIncomingDatagram(datagram, endpoint);       // the network duplicated it

            var local = host.LocalGamers[0];
            var buffer = new byte[64];
            Assert.That(local.IsDataAvailable, Is.True);
            int length = local.ReceiveData(buffer, out NetworkGamer sender);
            Assert.That(buffer.Take(length), Is.EqualTo(new byte[] { 0x50, 9, 9 }));
            Assert.That(sender.Id, Is.EqualTo("p2"));
            Assert.That(local.IsDataAvailable, Is.False, "the duplicate must not be delivered");
            await host.DisposeAsync();
        }

        [Test]
        public async Task ReliableData_FromAPeer_IsAcknowledged()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();

            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelReliable, 0, 0, PayloadKind.Application, 0x50 }, endpoint);
            Thread.Sleep(300);          // the resend timer sends the acknowledgement

            Assert.That(transport.SentPackets.Any(p => p.data[0] == ReliableChannel.ChannelAck && p.data[1] == 1),
                "the peer should be told that sequence 0 arrived and 1 is next");
            await host.DisposeAsync();
        }

        [Test]
        public async Task OutOfOrderReliableData_IsHeldBackUntilTheGapIsFilled()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();

            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelReliable, 1, 0, PayloadKind.Application, 0x50, 2 }, endpoint);
            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False, "sequence 1 must wait for sequence 0");

            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelReliable, 0, 0, PayloadKind.Application, 0x50, 1 }, endpoint);

            var local = host.LocalGamers[0];
            var buffer = new byte[64];
            local.ReceiveData(buffer, out _);
            Assert.That(buffer[1], Is.EqualTo(1));
            local.ReceiveData(buffer, out _);
            Assert.That(buffer[1], Is.EqualTo(2));
            await host.DisposeAsync();
        }

        [Test]
        public async Task AnyDatagramFromAGamer_CountsAsProofTheyAreStillThere()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            Thread.Sleep(200);
            Assert.That(host.ConnectionMonitor.TimeSinceActivity("p2").Value, Is.GreaterThan(TimeSpan.FromMilliseconds(150)));

            // game data, not a heartbeat
            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelUnreliable, PayloadKind.Application, 0x50, 1 }, endpoint);

            Assert.That(host.ConnectionMonitor.TimeSinceActivity("p2").Value, Is.LessThan(TimeSpan.FromMilliseconds(100)));
            await host.DisposeAsync();
        }

        [Test]
        public async Task DatagramsFromAnUnknownEndpoint_DoNotKeepAGamerAlive()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            Thread.Sleep(200);

            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelUnreliable, PayloadKind.Application, 0x50, 1 }, new IPEndPoint(IPAddress.Loopback, 54321));

            Assert.That(host.ConnectionMonitor.TimeSinceActivity("p2").Value, Is.GreaterThan(TimeSpan.FromMilliseconds(150)));
            await host.DisposeAsync();
        }

        [Test]
        public async Task PlainDatagrams_AreOnlyEverRegisteredMessages_NeverGameData()
        {
            // join requests and replies are sent without a channel, so a plain datagram is a framework
            // message or nothing: it must never come out as game data
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            var received = new List<INetworkMessage>();
            host.MessageReceived += (s, e) => received.Add(e.Message);

            host.ProcessIncomingDatagram(new byte[] { 0x50, 7 }, endpoint);          // unregistered type id
            var heartbeat = new PacketWriter();
            new HeartbeatMessage { GamerId = "p2", SequenceNumber = 1, Timestamp = 1 }.Serialize(heartbeat);
            host.ProcessIncomingDatagram(heartbeat.GetData(), endpoint);             // a registered message

            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False);
            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received[0], Is.InstanceOf<HeartbeatMessage>());
            await host.DisposeAsync();
        }

        [Test]
        public async Task GameData_ThatLooksLikeAFrameworkMessage_IsStillGameData()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            var received = new List<INetworkMessage>();
            host.MessageReceived += (s, e) => received.Add(e.Message);

            // every framework message id as the first byte, sized so that some of them would parse
            for (byte id = 1; id <= 12; id++)
                host.ProcessIncomingDatagram(
                    new byte[] { ReliableChannel.ChannelUnreliable, PayloadKind.Application, id, 0, 0, 0 }, endpoint);

            var local = host.LocalGamers[0];
            var buffer = new byte[16];
            for (byte id = 1; id <= 12; id++)
            {
                Assert.That(local.IsDataAvailable, Is.True, "game packet starting with " + id + " was swallowed");
                int length = local.ReceiveData(buffer, out _);
                Assert.That(buffer.Take(length), Is.EqualTo(new byte[] { id, 0, 0, 0 }));
            }
            Assert.That(received, Is.Empty, "game data must not be treated as a framework message");
            await host.DisposeAsync();
        }

        [Test]
        public async Task FrameworkMessages_ArriveThroughTheChannel_AsMessages_NotGameData()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();
            var received = new List<INetworkMessage>();
            host.MessageReceived += (s, e) => received.Add(e.Message);
            var writer = new PacketWriter();
            new HeartbeatMessage { GamerId = "p2", SequenceNumber = 1, Timestamp = 1 }.Serialize(writer);
            byte[] message = writer.GetData();
            byte[] datagram = new byte[message.Length + 2];
            datagram[0] = ReliableChannel.ChannelUnreliable;
            datagram[1] = PayloadKind.Message;
            Buffer.BlockCopy(message, 0, datagram, 2, message.Length);

            host.ProcessIncomingDatagram(datagram, endpoint);

            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False);
            await host.DisposeAsync();
        }

        [Test]
        public async Task ChannelDatagrams_FromAnUnknownSender_AreDropped()
        {
            var (host, transport, gamer, endpoint) = await HostWithGamer();

            host.ProcessIncomingDatagram(new byte[] { ReliableChannel.ChannelReliable, 0, 0, PayloadKind.Application, 0x50 }, new IPEndPoint(IPAddress.Loopback, 54321));

            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False);
            await host.DisposeAsync();
        }

        [Test]
        public async Task Host_RejectsAnOlderProtocolVersion()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            host.NetworkTransport = transport;

            // before the reliable channel existed (version 1) data was not wrapped, so the two cannot talk
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(
                new JoinRequestMessage { GamerId = "old", Gamertag = "Old", ProtocolVersion = 1 },
                new IPEndPoint(IPAddress.Loopback, 12345)));

            Assert.IsFalse(host.AllGamers.Any(g => g.Id == "old"));
            Assert.That(LastRejection(transport).ErrorCode, Is.EqualTo(NetworkSessionJoinError.ProtocolVersionMismatch));
            await host.DisposeAsync();
        }

        [Test]
        public async Task SendToAll_UsesFakeTransportForRemoteGamers()
        {
            var session = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var transport = new FakeNetworkTransport();
            session.NetworkTransport = transport;

            var remote = new NetworkGamer(session, "remote-1", false, false, "RemoteOne");
            session.AcceptGamer(remote);
            session.RegisterGamerEndpoint(remote, new IPEndPoint(IPAddress.Loopback, 54321));

            var writer = new PacketWriter();
            writer.Write("payload");

            session.SendToAll(writer, SendDataOptions.Reliable);

            Assert.That(transport.SentPackets.Count, Is.EqualTo(1));
            Assert.That(transport.SentPackets[0].length, Is.GreaterThan(0));

            await session.DisposeAsync();
        }

        [Test]
        public async Task DisposeAsync_CleansUpResources()
        {
            var session = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            Assert.DoesNotThrowAsync(async () => await session.DisposeAsync());
        }

        [Test]
        public async Task EvictGamer_WhenRemoteHostRemoved_ClientEndsSession()
        {
            var session = CreateNonHostSessionForTests();
            var remoteHost = new NetworkGamer(session, "remote-host", isLocal: false, isHost: true, gamertag: "Host");
            session.AcceptGamer(remoteHost);
            session.AllowHostMigration = true;

            NetworkSessionEndReason? endReason = null;
            session.SessionEnded += (_, args) => endReason = args.EndReason;

            session.EvictGamer(remoteHost);

            Assert.That(session.SessionState, Is.EqualTo(NetworkSessionState.Ended));
            Assert.That(endReason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));

            await session.DisposeAsync();
        }

        [Test]
        public async Task EvictGamer_WhenRemotePeerRemoved_HostStaysActive()
        {
            var session = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var remotePeer = new NetworkGamer(session, "remote-peer", isLocal: false, isHost: false, gamertag: "Peer");
            session.AcceptGamer(remotePeer);

            session.EvictGamer(remotePeer);

            Assert.That(session.SessionState, Is.Not.EqualTo(NetworkSessionState.Ended));
            Assert.That(session.AllGamers.Any(g => g.Id == remotePeer.Id), Is.False);

            await session.DisposeAsync();
        }

        private static NetworkSession CreateNonHostSessionForTests()
        {
            var ctor = typeof(NetworkSession).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(NetworkSessionType), typeof(int), typeof(int), typeof(bool), typeof(INetworkTransport) },
                modifiers: null);

            Assert.That(ctor, Is.Not.Null, "Expected internal NetworkSession constructor for test setup.");

            var session = (NetworkSession)ctor.Invoke(new object[]
            {
                NetworkSessionType.Local,
                4,
                0,
                false,
                new FakeNetworkTransport()
            });

            return session;
        }
    }
}