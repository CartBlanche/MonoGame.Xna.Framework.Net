using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Against the real Steam client. Needs Steam running and signed in, a <c>steam_appid.txt</c> (480 is
    /// Valve's public test app) next to the test assembly, and MGNET_STEAM_SMOKE=1. One account can only
    /// test the host side: Steam will not connect an account to itself, so finding and joining needs a
    /// second account on another machine.
    /// </summary>
    [TestFixture]
    public class SteamLobbySmokeTests
    {
        private CancellationTokenSource pump;
        private INetworkBackend original;

        [SetUp]
        public void SetUp()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("MGNET_STEAM_SMOKE"), "1", StringComparison.Ordinal))
                Assert.Ignore("Set MGNET_STEAM_SMOKE=1 to run the real Steam smoke tests.");

            Assert.That(SteamRuntime.Initialize(), Is.True, "Steam is not running, or steam_appid.txt is missing");
            original = NetworkServiceProvider.Backend;

            // Steam delivers every result through callbacks, which only fire while someone runs them
            pump = new CancellationTokenSource();
            var token = pump.Token;
            _ = Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    SteamRuntime.RunCallbacks();
                    Thread.Sleep(10);
                }
            });
        }

        [TearDown]
        public void TearDown()
        {
            pump?.Cancel();
            if (original != null)
                NetworkServiceProvider.SetBackend(original);
            SteamRuntime.Shutdown();
        }

        private static Task<T> CallResult<T>(SteamAPICall_t call) where T : struct
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = Steamworks.CallResult<T>.Create((r, ioFailure) =>
            {
                if (ioFailure) done.TrySetException(new InvalidOperationException("Steam call failed"));
                else done.TrySetResult(r);
            });
            result.Set(call);
            return done.Task;
        }

        // our own lobbies for this game tag, looked up the way a searcher would, as Steam sees them
        private static async Task<List<CSteamID>> LobbiesWithTagAsync(string tag)
        {
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListStringFilter("mgnet_game", tag, ELobbyComparison.k_ELobbyComparisonEqual);
            var list = await CallResult<LobbyMatchList_t>(SteamMatchmaking.RequestLobbyList());
            var lobbies = new List<CSteamID>();
            for (int i = 0; i < (int)list.m_nLobbiesMatching; i++)
                lobbies.Add(SteamMatchmaking.GetLobbyByIndex(i));
            return lobbies;
        }

        private static async Task<CSteamID> WaitForLobbyAsync(string tag)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var lobbies = await LobbiesWithTagAsync(tag);
                if (lobbies.Count > 0)
                    return lobbies[0];
                await Task.Delay(500);
            }
            Assert.Fail("the lobby never showed up in Steam's lobby list");
            return default;
        }

        [Test]
        [Category("Smoke")]
        public async Task AHostedSession_BecomesALobby_CarryingItsTypeAndProperties_AndKeepsThemCurrent()
        {
            string tag = "mgnet-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            NetworkServiceProvider.SetBackend(new SteamBackend(tag, SteamFallbackMode.Strict));

            NetworkSession host = await NetworkSession.CreateAsync(NetworkSessionType.PlayerMatch, 1, 4, 0,
                new Dictionary<string, object> { ["0"] = 7, ["mode"] = "ctf", ["ranked"] = true });
            try
            {
                CSteamID lobby = await WaitForLobbyAsync(tag);

                Assert.That(SteamMatchmaking.GetLobbyData(lobby, "mgnet_type"), Is.EqualTo("PlayerMatch"));
                Assert.That(SteamMatchmaking.GetLobbyData(lobby, "p.0"), Is.EqualTo("i:7"));
                Assert.That(SteamMatchmaking.GetLobbyData(lobby, "p.mode"), Is.EqualTo("s:ctf"));
                Assert.That(SteamMatchmaking.GetLobbyData(lobby, "p.ranked"), Is.EqualTo("b:1"));
                Assert.That(SteamMatchmaking.GetLobbyOwner(lobby), Is.EqualTo(SteamUser.GetSteamID()));

                // a searcher never sees its own lobby (a same-account join cannot work)
                using (var found = await NetworkSession.FindAsync(NetworkSessionType.PlayerMatch, 1, null))
                    Assert.That(found.Count, Is.Zero);

                // the host changes a property later: the lobby follows
                host.SessionProperties["mode"] = "dm";
                bool updated = false;
                for (int attempt = 0; attempt < 20 && !updated; attempt++)
                {
                    updated = SteamMatchmaking.GetLobbyData(lobby, "p.mode") == "s:dm";
                    if (!updated)
                        await Task.Delay(250);
                }
                Assert.That(updated, Is.True, "the lobby did not follow the session's properties");
            }
            finally
            {
                host.Dispose();
            }
        }

        [Test]
        [Category("Smoke")]
        public async Task AnInvitationToALobby_IsDescribedAsAJoinableSession()
        {
            string tag = "mgnet-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var backend = new SteamBackend(tag, SteamFallbackMode.Strict);
            NetworkServiceProvider.SetBackend(backend);
            NetworkSession host = await NetworkSession.CreateAsync(NetworkSessionType.Ranked, 1, 4, 0,
                new Dictionary<string, object> { ["0"] = 3 });
            try
            {
                CSteamID lobby = await WaitForLobbyAsync(tag);
                var invites = new TaskCompletionSource<InviteReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                backend.InviteReceived += (s, e) => invites.TrySetResult(e);

                // what Steam's own "join game" callback does once it has a lobby id
                await backend.RaiseInviteAsync(lobby);

                var invite = await invites.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(invite.Session.SessionId, Is.EqualTo(lobby.ToString()));
                Assert.That(invite.Session.SessionType, Is.EqualTo(NetworkSessionType.Ranked));
                Assert.That(invite.Session.SessionProperties["0"], Is.EqualTo(3));
                Assert.That(invite.Session.HostPeer, Is.EqualTo(SteamP2PTransport.ToPeer(SteamUser.GetSteamID())));
            }
            finally
            {
                host.Dispose();
            }
        }
    }
}
