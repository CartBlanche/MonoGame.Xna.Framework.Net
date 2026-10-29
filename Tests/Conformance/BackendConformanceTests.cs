using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// What every <see cref="INetworkBackend"/> has to get right, written against the public XNA-shaped
    /// API only. Each back-end's test project includes this file and derives one concrete fixture that
    /// says how to create the back-end, so every back-end faces the same scenarios.
    /// </summary>
    /// <remarks>
    /// Two sessions run in one process, so a back-end that cannot talk to itself (the same Steam or Epic
    /// account on both ends) overrides <see cref="CanRunTwoSessionsInOneProcess"/>. A back-end whose
    /// runtime is not available falls back to the LAN, and the scenarios then check that fallback.
    /// SystemLink uses fixed ports: fixtures derived from this must not run in parallel.
    /// </remarks>
    public abstract class BackendConformanceTests
    {
        private const int TimeoutMs = 10000;

        private readonly List<NetworkSession> open = new List<NetworkSession>();
        private INetworkBackend original;

        /// <summary>Creates the back-end under test.</summary>
        protected abstract INetworkBackend CreateBackend();

        /// <summary>False when two sessions cannot reach each other from one process.</summary>
        protected virtual bool CanRunTwoSessionsInOneProcess => true;

        /// <summary>False when the back-end does not carry <see cref="NetworkSessionProperties"/> to searchers.</summary>
        protected virtual bool CarriesSessionProperties => true;

        /// <summary>False when a full session is still listed by searches.</summary>
        protected virtual bool StopsAdvertisingWhenFull => true;

        [SetUp]
        public void SetUp()
        {
            original = NetworkServiceProvider.Backend;
            NetworkServiceProvider.SetBackend(CreateBackend());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (NetworkSession session in Enumerable.Reverse(open))
            {
                try { session.Dispose(); }
                catch { }
            }
            open.Clear();
            NetworkServiceProvider.SetBackend(original);
        }

        // --- helpers ---

        private Task<NetworkSession> HostAsync(int maxGamers = 4, NetworkSessionProperties properties = null)
        {
            NetworkSession host = NetworkSession.Create(NetworkSessionType.SystemLink, 1, maxGamers, 0, properties);
            open.Add(host);
            return Task.FromResult(host);
        }

        private static string TagOf(NetworkSession session) => session.LocalGamers[0].Gamertag;

        private static async Task<AvailableNetworkSession> TryFindAsync(NetworkSession host, NetworkSessionProperties search = null, int timeoutMs = TimeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                using (var found = await Task.Run(() => NetworkSession.Find(NetworkSessionType.SystemLink, 1, search)))
                {
                    var match = found.FirstOrDefault(s => s.HostGamertag == TagOf(host));
                    if (match != null)
                        return match;
                }
            }
            return null;
        }

        private async Task<NetworkSession> JoinAsync(NetworkSession host)
        {
            AvailableNetworkSession target = await TryFindAsync(host);
            Assert.That(target, Is.Not.Null, "the hosted session was never found");
            NetworkSession client = await NetworkSession.JoinAsync(target);
            open.Add(client);
            return client;
        }

        private async Task<bool> PumpUntilAsync(Func<bool> condition, int timeoutMs = TimeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                foreach (NetworkSession session in open.ToArray())
                    session.Update();
                if (condition())
                    return true;
                await Task.Delay(10);
            }
            return condition();
        }

        private void RequireLoopback()
        {
            if (!CanRunTwoSessionsInOneProcess)
                Assert.Ignore("This back-end cannot connect two sessions from one process (needs two accounts).");
        }

        // --- scenarios ---

        [Test]
        public async Task AHostedSession_IsFoundBySearch()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();

            AvailableNetworkSession found = await TryFindAsync(host);

            Assert.That(found, Is.Not.Null);
            Assert.That(found.HostGamertag, Is.EqualTo(TagOf(host)));
            Assert.That(found.HostPeer.IsValid, Is.True, "a found session must say how to reach its host");
        }

        [Test]
        public async Task AClientThatJoins_AndTheHost_SeeEachOther()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();
            NetworkSession client = await JoinAsync(host);

            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2 && client.AllGamers.Count == 2),
                "both sides should see two gamers");
            Assert.That(client.AllGamers.Host.GamerId, Is.EqualTo(host.LocalGamers[0].GamerId));
            Assert.That(client.AllGamers.Host.IsLocal, Is.False);
        }

        [Test]
        public async Task ReliableData_FlowsInBothDirections()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();
            NetworkSession client = await JoinAsync(host);
            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2));

            // to the other gamer: data for everyone would also come back to the sender
            host.LocalGamers[0].SendData(new byte[] { 1, 2, 3 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
            client.LocalGamers[0].SendData(new byte[] { 9, 8 }, SendDataOptions.ReliableInOrder, client.Host);

            Assert.That(await PumpUntilAsync(() => client.LocalGamers[0].IsDataAvailable && host.LocalGamers[0].IsDataAvailable));
            var buffer = new byte[16];
            int fromHost = client.LocalGamers[0].ReceiveData(buffer, out NetworkGamer sender);
            Assert.That(buffer.Take(fromHost), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(sender.IsHost, Is.True);
            int fromClient = host.LocalGamers[0].ReceiveData(buffer, out sender);
            Assert.That(buffer.Take(fromClient), Is.EqualTo(new byte[] { 9, 8 }));
            Assert.That(sender.IsLocal, Is.False);
        }

        [Test]
        public async Task ALargeReliableMessage_ArrivesWhole()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();
            NetworkSession client = await JoinAsync(host);
            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2));

            var message = new byte[60 * 1024];
            new Random(7).NextBytes(message);
            host.LocalGamers[0].SendData(message, SendDataOptions.ReliableInOrder);

            Assert.That(await PumpUntilAsync(() => client.LocalGamers[0].IsDataAvailable), "the message never arrived");
            var buffer = new byte[message.Length + 16];
            int length = client.LocalGamers[0].ReceiveData(buffer, out _);
            Assert.That(buffer.Take(length), Is.EqualTo(message));
        }

        [Test]
        public async Task AClientLeaving_IsReportedToTheHost()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();
            NetworkSession client = await JoinAsync(host);
            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2));
            int left = 0;
            host.GamerLeft += (s, e) => left++;

            client.Leave();

            Assert.That(await PumpUntilAsync(() => left == 1), "GamerLeft was not raised on the host");
            Assert.That(host.AllGamers.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task TheHostLeaving_EndsTheClientsSession()
        {
            RequireLoopback();
            NetworkSession host = await HostAsync();
            NetworkSession client = await JoinAsync(host);
            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2));
            NetworkSessionEndReason? reason = null;
            client.SessionEnded += (s, e) => reason = e.EndReason;

            host.Leave();

            Assert.That(await PumpUntilAsync(() => reason != null), "the client was never told");
            Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));
        }

        [Test]
        public async Task AFullSession_IsNoLongerAdvertised()
        {
            RequireLoopback();
            if (!StopsAdvertisingWhenFull)
                Assert.Ignore("This back-end lists full sessions.");
            NetworkSession host = await HostAsync(maxGamers: 2);
            await JoinAsync(host);
            Assert.That(await PumpUntilAsync(() => host.AllGamers.Count == 2));

            Assert.That(await TryFindAsync(host, timeoutMs: 3000), Is.Null);
        }

        [Test]
        public async Task SearchProperties_NarrowTheResults()
        {
            RequireLoopback();
            if (!CarriesSessionProperties)
                Assert.Ignore("This back-end does not carry session properties to searchers.");
            NetworkSession host = await HostAsync(properties: new NetworkSessionProperties { [0] = 7 });

            Assert.That(await TryFindAsync(host, new NetworkSessionProperties { [0] = 7 }), Is.Not.Null);
            Assert.That(await TryFindAsync(host, new NetworkSessionProperties { [0] = 8 }, timeoutMs: 3000), Is.Null);
        }
    }
}
