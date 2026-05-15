using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.iOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class IOSAchievementMediaProviderTests
    {
        [TearDown]
        public void TearDown()
        {
            IOSRuntime.Shutdown();
        }

        [Test]
        public async Task IOSAchievementMediaProvider_WhenRuntimeUnavailable_ReturnsNull()
        {
            var provider = new IOSAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.any");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task IOSAchievementMediaProvider_WhenClientReturnsNoIconBytes_ReturnsNull()
        {
            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(new StubAppleGameCenterClient(
                achievementProgress: new Dictionary<string, AppleGameCenterAchievementProgress>
                {
                    ["achievement.noicon"] = new AppleGameCenterAchievementProgress
                    {
                        Id = "achievement.noicon",
                        IconData = null,
                        IsUnlocked = false,
                        PercentComplete = 0f
                    }
                }));

            var provider = new IOSAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.noicon");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task IOSAchievementMediaProvider_WhenClientThrows_ReturnsNull()
        {
            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(new StubAppleGameCenterClient(
                achievementProgress: new Dictionary<string, AppleGameCenterAchievementProgress>(),
                throwOnProgress: true));

            var provider = new IOSAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.error");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task IOSAchievementMediaProvider_SuccessfulIconRetrievalReturnsIconPayload()
        {
            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(new StubAppleGameCenterClient(
                achievementProgress: new Dictionary<string, AppleGameCenterAchievementProgress>
                {
                    ["achievement.success"] = new AppleGameCenterAchievementProgress
                    {
                        Id = "achievement.success",
                        IconData = new byte[] { 1, 2, 3, 4 },
                        IconContentType = "image/png",
                        IsUnlocked = false,
                        PercentComplete = 10f
                    }
                }));

            var provider = new IOSAchievementMediaProvider();

            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.success");

            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.Data, Is.Not.Null);
            Assert.That(icon.Data.Length, Is.EqualTo(4));
            Assert.That(icon.ContentType, Is.EqualTo("image/png"));
            Assert.That(icon.CacheKey, Is.EqualTo("ios:achievement.success"));
        }

        [Test]
        public async Task IOSAchievementMediaProvider_ResultIsCached_PerAchievementKey()
        {
            var client = new StubAppleGameCenterClient(
                achievementProgress: new Dictionary<string, AppleGameCenterAchievementProgress>
                {
                    ["achievement.cached"] = new AppleGameCenterAchievementProgress
                    {
                        Id = "achievement.cached",
                        IconData = new byte[] { 9, 8, 7 },
                        IconContentType = "image/png",
                        IsUnlocked = false,
                        PercentComplete = 0f
                    }
                });

            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(client);

            var provider = new IOSAchievementMediaProvider();

            var first = await provider.GetIconAsync(SignedInGamer.Current, "achievement.cached");
            var second = await provider.GetIconAsync(SignedInGamer.Current, "achievement.cached");

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(client.ProgressCalls, Is.EqualTo(1));
        }

        private sealed class StubAppleGameCenterClient : IAppleGameCenterClient
        {
            private readonly IReadOnlyDictionary<string, AppleGameCenterAchievementProgress> achievementProgress;
            private readonly bool throwOnProgress;

            public StubAppleGameCenterClient(
                IReadOnlyDictionary<string, AppleGameCenterAchievementProgress> achievementProgress,
                bool throwOnProgress = false)
            {
                this.achievementProgress = achievementProgress;
                this.throwOnProgress = throwOnProgress;
            }

            public int ProgressCalls { get; private set; }

            public Task<AppleGameCenterPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new AppleGameCenterPlayer
                {
                    Id = "ios-player",
                    DisplayName = "IOSTester"
                });
            }

            public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<AppleGameCenterScoreEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<AppleGameCenterScoreEntry>>(Array.Empty<AppleGameCenterScoreEntry>());
            }

            public Task<IReadOnlyDictionary<string, AppleGameCenterAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
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

            public Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}