using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            EOSRuntime.Shutdown();
            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [Test]
        public async Task SetEpicOnlineServicesClient_ThenSignInAsync_UsesInjectedClient()
        {
            EOSRuntime.Initialize();
            EOSRuntime.SetEpicOnlineServicesClient(new StubEpicOnlineServicesClient());

            var signedIn = await EOSRuntime.SignInAsync();

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(SignedInGamer.Current.Gamertag, Is.EqualTo("EOSTester"));
        }

        private sealed class StubEpicOnlineServicesClient : IEpicOnlineServicesClient
        {
            public Task<EpicAccountPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new EpicAccountPlayer
                {
                    Id = "eos-player-1",
                    DisplayName = "EOSTester"
                });
            }

            public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<EpicLeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<EpicLeaderboardEntry>>(Array.Empty<EpicLeaderboardEntry>());
            }

            public Task<IReadOnlyDictionary<string, EpicAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyDictionary<string, EpicAchievementProgress>>(new Dictionary<string, EpicAchievementProgress>());
            }

            public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
