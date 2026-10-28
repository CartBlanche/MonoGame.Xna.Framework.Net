using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class PeerTransportTests
    {
        [Test]
        public void PeerIds_AreComparedByValue_AndNoneIsInvalid()
        {
            Assert.That(new PeerId("a"), Is.EqualTo(new PeerId("a")));
            Assert.That(new PeerId("a") == new PeerId("b"), Is.False);
            Assert.That(PeerId.None.IsValid, Is.False);
            Assert.That(new PeerId("a").IsValid, Is.True);
            Assert.That(new PeerId("a").GetHashCode(), Is.EqualTo(new PeerId("a").GetHashCode()));
            Assert.Throws<ArgumentNullException>(() => new PeerId(null));
        }

        [Test]
        public void AUdpPeer_RoundTripsToItsEndPoint_AndOtherPeersAreNotUdp()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 31338);

            Assert.That(UdpTransport.ToEndPoint(UdpTransport.ToPeer(endpoint)), Is.EqualTo(endpoint));
            Assert.That(UdpTransport.ToEndPoint(new PeerId("steam:76561198000000000")), Is.Null);
            Assert.That(UdpTransport.ToEndPoint(PeerId.None), Is.Null);
        }

        [Test]
        public async Task TwoUdpTransports_ExchangeDatagrams_ByPeerId()
        {
            using var a = new UdpTransport();
            using var b = new UdpTransport();
            a.Bind();
            b.Bind();
            PeerId bPeer = UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, b.LocalEndPoint.Port));

            a.Send(bPeer, new byte[] { 1, 2, 3, 99 }, 3);        // only the first 3 bytes
            var received = await b.ReceiveAsync();

            Assert.That(received.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(UdpTransport.ToEndPoint(received.Sender).Port, Is.EqualTo(a.LocalEndPoint.Port));
        }

        [Test]
        public void SendingToAPeerOfAnotherTransport_IsRejected()
        {
            using var udp = new UdpTransport();
            udp.Bind();
            Assert.Throws<ArgumentException>(() => udp.Send(new PeerId("steam:1"), new byte[1], 1));
        }

        [Test]
        public void APreferredPortThatIsTaken_FallsBackToAFreeOne()
        {
            using var first = new UdpTransport();
            first.Bind();
            int taken = first.LocalEndPoint.Port;

            using var second = new UdpTransport(taken);
            second.Bind();

            Assert.That(second.IsBound, Is.True);
            Assert.That(second.LocalEndPoint.Port, Is.Not.EqualTo(taken));
        }
    }
}
