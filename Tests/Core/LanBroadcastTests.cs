using System;
using System.Net;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class LanBroadcastTests
    {
        [TestCase("192.168.1.20", "255.255.255.0", "192.168.1.255")]
        [TestCase("10.0.5.7", "255.0.0.0", "10.255.255.255")]
        [TestCase("172.16.35.9", "255.255.240.0", "172.16.47.255")]
        [TestCase("192.168.1.130", "255.255.255.192", "192.168.1.191")]
        [TestCase("192.168.1.5", "255.255.255.252", "192.168.1.7")]
        public void DirectedBroadcast_fills_the_host_bits(string address, string mask, string expected)
        {
            Assert.That(
                LanBroadcast.DirectedBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask)).ToString(),
                Is.EqualTo(expected));
        }

        [Test]
        public void DirectedBroadcast_rejects_IPv6()
        {
            Assert.Throws<ArgumentException>(() =>
                LanBroadcast.DirectedBroadcast(IPAddress.IPv6Loopback, IPAddress.Parse("255.255.255.0")));
        }

        [Test]
        public void GetBroadcastAddresses_lists_real_subnets_only()
        {
            foreach (IPAddress address in LanBroadcast.GetBroadcastAddresses())
            {
                Assert.That(address.AddressFamily, Is.EqualTo(System.Net.Sockets.AddressFamily.InterNetwork));
                Assert.That(address, Is.Not.EqualTo(IPAddress.Broadcast), "the global broadcast is sent separately");
                Assert.That(IPAddress.IsLoopback(address), Is.False);
            }
        }

        [Test]
        public void GetBroadcastAddresses_has_no_duplicates()
        {
            var addresses = LanBroadcast.GetBroadcastAddresses();
            Assert.That(addresses, Is.Unique);
        }
    }
}
