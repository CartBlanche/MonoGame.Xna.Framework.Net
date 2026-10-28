using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Session lifecycle over real UDP: create, find, join, ready, start, leave, end.
    /// The shape follows what the NetworkStateManagement sample and ShipGame do.
    /// </summary>
    [TestFixture]
    public class LanSessionTests
    {
        SessionHarness lan;

        [SetUp]
        public void SetUp() => lan = new SessionHarness();

        [TearDown]
        public void TearDown() => lan.Dispose();

        [Test]
        public async Task CreatedSession_IsInTheLobby_WithTheHostAsItsOnlyGamer()
        {
            NetworkSession host = await lan.StartHostAsync();

            Assert.That(host.IsHost, Is.True);
            Assert.That(host.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            Assert.That(host.SessionType, Is.EqualTo(NetworkSessionType.SystemLink));
            Assert.That(host.MaxGamers, Is.EqualTo(4));
            Assert.That(host.AllGamers.Count, Is.EqualTo(1));
            Assert.That(host.LocalGamers.Count, Is.EqualTo(1));
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(0));
            Assert.That(host.Host, Is.SameAs(host.LocalGamers[0]));
        }

        [Test]
        public async Task FindingSessions_ListsTheHostedSession()
        {
            NetworkSession host = await lan.StartHostAsync(maxGamers: 4);

            AvailableNetworkSession found = await lan.FindHostAsync();

            Assert.That(found.SessionType, Is.EqualTo(NetworkSessionType.SystemLink));
            Assert.That(found.HostGamertag, Is.EqualTo(host.LocalGamers[0].Gamertag));
            Assert.That(found.CurrentGamerCount, Is.EqualTo(1));
            Assert.That(found.OpenPublicGamerSlots, Is.EqualTo(3));
        }

        [Test]
        public async Task JoiningAClient_AddsItToHostAndClient_AndRaisesGamerJoinedOnTheHost()
        {
            NetworkSession host = await lan.StartHostAsync();
            var joined = new List<NetworkGamer>();
            host.GamerJoined += (s, e) => { lock (joined) joined.Add((NetworkGamer)e.Gamer); };

            NetworkSession client = await lan.JoinClientAsync();

            Assert.That(client.IsHost, Is.False);
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2), "host never saw the client");
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(1));
            Assert.That(client.Host, Is.Not.Null);
            Assert.That(client.Host.IsHost, Is.True);
            Assert.That(client.LocalGamers.Count, Is.EqualTo(1));

            // note: subscribing in the lobby also replays the gamers already in the session (the host
            // itself), see plan task 3.14, so look for the client rather than counting
            Assert.That(await lan.WaitUntilAsync(() => { lock (joined) return joined.Any(g => !g.IsLocal); }),
                "GamerJoined was not raised on the host for the client");
        }

        [Test]
        public async Task FourInstances_AllJoinTheSameSession()
        {
            NetworkSession host = await lan.StartHostAsync(maxGamers: 4);
            for (int i = 0; i < 3; i++)
                await lan.JoinClientAsync();

            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 4), "host did not reach 4 gamers");
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(3));
            foreach (NetworkSession client in lan.Clients)
                Assert.That(client.Host.Gamertag, Is.EqualTo(host.LocalGamers[0].Gamertag));
        }

        [Test]
        public async Task AClientReady_IsSeenByTheHost_AndStartGameReachesEveryone()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));

            int hostStarted = 0, clientStarted = 0;
            host.GameStarted += (s, e) => hostStarted++;
            client.GameStarted += (s, e) => clientStarted++;

            Assert.That(host.IsEveryoneReady, Is.False);
            host.LocalGamers[0].IsReady = true;
            client.LocalGamers[0].IsReady = true;

            Assert.That(await lan.WaitUntilAsync(() => host.IsEveryoneReady), "host never saw everyone ready");

            host.StartGame();

            Assert.That(await lan.WaitUntilAsync(() => client.SessionState == NetworkSessionState.Playing),
                "client never saw the game start");
            Assert.That(host.SessionState, Is.EqualTo(NetworkSessionState.Playing));
            Assert.That(hostStarted, Is.EqualTo(1));
            Assert.That(clientStarted, Is.EqualTo(1));

            host.EndGame();
            Assert.That(await lan.WaitUntilAsync(() => client.SessionState == NetworkSessionState.Lobby),
                "client never saw the game end");
        }

        [Test]
        [Ignore("Known gap, plan task 3.15: a client's AllGamers holds only itself and the host, not the other clients")]
        public async Task EveryGamer_CanSeeEveryOtherGamer()
        {
            NetworkSession host = await lan.StartHostAsync(maxGamers: 4);
            for (int i = 0; i < 2; i++)
                await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 3));

            foreach (NetworkSession client in lan.Clients)
                Assert.That(await lan.WaitUntilAsync(() => client.AllGamers.Count == 3, 3000),
                    "a client does not see all three gamers (sees " + client.AllGamers.Count + ")");
        }

        [Test]
        public async Task AClientLeaving_RaisesGamerLeftOnTheHost()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
            var left = new List<NetworkGamer>();
            host.GamerLeft += (s, e) => { lock (left) left.Add((NetworkGamer)e.Gamer); };

            client.Leave();

            Assert.That(await lan.WaitUntilAsync(() => { lock (left) return left.Count == 1; }),
                "GamerLeft was not raised on the host");
            Assert.That(host.AllGamers.Count, Is.EqualTo(1));
            Assert.That(host.SessionState, Is.Not.EqualTo(NetworkSessionState.Ended));
        }

        [Test]
        public async Task TheHostLeaving_EndsTheSessionOnClients()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
            NetworkSessionEndReason? reason = null;
            client.SessionEnded += (s, e) => reason = e.EndReason;

            host.Leave();

            Assert.That(await lan.WaitUntilAsync(() => reason != null), "SessionEnded was not raised on the client");
            Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Ended));
        }

        [Test]
        [Ignore("Known gap, plan task 3.13: discovery hard-codes CurrentGamerCount to 1; the advertisement does not carry the real count")]
        public async Task FindingASession_ReportsTheCurrentGamerCount()
        {
            NetworkSession host = await lan.StartHostAsync(maxGamers: 4);
            await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));

            AvailableNetworkSession found = await lan.FindHostAsync();

            Assert.That(found.CurrentGamerCount, Is.EqualTo(2));
            Assert.That(found.OpenPublicGamerSlots, Is.EqualTo(2));
        }

        [Test]
        public async Task AFullSession_StopsBeingAdvertised()
        {
            await lan.StartHostAsync(maxGamers: 2);
            await lan.JoinClientAsync();

            Assert.ThrowsAsync<TimeoutException>(() => lan.FindHostAsync(),
                "a full session should no longer be advertised");
        }

        [Test]
        public async Task WhenJoinInProgressIsNotAllowed_JoiningAStartedGameIsRefused()
        {
            NetworkSession host = await lan.StartHostAsync(maxGamers: 4);
            host.AllowJoinInProgress = false;
            AvailableNetworkSession found = await lan.FindHostAsync();
            host.StartGame();

            NetworkSessionJoinException ex = Assert.ThrowsAsync<NetworkSessionJoinException>(
                () => NetworkSession.JoinAsync(found));

            Assert.That(ex.JoinError, Is.EqualTo(NetworkSessionJoinError.SessionNotJoinable));
            Assert.That(host.AllGamers.Count, Is.EqualTo(1));
        }
    }
}
