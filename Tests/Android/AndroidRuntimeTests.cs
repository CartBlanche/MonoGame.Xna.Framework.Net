using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Android;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class AndroidRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            AndroidRuntime.Shutdown();
        }

        [Test]
        public void SetGooglePlayGamesClient_ThenTryGet_ReturnsInjectedClient()
        {
            var client = new StubGooglePlayGamesClient();
            AndroidRuntime.SetGooglePlayGamesClient(client);

            Assert.That(AndroidRuntime.TryGetGooglePlayGamesClient(out var resolvedClient), Is.True);
            Assert.That(resolvedClient, Is.SameAs(client));
        }

        private sealed class StubGooglePlayGamesClient : IGooglePlayGamesClient
        {
            public Task<GooglePlayGamesPlayer> GetCurrentPlayerAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new GooglePlayGamesPlayer
                {
                    Id = "android-player-1",
                    DisplayName = "AndroidTester"
                });
            }

            public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<GooglePlayGamesScoreEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<GooglePlayGamesScoreEntry>>(Array.Empty<GooglePlayGamesScoreEntry>());
            }

            public Task<IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress>>(new Dictionary<string, GooglePlayGamesAchievementProgress>());
            }

            public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task IncrementAchievementAsync(string achievementId, int steps, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
