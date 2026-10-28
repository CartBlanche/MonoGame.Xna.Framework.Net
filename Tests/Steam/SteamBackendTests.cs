using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The Steam back-end without Steam running: it falls back to the LAN, or, in strict mode, refuses.
    /// SystemLink uses fixed ports, so these must not run in parallel with other suites that host sessions.
    /// </summary>
    [TestFixture]
    public class SteamBackendTests
    {
        private INetworkBackend originalBackend;

        [SetUp]
        public void SetUp() => originalBackend = NetworkServiceProvider.Backend;

        [TearDown]
        public void TearDown() => NetworkServiceProvider.SetBackend(originalBackend);

        private static async Task<AvailableNetworkSession> FindHostAsync(NetworkSession host)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                using var found = await NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, null);
                var match = found.FirstOrDefault(s => s.HostGamertag == host.LocalGamers[0].Gamertag);
                if (match != null)
                    return match;
            }
            Assert.Fail("the host was never found");
            return null;
        }

        private static async Task PumpAsync(Func<bool> done, params NetworkSession[] sessions)
        {
            for (int i = 0; i < 300 && !done(); i++)
            {
                foreach (var session in sessions)
                    session.Update();
                await Task.Delay(10);
            }
        }

        [Test]
        public void TheBackend_IsNamedSteam()
        {
            Assert.That(new SteamBackend().Name, Is.EqualTo("Steam"));
        }

        [Test]
        public async Task WithoutSteam_ItFallsBackToTheLan_HostJoinAndData()
        {
            NetworkServiceProvider.SetBackend(new SteamBackend());
            var host = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null);
            NetworkSession client = null;
            try
            {
                client = await NetworkSession.JoinAsync(await FindHostAsync(host));
                await PumpAsync(() => host.AllGamers.Count == 2, host, client);
                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
                Assert.That(host.AllowHostMigration, Is.False);

                host.LocalGamers[0].SendData(new byte[] { 9 }, SendDataOptions.ReliableInOrder);
                await PumpAsync(() => client.LocalGamers[0].IsDataAvailable, host, client);
                Assert.That(client.LocalGamers[0].IsDataAvailable, Is.True);
            }
            finally
            {
                client?.Dispose();
                host.Dispose();
            }
        }

        [Test]
        public async Task WhenTheHostLeaves_TheClientEndsWithHostEndedSession()
        {
            NetworkServiceProvider.SetBackend(new SteamBackend());
            var host = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null);
            NetworkSession client = null;
            try
            {
                client = await NetworkSession.JoinAsync(await FindHostAsync(host));
                NetworkSessionEndReason? reason = null;
                client.SessionEnded += (_, e) => reason = e.EndReason;
                await PumpAsync(() => host.AllGamers.Count == 2, host, client);

                host.Leave();
                await PumpAsync(() => reason != null, client);

                Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));
            }
            finally
            {
                client?.Dispose();
                host.Dispose();
            }
        }

        [Test]
        public void StrictMode_WithoutSteam_RefusesToFindAndCreate()
        {
            NetworkServiceProvider.SetBackend(new SteamBackend(fallbackMode: SteamFallbackMode.Strict));

            Assert.ThrowsAsync<InvalidOperationException>(() => NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, null));
            Assert.ThrowsAsync<InvalidOperationException>(() => NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null));
        }

        [Test]
        public void OnlySteamPeers_AreSteamPeers()
        {
            Assert.That(SteamP2PTransport.TryGetSteamId(new PeerId("udp:1.2.3.4:5"), out _), Is.False);
            Assert.That(SteamP2PTransport.TryGetSteamId(new PeerId("steam:not-a-number"), out _), Is.False);
            Assert.That(SteamP2PTransport.TryGetSteamId(PeerId.None, out _), Is.False);
        }
    }
}
