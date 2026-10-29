using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Invitations the user accepts in a platform's own UI: raised from Update() on the game's thread,
    /// then joined with JoinInvitedAsync. Uses a back-end that raises them on demand and Local sessions,
    /// so nothing touches the network.
    /// </summary>
    [TestFixture]
    public class InviteTests
    {
        private sealed class InvitingBackend : INetworkBackend
        {
            public string Name => "Inviting";
            public bool SupportsSessionType(NetworkSessionType sessionType) => true;
            public event EventHandler<InviteReceivedEventArgs> InviteReceived;
            public IPeerTransport CreateTransport() => throw new NotSupportedException();
            public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken) => Task.CompletedTask;
            public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<AvailableNetworkSession>>(new List<AvailableNetworkSession>());
            public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) =>
                Task.FromResult<IDisposable>(null);

            public void Invite(AvailableNetworkSession session) => InviteReceived?.Invoke(this, new InviteReceivedEventArgs(session));
        }

        private InvitingBackend backend;
        private INetworkBackend original;
        private readonly List<NetworkSession> open = new List<NetworkSession>();
        private readonly List<EventHandler<InviteAcceptedEventArgs>> handlers = new List<EventHandler<InviteAcceptedEventArgs>>();

        [SetUp]
        public void SetUp()
        {
            original = NetworkServiceProvider.Backend;
            backend = new InvitingBackend();
            NetworkServiceProvider.SetBackend(backend);
            NetworkServiceProvider.TakePendingInvite();     // start with none waiting
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var handler in handlers)
                NetworkSession.InviteAccepted -= handler;
            handlers.Clear();
            foreach (var session in open)
                session.Dispose();
            open.Clear();
            NetworkServiceProvider.TakePendingInvite();
            NetworkServiceProvider.SetBackend(original);
        }

        private void Listen(EventHandler<InviteAcceptedEventArgs> handler)
        {
            handlers.Add(handler);
            NetworkSession.InviteAccepted += handler;
        }

        private async Task<(NetworkSession Host, AvailableNetworkSession Available)> LocalHostAsync()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            open.Add(host);
            var found = await NetworkSession.FindAsync(NetworkSessionType.Local, 1, null);
            return (host, found.Single(s => s.SessionId == HostSessionId(host, found)));
        }

        // the available session that is this host (there may be others left by earlier tests)
        private static string HostSessionId(NetworkSession host, AvailableNetworkSessionCollection found) =>
            found.First(s => s.HostGamertag == host.LocalGamers[0].Gamertag).SessionId;

        [Test]
        public async Task AnInvite_IsRaisedFromUpdate_NotWhenItArrives()
        {
            var (host, available) = await LocalHostAsync();
            var raised = new List<InviteAcceptedEventArgs>();
            Listen((s, e) => raised.Add(e));

            backend.Invite(available);
            Assert.That(raised, Is.Empty, "raised before the game's Update()");

            host.Update();
            Assert.That(raised.Count, Is.EqualTo(1));
            Assert.That(raised[0].Gamer, Is.Not.Null);
            host.Update();
            Assert.That(raised.Count, Is.EqualTo(1), "one invitation is raised once");
        }

        [Test]
        public void TheGamerServicesComponent_AlsoRaisesInvites_ForGamesWithoutASession()
        {
            var available = new AvailableNetworkSession("s", "h", 1, 3, 0, NetworkSessionType.Local, new Dictionary<string, object>(), "some-session");
            int raised = 0;
            Listen((s, e) => raised++);

            backend.Invite(available);
            new GamerServicesComponent(null).Update(new GameTime());

            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public async Task AnInviteToTheSessionYouAreIn_IsMarkedAsTheCurrentSession_AndIsNotLeftToJoin()
        {
            var (host, available) = await LocalHostAsync();
            InviteAcceptedEventArgs got = null;
            Listen((s, e) => got = e);

            backend.Invite(available);
            host.Update();

            Assert.That(got.IsCurrentSession, Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                NetworkSession.JoinInvitedAsync(new[] { SignedInGamer.Current }));
        }

        [Test]
        public void JoinInvited_WithNoInvitation_Throws_AndNeedsAGamer()
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => NetworkSession.JoinInvitedAsync(new[] { SignedInGamer.Current }));
            Assert.Throws<ArgumentException>(() => NetworkSession.JoinInvitedAsync(new SignedInGamer[0]));
        }

        [Test]
        public void ABackendThatDoesNotCarryASessionType_IsRefusedByName()
        {
            NetworkServiceProvider.SetBackend(new UdpBackend());

            var create = Assert.ThrowsAsync<NotSupportedException>(() => NetworkSession.CreateAsync(NetworkSessionType.PlayerMatch, 1, 4, 0, null));
            Assert.That(create.Message, Does.Contain("UDP/SystemLink").And.Contain("PlayerMatch"));
            Assert.ThrowsAsync<NotSupportedException>(() => NetworkSession.FindAsync(NetworkSessionType.Ranked, 1, null));
        }

        [Test]
        public void SessionProperties_RoundTripThroughTheirTextEncoding_KeepingTheirType()
        {
            foreach (object value in new object[] { 7, -3, true, false, "hello", "a:b:c", "" })
            {
                Assert.That(SessionPropertyEncoding.TryDecode(SessionPropertyEncoding.Encode(value), out var decoded), Is.True);
                Assert.That(decoded, Is.EqualTo(value));
            }

            SessionPropertyEncoding.TryDecode(SessionPropertyEncoding.Encode(7), out var integer);
            SessionPropertyEncoding.TryDecode(SessionPropertyEncoding.Encode("7"), out var text);
            Assert.That(integer, Is.Not.EqualTo(text), "the integer 7 is not the text \"7\"");
            Assert.That(SessionPropertyEncoding.TryDecode("garbage", out _), Is.False);
            Assert.That(SessionPropertyEncoding.TryDecode("i:notanumber", out _), Is.False);
        }
    }
}
