using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Game data over real UDP, through SendData / IsDataAvailable / ReceiveData, as ShipGame uses it.
    /// </summary>
    [TestFixture]
    public class DataTests
    {
        SessionHarness lan;
        NetworkSession host;
        NetworkSession client;

        [SetUp]
        public async Task SetUp()
        {
            lan = new SessionHarness();
            host = await lan.StartHostAsync(maxGamers: 4);
            client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2), "client never reached the host");
        }

        [TearDown]
        public void TearDown() => lan.Dispose();

        /// <summary>Drains everything waiting for a local gamer, as a game's per-frame receive loop does.</summary>
        static List<(byte[] Data, NetworkGamer Sender)> Drain(LocalNetworkGamer gamer)
        {
            var packets = new List<(byte[], NetworkGamer)>();
            var buffer = new byte[1024];
            while (gamer.IsDataAvailable)
            {
                int length = gamer.ReceiveData(buffer, out NetworkGamer sender);
                packets.Add((buffer.Take(length).ToArray(), sender));
            }
            return packets;
        }

        static async Task<List<(byte[] Data, NetworkGamer Sender)>> CollectAsync(
            SessionHarness lan, LocalNetworkGamer receiver, int expected)
        {
            var got = new List<(byte[] Data, NetworkGamer Sender)>();
            await lan.WaitUntilAsync(() =>
            {
                got.AddRange(Drain(receiver));
                return got.Count >= expected;
            });
            return got;
        }

        [Test]
        public async Task ReliableData_FromTheHost_ArrivesAtTheClient_WithTheHostAsSender()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            hostGamer.SendData(new byte[] { 1, 2, 3, 4 }, SendDataOptions.Reliable, host.RemoteGamers[0]);

            var got = await CollectAsync(lan, clientGamer, 1);

            Assert.That(got.Count, Is.EqualTo(1));
            Assert.That(got[0].Data, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(got[0].Sender.IsHost, Is.True);
        }

        [Test]
        public async Task UnreliableData_FromTheClient_ArrivesAtTheHost_OnALoopbackLan()
        {
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);

            // unreliable means may be lost, but not on loopback; send a few so one lost datagram can't fail the test
            for (int i = 0; i < 5; i++)
                clientGamer.SendData(new byte[] { 9, (byte)i }, SendDataOptions.None, client.Host);

            var got = await CollectAsync(lan, hostGamer, 1);

            Assert.That(got, Is.Not.Empty);
            Assert.That(got[0].Data[0], Is.EqualTo(9));
            Assert.That(got[0].Sender.IsLocal, Is.False);
        }

        [Test]
        public async Task ReliableData_ArrivesOnce_AndInOrder_ManyPackets()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);
            const int Count = 300;

            // first byte 0xAA keeps these clear of the framework message ids (see the collision test below)
            for (int i = 0; i < Count; i++)
                hostGamer.SendData(Packet(0xAA, i), SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);

            var got = await CollectAsync(lan, clientGamer, Count);

            Assert.That(got.Count, Is.EqualTo(Count), "wrong number of packets (lost or duplicated)");
            for (int i = 0; i < Count; i++)
                Assert.That(BitConverter.ToInt32(got[i].Data, 1), Is.EqualTo(i), "packet " + i + " out of order");
        }

        [Test]
        public async Task GamePackets_ThatStartWithAFrameworkMessageId_AreStillDelivered()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            // four bytes, the way an int goes out: 2 to 10 are the framework message ids, and some of
            // those messages are four bytes long when empty
            for (byte first = 1; first <= 12; first++)
                hostGamer.SendData(new byte[] { first, 0, 0, 0 }, SendDataOptions.Reliable, host.RemoteGamers[0]);

            var got = await CollectAsync(lan, clientGamer, 12);

            Assert.That(got.Select(p => p.Data[0]), Is.EqualTo(Enumerable.Range(1, 12).Select(b => (byte)b)),
                "some game packets never reached the game");
        }

        static byte[] Packet(byte first, int value)
        {
            var packet = new byte[5];
            packet[0] = first;
            BitConverter.GetBytes(value).CopyTo(packet, 1);
            return packet;
        }

        [Test]
        public async Task ALargeReliableMessage_ArrivesInOnePiece()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);
            var big = new byte[200000];
            new Random(3).NextBytes(big);

            hostGamer.SendData(new byte[] { 1 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
            hostGamer.SendData(big, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
            hostGamer.SendData(new byte[] { 2 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);

            var got = new List<(byte[] Data, NetworkGamer Sender)>();
            var buffer = new byte[big.Length + 16];
            await lan.WaitUntilAsync(() =>
            {
                while (clientGamer.IsDataAvailable)
                {
                    int length = clientGamer.ReceiveData(buffer, out NetworkGamer sender);
                    got.Add((buffer.Take(length).ToArray(), sender));
                }
                return got.Count >= 3;
            });

            Assert.That(got.Count, Is.EqualTo(3));
            Assert.That(got[0].Data, Is.EqualTo(new byte[] { 1 }));
            Assert.That(got[1].Data, Is.EqualTo(big));
            Assert.That(got[2].Data, Is.EqualTo(new byte[] { 2 }));
        }

        [Test]
        public async Task InOrderData_WithoutReliable_ArrivesOnALoopbackLan()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            for (int i = 0; i < 20; i++)
                hostGamer.SendData(Packet(0xBB, i), SendDataOptions.InOrder, host.RemoteGamers[0]);

            var got = new List<(byte[] Data, NetworkGamer Sender)>();
            await lan.WaitUntilAsync(() =>
            {
                got.AddRange(Drain(clientGamer));
                return got.Count >= 1;
            });
            await lan.WaitUntilAsync(() => false, timeoutMs: 300);
            got.AddRange(Drain(clientGamer));

            // loss is allowed, going backwards is not
            var values = got.Select(p => BitConverter.ToInt32(p.Data, 1)).ToList();
            Assert.That(values, Is.Not.Empty);
            Assert.That(values, Is.Ordered.Ascending);
        }

        [Test]
        public void UnreliableData_LargerThanOnePacket_IsRefused_ButReliableIsNot()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                hostGamer.SendData(new byte[5000], SendDataOptions.None, host.RemoteGamers[0]));
            Assert.DoesNotThrow(() =>
                hostGamer.SendData(new byte[5000], SendDataOptions.Reliable, host.RemoteGamers[0]));
        }

        [Test]
        public async Task PacketWriterAndReader_RoundTripAcrossTheWire()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            var writer = new PacketWriter();
            writer.Write((byte)42);
            writer.Write(-7);
            writer.Write(3.5f);
            writer.Write("ship");
            writer.Write(new Vector3(1, 2, 3));
            writer.Write(true);
            writer.Write(123456789012345L);
            hostGamer.SendData(writer, SendDataOptions.Reliable, host.RemoteGamers[0]);

            var reader = new PacketReader();
            NetworkGamer sender = null;
            bool received = await lan.WaitUntilAsync(() =>
            {
                if (!clientGamer.IsDataAvailable)
                    return false;
                clientGamer.ReceiveData(reader, out sender);
                return true;
            });

            Assert.That(received, "packet never arrived");
            Assert.That(sender.IsHost, Is.True);
            Assert.That(reader.ReadByte(), Is.EqualTo(42));
            Assert.That(reader.ReadInt32(), Is.EqualTo(-7));
            Assert.That(reader.ReadSingle(), Is.EqualTo(3.5f));
            Assert.That(reader.ReadString(), Is.EqualTo("ship"));
            Assert.That(reader.ReadVector3(), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(reader.ReadBoolean(), Is.True);
            Assert.That(reader.ReadInt64(), Is.EqualTo(123456789012345L));
            Assert.That(reader.BytesRemaining, Is.EqualTo(0));
        }

        [Test]
        public async Task SendingToOneGamer_DoesNotReachAnother()
        {
            NetworkSession second = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 3));
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer firstClient = SessionHarness.LocalOf(client);
            LocalNetworkGamer secondClient = SessionHarness.LocalOf(second);

            NetworkGamer firstRemote = host.RemoteGamers.First(g => g.Gamertag == firstClient.Gamertag);
            hostGamer.SendData(new byte[] { 5 }, SendDataOptions.Reliable, firstRemote);

            var got = await CollectAsync(lan, firstClient, 1);
            await lan.WaitUntilAsync(() => false, timeoutMs: 300);

            Assert.That(got.Count, Is.EqualTo(1));
            Assert.That(Drain(secondClient), Is.Empty);
        }

        [Test]
        public async Task TheHostCanSendToEveryClient_WithOneCall()
        {
            NetworkSession second = await lan.JoinClientAsync();
            NetworkSession third = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 4));
            var recipients = host.RemoteGamers.ToList();

            SessionHarness.LocalOf(host).SendData(new byte[] { 77 }, SendDataOptions.Reliable, recipients);

            foreach (NetworkSession c in new[] { client, second, third })
            {
                var got = await CollectAsync(lan, SessionHarness.LocalOf(c), 1);
                Assert.That(got.Count, Is.EqualTo(1), c.LocalGamers[0].Gamertag + " got nothing");
                Assert.That(got[0].Data, Is.EqualTo(new byte[] { 77 }));
            }
        }
    }
}
