using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class RateLimiterTests
    {
        [Test]
        public void AKey_GetsABurst_ThenATrickle()
        {
            double now = 0;
            var limiter = new RateLimiter(burst: 3, perSecond: 1, seconds: () => now);

            Assert.That(Enumerable.Range(0, 3).All(_ => limiter.TryAcquire("a")), "the burst");
            Assert.That(limiter.TryAcquire("a"), Is.False, "then it must wait");

            now = 0.5;
            Assert.That(limiter.TryAcquire("a"), Is.False, "half a token is not enough");
            now = 1.0;
            Assert.That(limiter.TryAcquire("a"), Is.True, "one second earns one");
            Assert.That(limiter.TryAcquire("a"), Is.False);

            now = 1000;
            Assert.That(Enumerable.Range(0, 3).Count(_ => limiter.TryAcquire("a")), Is.EqualTo(3), "idle time refills the burst, but no more than the burst");
        }

        [Test]
        public void KeysAreIndependent()
        {
            var limiter = new RateLimiter(burst: 1, perSecond: 0.001, seconds: () => 0);
            Assert.That(limiter.TryAcquire("a"), Is.True);
            Assert.That(limiter.TryAcquire("a"), Is.False);
            Assert.That(limiter.TryAcquire("b"), Is.True, "another key is not held up by the first");
        }

        [Test]
        public void AFloodOfMadeUpKeys_DoesNotGrowItWithoutLimit()
        {
            var limiter = new RateLimiter(burst: 1, perSecond: 1, maxKeys: 100, seconds: () => 0);
            for (int i = 0; i < 100_000; i++)
                limiter.TryAcquire("peer-" + i);

            Assert.That(limiter.TrackedKeys, Is.LessThanOrEqualTo(100));
        }

        private static PeerId Peer(int port) => UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, port));

        private sealed class CountingTransport : IPeerTransport
        {
            public int Sent;
            public int Acceptances;      // plain JoinAccepted replies, the ones an address can ask for
            public bool IsBound => true;
            public PeerId LocalPeer => PeerId.None;
            public void Bind() { }
            public void Send(PeerId peer, byte[] data, int length)
            {
                Sent++;
                if (length > 0 && data[0] == new JoinAcceptedMessage().MessageType)
                    Acceptances++;
            }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => new TaskCompletionSource<(byte[], PeerId)>().Task;
            public void Dispose() { }
        }

        private static async Task<(NetworkSession Host, CountingTransport Transport)> HostAsync()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 200, 0, new Dictionary<string, object>());
            var transport = new CountingTransport();
            host.NetworkTransport = transport;
            return (host, transport);
        }

        private static void RequestJoin(NetworkSession host, string gamerId, PeerId from) =>
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(new JoinRequestMessage { GamerId = gamerId, Gamertag = gamerId }, from));

        [Test]
        public async Task OneAddress_FloodingJoinRequests_IsAnsweredAFewTimesAndThenIgnored()
        {
            var (host, transport) = await HostAsync();

            for (int i = 0; i < 100; i++)
                RequestJoin(host, "flood-" + i, Peer(5555));

            Assert.That(host.RemoteGamers.Count, Is.EqualTo(5), "only the burst may get in");
            Assert.That(transport.Acceptances, Is.EqualTo(5), "and the host must not answer the rest");
            await host.DisposeAsync();
        }

        [Test]
        public async Task ManyAddresses_CannotTogetherExceedTheHostsOverallCeiling()
        {
            var (host, transport) = await HostAsync();

            for (int i = 0; i < 500; i++)
                RequestJoin(host, "g" + i, Peer(10000 + i));

            Assert.That(host.RemoteGamers.Count, Is.InRange(25, 40), "about the overall burst of 30");
            await host.DisposeAsync();
        }

        [Test]
        public async Task AGenuineClientsRetries_StillGetThrough()
        {
            var (host, transport) = await HostAsync();

            // the same gamer asking three times, as JoinSessionAsync does when an acceptance is lost
            for (int i = 0; i < 3; i++)
                RequestJoin(host, "honest", Peer(7001));

            Assert.That(host.RemoteGamers.Single().GamerId, Is.EqualTo("honest"));
            Assert.That(transport.Acceptances, Is.EqualTo(3), "each retry is answered");
            await host.DisposeAsync();
        }
    }
}
