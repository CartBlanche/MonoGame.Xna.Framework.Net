using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Garbage on the wire. Whatever arrives, from a stranger or from a gamer already in the session, a
    /// host must neither throw out of its receive path, hang, nor lose its own state.
    /// </summary>
    [TestFixture]
    public class FuzzTests
    {
        private sealed class NullTransport : IPeerTransport
        {
            public bool IsBound => true;
            public PeerId LocalPeer => PeerId.None;
            public void Bind() { }
            public void Send(PeerId peer, byte[] data, int length) { }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => new TaskCompletionSource<(byte[], PeerId)>().Task;
            public void Dispose() { }
        }

        private static PeerId Peer(int port) => UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, port));

        private static async Task<(NetworkSession Host, NetworkGamer Gamer)> HostWithGamerAsync()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 8, 0, new Dictionary<string, object>());
            host.NetworkTransport = new NullTransport();
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(
                new JoinRequestMessage { GamerId = "p2", Gamertag = "Player 2" }, Peer(40001)));
            host.Update();
            return (host, host.RemoteGamers.Single(g => g.GamerId == "p2"));
        }

        // the first byte decides which parser sees the rest, so it is chosen from the ones that matter far more often than chance would
        private static byte InterestingFirstByte(Random random)
        {
            byte[] interesting = { 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x31, 0x32, 0x33, 0x34 };
            return random.Next(3) == 0 ? (byte)random.Next(256) : interesting[random.Next(interesting.Length)];
        }

        private static byte[] RandomDatagram(Random random)
        {
            var data = new byte[random.Next(0, 1300)];
            random.NextBytes(data);
            if (data.Length > 0)
                data[0] = InterestingFirstByte(random);
            // inside a channel the next byte is the payload kind (or part of a sequence number)
            if (data.Length > 4 && random.Next(2) == 0)
                data[3] = (byte)(0x31 + random.Next(5));
            return data;
        }

        private static byte[] Serialized(INetworkMessage message)
        {
            var writer = new PacketWriter();
            message.Serialize(writer);
            return writer.GetData();
        }

        // valid messages, to be damaged: truncated, bit-flipped, or with a field that claims far more than there is
        private static IEnumerable<byte[]> ValidMessages()
        {
            yield return Serialized(new JoinRequestMessage { GamerId = "x", Gamertag = "y" });
            yield return Serialized(new JoinAcceptedMessage { SessionId = "s", HostGamerId = "h", HostGamertag = "t", HostId = 1, YourId = 2,
                Others = { new JoinAcceptedMessage.RosterEntry { Id = 3, GamerId = "o", Gamertag = "p" } } });
            yield return Serialized(new JoinRejectedMessage { ErrorCode = NetworkSessionJoinError.SessionFull, Reason = "full" });
            yield return Serialized(new ReadinessUpdateMessage { GamerId = "p2", IsReady = true });
            yield return Serialized(new HeartbeatMessage { GamerId = "p2", SequenceNumber = 1, Timestamp = 5 });
            yield return Serialized(new HeartbeatReplyMessage { GamerId = "p2", RequestTimestamp = 5, ReplyTimestamp = 6 });
            yield return Serialized(new GamerLeavingMessage { GamerId = "p2", Reason = "bye" });
            yield return Serialized(new SessionStateMessage { NewState = NetworkSessionState.Playing, Timestamp = 1, Reason = "go" });
            yield return Serialized(new GamerJoinedMessage { Id = 4, GamerId = "g", Gamertag = "n" });
            yield return Serialized(new RemovedFromSessionMessage { Reason = "out" });
        }

        private static byte[] Damaged(byte[] original, Random random)
        {
            var copy = (byte[])original.Clone();
            switch (random.Next(4))
            {
                case 0:
                    return copy.Take(random.Next(copy.Length)).ToArray();
                case 1:
                    for (int i = 0, flips = random.Next(1, 5); i < flips; i++)
                        copy[random.Next(copy.Length)] ^= (byte)(1 << random.Next(8));
                    return copy;
                case 2:
                    // a length prefix or count that claims a great deal more than follows
                    if (copy.Length > 2)
                        copy[random.Next(1, copy.Length)] = 0xFF;
                    return copy;
                default:
                    return copy.Concat(new byte[random.Next(1, 64)]).ToArray();
            }
        }

        private static void Feed(NetworkSession host, byte[] datagram, PeerId from, List<string> problems)
        {
            try
            {
                host.ProcessIncomingDatagram(datagram, from);
            }
            catch (Exception ex)
            {
                problems.Add($"{ex.GetType().Name} on {BitConverter.ToString(datagram.Take(24).ToArray())}: {ex.Message}");
            }
        }

        [Test]
        public async Task RandomBytes_FromAGamerAndFromStrangers_NeverThrowOrHang()
        {
            var (host, gamer) = await HostWithGamerAsync();
            var random = new Random(12345);
            var problems = new List<string>();
            var gamerPeer = Peer(40001);
            var clock = Stopwatch.StartNew();

            for (int i = 0; i < 30000; i++)
                Feed(host, RandomDatagram(random), random.Next(2) == 0 ? gamerPeer : Peer(41000 + random.Next(50)), problems);

            Assert.That(problems.Take(5), Is.Empty, $"{problems.Count} of 30000 datagrams made the receive path throw");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(60)), "the receive path hung or crawled");
            AssertHostIsIntact(host);
            await host.DisposeAsync();
        }

        [Test]
        public async Task DamagedMessages_FromAGamerAndFromStrangers_NeverThrow()
        {
            var (host, gamer) = await HostWithGamerAsync();
            var random = new Random(777);
            var problems = new List<string>();
            var valid = ValidMessages().ToList();
            var gamerPeer = Peer(40001);

            for (int i = 0; i < 30000; i++)
            {
                byte[] bytes = Damaged(valid[random.Next(valid.Count)], random);
                // as the plain handshake datagram it would arrive as, and wrapped in a channel as game traffic would
                Feed(host, bytes, random.Next(2) == 0 ? gamerPeer : Peer(41000 + random.Next(50)), problems);
                var wrapped = new byte[bytes.Length + 2];
                wrapped[0] = ReliableChannel.ChannelUnreliable;
                wrapped[1] = PayloadKind.Message;
                Buffer.BlockCopy(bytes, 0, wrapped, 2, bytes.Length);
                Feed(host, wrapped, gamerPeer, problems);
            }

            Assert.That(problems.Take(5), Is.Empty, $"{problems.Count} damaged messages made the receive path throw");
            AssertHostIsIntact(host);
            await host.DisposeAsync();
        }

        [Test]
        public void EveryMessageType_RejectsABodyThatIsTooShort_WithOnlyAnExpectedException()
        {
            foreach (byte[] valid in ValidMessages())
            {
                for (int length = 0; length < valid.Length; length++)
                {
                    var message = NetworkMessageRegistry.CreateMessage(valid[0]);
                    if (message == null)
                        continue;
                    var reader = new PacketReader(valid.Take(length).ToArray());
                    reader.Position = Math.Min(1, length);
                    try
                    {
                        message.Deserialize(reader);
                    }
                    catch (System.IO.EndOfStreamException)
                    {
                        // the expected way to refuse a short body
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"{message.GetType().Name} cut to {length} bytes threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        private static void AssertHostIsIntact(NetworkSession host)
        {
            Assert.That(host.SessionState, Is.Not.EqualTo(NetworkSessionState.Ended), "garbage ended the session");
            Assert.That(host.LocalGamers.Count, Is.EqualTo(1));
            Assert.That(host.AllGamers.Count, Is.LessThanOrEqualTo(host.MaxGamers));
            Assert.That(host.AllGamers.Select(g => g.GamerId).Distinct().Count(), Is.EqualTo(host.AllGamers.Count), "a gamer is listed twice");
            Assert.That(host.AllGamers.Count(g => g.IsHost), Is.EqualTo(1), "there must be exactly one host");
        }
    }
}
