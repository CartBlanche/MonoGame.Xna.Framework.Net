using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Session types beyond SystemLink and joining from an invitation, over real UDP loopback. The LAN
    /// carries SystemLink only, so a stand-in back-end that reuses the LAN transport but claims to carry
    /// every type (and can raise an invitation) plays the part of an online back-end such as Steam.
    /// SystemLink uses fixed ports: these must not run in parallel with other session tests.
    /// </summary>
    [TestFixture]
    public class OnlineSessionTypeTests
    {
        private sealed class OnlineLikeBackend : INetworkBackend
        {
            private readonly UdpBackend lan = new UdpBackend();

            // when set, found sessions are labelled with this type whatever was asked for
            public NetworkSessionType? LabelAs;

            public string Name => "OnlineLike";
            public bool SupportsSessionType(NetworkSessionType sessionType) => true;
            public event EventHandler<InviteReceivedEventArgs> InviteReceived;

            public IPeerTransport CreateTransport() => lan.CreateTransport();
            public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken) =>
                lan.AdvertiseAsync(session, transport, stopToken);

            // the LAN advertisement carries no session type, so the stand-in labels what it finds with the type asked for
            public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
            {
                var found = await lan.FindAsync(NetworkSessionType.SystemLink, maxLocalGamers, cancellationToken);
                return found.Select(s => new AvailableNetworkSession(s.SessionName, s.HostGamertag, s.CurrentGamerCount, s.OpenPublicGamerSlots,
                    s.OpenPrivateGamerSlots, LabelAs ?? sessionType, s.SessionProperties, s.SessionId, s.HostPeer)).ToList();
            }

            public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) =>
                lan.PrepareJoinAsync(session, transport, cancellationToken);

            public void Invite(AvailableNetworkSession session) => InviteReceived?.Invoke(this, new InviteReceivedEventArgs(session));
        }

        private OnlineLikeBackend backend;
        private INetworkBackend original;
        private readonly List<NetworkSession> open = new List<NetworkSession>();

        [SetUp]
        public void SetUp()
        {
            original = NetworkServiceProvider.Backend;
            backend = new OnlineLikeBackend();
            NetworkServiceProvider.SetBackend(backend);
            NetworkServiceProvider.ResetToDefault();
            NetworkServiceProvider.SetBackend(backend);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var session in Enumerable.Reverse(open))
            {
                try { session.Dispose(); }
                catch { }
            }
            open.Clear();
            NetworkServiceProvider.SetBackend(original);
        }

        private static async Task<AvailableNetworkSession> FindAsync(NetworkSessionType type, NetworkSession host)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                using (var found = await NetworkSession.FindAsync(type, 1, null))
                {
                    var match = found.FirstOrDefault(s => s.HostGamertag == host.LocalGamers[0].Gamertag);
                    if (match != null)
                        return match;
                }
            }
            return null;
        }

        [TestCase(NetworkSessionType.PlayerMatch)]
        [TestCase(NetworkSessionType.Ranked)]
        public async Task ABackendThatCarriesTheType_HostsFindsAndJoinsIt(NetworkSessionType type)
        {
            NetworkSession host = await NetworkSession.CreateAsync(type, 1, 4, 0, null);
            open.Add(host);

            AvailableNetworkSession found = await FindAsync(type, host);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.SessionType, Is.EqualTo(type));

            NetworkSession client = await NetworkSession.JoinAsync(found);
            open.Add(client);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (host.AllGamers.Count < 2 && DateTime.UtcNow < deadline)
            {
                host.Update();
                await Task.Delay(10);
            }
            Assert.That(host.AllGamers.Count, Is.EqualTo(2));
            Assert.That(host.SessionType, Is.EqualTo(type));
        }

        [Test]
        public async Task SessionsOfAnotherType_AreLeftOutOfASearch()
        {
            NetworkSession host = await NetworkSession.CreateAsync(NetworkSessionType.PlayerMatch, 1, 4, 0, null);
            open.Add(host);
            Assert.That(await FindAsync(NetworkSessionType.PlayerMatch, host), Is.Not.Null);

            // the back-end now reports what it has as Ranked sessions: a PlayerMatch search must not list them
            backend.LabelAs = NetworkSessionType.Ranked;
            using (var found = await NetworkSession.FindAsync(NetworkSessionType.PlayerMatch, 1, null))
                Assert.That(found.Count, Is.Zero);
        }

        [Test]
        public async Task AnAcceptedInvitation_IsJoinedWithJoinInvited()
        {
            NetworkSession host = await NetworkSession.CreateAsync(NetworkSessionType.PlayerMatch, 1, 4, 0, null);
            open.Add(host);
            AvailableNetworkSession onTheLan = await FindAsync(NetworkSessionType.PlayerMatch, host);
            Assert.That(onTheLan, Is.Not.Null);

            // the host here is in this same process, which would make this invitation "to the session you are
            // already in"; the invitation carries the platform's own id for the session instead
            var invitedTo = new AvailableNetworkSession(onTheLan.SessionName, onTheLan.HostGamertag, onTheLan.CurrentGamerCount,
                onTheLan.OpenPublicGamerSlots, onTheLan.OpenPrivateGamerSlots, onTheLan.SessionType, onTheLan.SessionProperties,
                "platform-lobby-42", onTheLan.HostPeer);

            // a second machine's game: it has no session yet, only the invitation the platform UI delivered
            InviteAcceptedEventArgs got = null;
            EventHandler<InviteAcceptedEventArgs> handler = (s, e) => got = e;
            NetworkSession.InviteAccepted += handler;
            try
            {
                backend.Invite(invitedTo);
                new GamerServicesComponent(null).Update(new GameTime());
                Assert.That(got, Is.Not.Null, "the invitation was never announced");

                NetworkSession client = await NetworkSession.JoinInvitedAsync(new[] { SignedInGamer.Current });
                open.Add(client);

                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (host.AllGamers.Count < 2 && DateTime.UtcNow < deadline)
                {
                    host.Update();
                    await Task.Delay(10);
                }
                Assert.That(host.AllGamers.Count, Is.EqualTo(2), "the invited client never arrived");
            }
            finally
            {
                NetworkSession.InviteAccepted -= handler;
            }
        }
    }
}
