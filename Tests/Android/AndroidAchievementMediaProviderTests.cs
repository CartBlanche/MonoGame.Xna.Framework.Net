using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.Android;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class AndroidAchievementMediaProviderTests
    {
        [TearDown]
        public void TearDown()
        {
            AndroidRuntime.Shutdown();
        }

        [Test]
        public async Task AndroidAchievementMediaProvider_WhenRuntimeUnavailable_ReturnsNull()
        {
            var provider = new AndroidAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.any");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task AndroidAchievementMediaProvider_WhenClientReturnsNoIconUrl_ReturnsNull()
        {
            AndroidRuntime.SetGooglePlayGamesClient(new StubGooglePlayGamesClient(
                achievementProgress: new Dictionary<string, GooglePlayGamesAchievementProgress>
                {
                    ["achievement.noicon"] = new GooglePlayGamesAchievementProgress
                    {
                        Id = "achievement.noicon",
                        IconUrl = string.Empty,
                        IsUnlocked = false,
                        PercentComplete = 0f
                    }
                }));

            var provider = new AndroidAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.noicon");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task AndroidAchievementMediaProvider_WhenClientThrows_ReturnsNull()
        {
            AndroidRuntime.SetGooglePlayGamesClient(new StubGooglePlayGamesClient(
                achievementProgress: new Dictionary<string, GooglePlayGamesAchievementProgress>(),
                throwOnProgress: true));

            var provider = new AndroidAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.error");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task AndroidAchievementMediaProvider_NullResultIsCached_PerAchievementKey()
        {
            var client = new StubGooglePlayGamesClient(
                achievementProgress: new Dictionary<string, GooglePlayGamesAchievementProgress>
                {
                    ["achievement.cached"] = new GooglePlayGamesAchievementProgress
                    {
                        Id = "achievement.cached",
                        IconUrl = string.Empty,
                        IsUnlocked = false,
                        PercentComplete = 0f
                    }
                });

            AndroidRuntime.SetGooglePlayGamesClient(client);

            var provider = new AndroidAchievementMediaProvider();

            var first = await provider.GetIconAsync(SignedInGamer.Current, "achievement.cached");
            var second = await provider.GetIconAsync(SignedInGamer.Current, "achievement.cached");

            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(client.ProgressCalls, Is.EqualTo(1));
        }

        private sealed class StubGooglePlayGamesClient : IGooglePlayGamesClient
        {
            private readonly IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress> achievementProgress;
            private readonly bool throwOnProgress;

            public StubGooglePlayGamesClient(
                IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress> achievementProgress,
                bool throwOnProgress = false)
            {
                this.achievementProgress = achievementProgress;
                this.throwOnProgress = throwOnProgress;
            }

            public int ProgressCalls { get; private set; }

            public Task<GooglePlayGamesPlayer> GetCurrentPlayerAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new GooglePlayGamesPlayer
                {
                    Id = "android-player",
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
                ProgressCalls++;

                if (throwOnProgress)
                {
                    throw new InvalidOperationException("Simulated progress failure.");
                }

                return Task.FromResult(achievementProgress);
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