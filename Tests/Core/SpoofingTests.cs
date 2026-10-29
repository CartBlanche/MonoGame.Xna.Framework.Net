using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>Messages that claim more than their sender is entitled to are ignored.</summary>
    [TestFixture]
    public class SpoofingTests
    {
        private sealed class RecordingTransport : IPeerTransport
        {
            public readonly List<(PeerId Peer, byte[] Data)> Sent = new List<(PeerId, byte[])>();
            public bool IsBound => true;
            public PeerId LocalPeer => PeerId.None;
            public void Bind() { }
            public void Send(PeerId peer, byte[] data, int length) { Sent.Add((peer, data.Take(length).ToArray())); }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => new TaskCompletionSource<(byte[], PeerId)>().Task;
            public void Dispose() { }
        }

        private static PeerId Peer(int port) => UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, port));

        private static MessageReceivedEventArgs Message(INetworkMessage message, PeerId from) => new MessageReceivedEventArgs(message, from);

        private static async Task<(NetworkSession Host, RecordingTransport Transport)> HostAsync()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 8, 0, new Dictionary<string, object>());
            var transport = new RecordingTransport();
            host.NetworkTransport = transport;
            return (host, transport);
        }

        private static void Join(NetworkSession host, string id, PeerId from) =>
            host.DispatchIncomingMessage(Message(new JoinRequestMessage { GamerId = id, Gamertag = id }, from));

        private static NetworkSession NonHostSession()
        {
            var ctor = typeof(NetworkSession).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(NetworkSessionType), typeof(int), typeof(int), typeof(bool), typeof(IPeerTransport) }, null);
            return (NetworkSession)ctor.Invoke(new object[] { NetworkSessionType.Local, 4, 0, false, new RecordingTransport() });
        }

        [Test]
        public async Task AGamer_CanOnlyReportItselfReady_NotAnotherGamer_NorAStranger()
        {
            var (host, _) = await HostAsync();
            Join(host, "p2", Peer(1));
            Join(host, "p3", Peer(2));
            NetworkGamer p2 = host.RemoteGamers.Single(g => g.GamerId == "p2");

            host.DispatchIncomingMessage(Message(new ReadinessUpdateMessage { GamerId = "p2", IsReady = true }, Peer(2)));   // p3 speaks for p2
            host.DispatchIncomingMessage(Message(new ReadinessUpdateMessage { GamerId = "p2", IsReady = true }, Peer(99)));  // a stranger does
            Assert.That(p2.IsReady, Is.False);

            host.DispatchIncomingMessage(Message(new ReadinessUpdateMessage { GamerId = "p2", IsReady = true }, Peer(1)));   // p2 itself
            Assert.That(p2.IsReady, Is.True);
            await host.DisposeAsync();
        }

        [Test]
        public async Task AJoinRetry_FromAnotherAddress_DoesNotTakeOverTheGamersConnection()
        {
            var (host, transport) = await HostAsync();
            Join(host, "victim", Peer(1));
            transport.Sent.Clear();

            Join(host, "victim", Peer(666));       // somebody who read the roster claims to be the victim

            Assert.That(transport.Sent.All(s => s.Peer == Peer(666)), "nothing may be sent to the victim because of this");
            Assert.That(transport.Sent.Count, Is.EqualTo(1));
            Assert.That(transport.Sent[0].Data[0], Is.EqualTo(new JoinRejectedMessage().MessageType), "the intruder must be refused, not accepted");

            // the victim's data still goes to the victim
            transport.Sent.Clear();
            host.SendDataToGamer(host.RemoteGamers.Single(), new byte[] { 1 }, SendDataOptions.Reliable);
            Assert.That(transport.Sent.All(s => s.Peer == Peer(1)));

            // the victim's own retry is still answered
            transport.Sent.Clear();
            Join(host, "victim", Peer(1));
            Assert.That(transport.Sent.Any(s => s.Peer == Peer(1) && s.Data[0] == new JoinAcceptedMessage().MessageType));
            await host.DisposeAsync();
        }

        [Test]
        public void AClient_OnlyBelievesTheHostAboutTheSessionState()
        {
            var client = NonHostSession();
            var hostPeer = Peer(5000);
            client.AcceptGamer(new NetworkGamer(client, "the-host", isLocal: false, isHost: true, gamertag: "Host"));
            client.RegisterGamerEndpoint(client.AllGamers.Single(g => g.IsHost), hostPeer);
            client.sessionState = NetworkSessionState.Lobby;
            var started = new SessionStateMessage { NewState = NetworkSessionState.Playing, Timestamp = 1, Reason = "forged" };

            client.DispatchIncomingMessage(Message(started, Peer(6666)));
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Lobby), "a stranger started the game");

            client.DispatchIncomingMessage(Message(started, hostPeer));
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Playing));
        }

        [Test]
        public void AJoinAcceptance_OnlyCountsFromTheAddressTheRequestWentTo()
        {
            var client = NonHostSession();
            client.sessionState = NetworkSessionState.Joining;
            client.JoinTargetPeer = Peer(5000);
            var accepted = new JoinAcceptedMessage { SessionId = "s", HostGamerId = "real-host", HostGamertag = "Host", HostId = 1, YourId = 2 };

            client.DispatchIncomingMessage(Message(accepted, Peer(7777)));         // an impostor answers first
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Joining));
            Assert.That(client.AllGamers.Any(g => g.IsHost), Is.False, "the impostor became the host");

            client.DispatchIncomingMessage(Message(accepted, Peer(5000)));
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            Assert.That(client.AllGamers.Single(g => g.IsHost).GamerId, Is.EqualTo("real-host"));
        }

        [Test]
        public async Task AHeartbeat_FromAStranger_OrNamingSomebodyElse_DoesNotKeepAnyoneAlive()
        {
            var (host, transport) = await HostAsync();
            Join(host, "p2", Peer(1));
            Join(host, "p3", Peer(2));
            transport.Sent.Clear();
            var beat = new HeartbeatMessage { GamerId = "p2", SequenceNumber = 1, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

            host.DispatchIncomingMessage(Message(beat, Peer(99)));    // a stranger
            host.DispatchIncomingMessage(Message(beat, Peer(2)));     // p3, naming p2
            Assert.That(transport.Sent, Is.Empty, "a heartbeat from the wrong sender must not even be answered");

            host.DispatchIncomingMessage(Message(beat, Peer(1)));     // p2 itself
            host.Update();
            await host.DisposeAsync();
        }

        [Test]
        public void ARoundtripTimestamp_FromTheFutureOrLongAgo_IsNotAMeasurement()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.That(ConnectionHealthMonitor.RoundtripSince(now - 30)?.TotalMilliseconds, Is.InRange(20, 200));
            Assert.That(ConnectionHealthMonitor.RoundtripSince(now + 100_000), Is.Null);
            Assert.That(ConnectionHealthMonitor.RoundtripSince(long.MinValue), Is.Null);
            Assert.That(ConnectionHealthMonitor.RoundtripSince(long.MaxValue), Is.Null);
            Assert.That(ConnectionHealthMonitor.RoundtripSince(now - 3_600_000), Is.Null);
        }
    }
}
