using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.iOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class IOSProvidersTests
    {
        [SetUp]
        public void SetUp()
        {
            IOSRuntime.Shutdown();
            AchievementCatalog.Clear();
            SignedInGamer.Current.SetSignedInToLive(false);
            SignedInGamer.Current.SetGamertag("iOSProviderTester");
        }

        [TearDown]
        public void TearDown()
        {
            IOSRuntime.Shutdown();
            AchievementCatalog.Clear();
            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [Test]
        public async Task IOSLeaderboardProvider_WhenClientAvailable_UsesRemoteReadAndSubmit()
        {
            var client = new StubAppleGameCenterClient
            {
                TopScores = new List<AppleGameCenterScoreEntry>
                {
                    new AppleGameCenterScoreEntry { Rank = 1, PlayerDisplayName = "Alpha", Score = 9000, IsCurrentPlayer = false },
                    new AppleGameCenterScoreEntry { Rank = 2, PlayerDisplayName = "Beta", Score = 8500, IsCurrentPlayer = true }
                }
            };

            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(client);

            var gamer = SignedInGamer.Current;
            var identity = new LeaderboardIdentity($"ios.lb.remote.{Guid.NewGuid():N}");
            var provider = new IOSLeaderboardProvider();

            await provider.SubmitAsync(new LeaderboardWriter(identity, gamer) { Score = 7777 });
            using var reader = await provider.ReadAsync(identity, pageStart: 0, pageSize: 10, pivotGamer: null);

            Assert.That(client.SubmittedLeaderboardId, Is.EqualTo(identity.Key));
            Assert.That(client.SubmittedScore, Is.EqualTo(7777));
            Assert.That(reader.Count, Is.EqualTo(2));
            Assert.That(reader[0].Gamertag, Is.EqualTo("Alpha"));
            Assert.That(reader[0].Score, Is.EqualTo(9000));
        }

        [Test]
        public async Task IOSLeaderboardProvider_WhenRemoteReadFails_FallsBackToLocalProjection()
        {
            var client = new StubAppleGameCenterClient { ThrowOnTopScores = true };

            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(client);

            var gamer = SignedInGamer.Current;
            var identity = new LeaderboardIdentity($"ios.lb.local.{Guid.NewGuid():N}");
            var provider = new IOSLeaderboardProvider();

            await provider.SubmitAsync(new LeaderboardWriter(identity, gamer) { Score = 4321 });
            using var reader = await provider.ReadAsync(identity, pageStart: 0, pageSize: 10, pivotGamer: gamer);

            Assert.That(reader.Count, Is.EqualTo(1));
            Assert.That(reader[0].Score, Is.EqualTo(4321));
            Assert.That(reader[0].Gamertag, Is.EqualTo(gamer.Gamertag));
        }

        [Test]
        public async Task IOSAchievementProvider_WhenClientAvailable_UsesRemoteProgressAndMetadata()
        {
            var key = $"ios.achievement.{Guid.NewGuid():N}";
            AchievementCatalog.Register(new AchievementDefinition(
                key: key,
                displayName: "Remote Achievement",
                description: "Remote description",
                howToEarn: "Do the thing",
                gamerScore: 15,
                isHidden: true,
                iconKey: "icon.key",
                iconUri: "https://example.test/icon.png"));

            var client = new StubAppleGameCenterClient
            {
                AchievementProgress = new Dictionary<string, AppleGameCenterAchievementProgress>
                {
                    [key] = new AppleGameCenterAchievementProgress
                    {
                        Id = key,
                        IsUnlocked = true,
                        PercentComplete = 100f,
                        LastUpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    }
                }
            };

            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(client);

            var provider = new IOSAchievementProvider();
            var achievements = await provider.GetAchievementsAsync(SignedInGamer.Current);
            var achievement = achievements.Single(a => a.Key == key);

            Assert.That(achievement.IsEarned, Is.True);
            Assert.That(achievement.PercentComplete, Is.EqualTo(100f));
            Assert.That(achievement.IsHidden, Is.True);
            Assert.That(achievement.GamerScore, Is.EqualTo(15));
        }

        [Test]
        public async Task IOSAchievementProvider_WhenRuntimeUnavailable_TracksLocalProgress()
        {
            var key = $"ios.achievement.local.{Guid.NewGuid():N}";
            AchievementCatalog.Register(new AchievementDefinition(
                key: key,
                displayName: "Local Achievement",
                description: "Local description",
                howToEarn: "Do local thing",
                gamerScore: 5,
                isHidden: false,
                iconKey: null,
                iconUri: null));

            var provider = new IOSAchievementProvider();
            await provider.SetProgressAsync(SignedInGamer.Current, key, 100f);

            var achievements = await provider.GetAchievementsAsync(SignedInGamer.Current);
            var achievement = achievements.Single(a => a.Key == key);

            Assert.That(achievement.IsEarned, Is.True);
            Assert.That(achievement.PercentComplete, Is.EqualTo(100f));
        }

        private sealed class StubAppleGameCenterClient : IAppleGameCenterClient
        {
            public IReadOnlyList<AppleGameCenterScoreEntry> TopScores { get; set; } = Array.Empty<AppleGameCenterScoreEntry>();
            public IReadOnlyDictionary<string, AppleGameCenterAchievementProgress> AchievementProgress { get; set; } =
                new Dictionary<string, AppleGameCenterAchievementProgress>();

            public bool ThrowOnTopScores { get; set; }
            public bool ThrowOnProgress { get; set; }

            public string SubmittedLeaderboardId { get; private set; } = string.Empty;
            public long SubmittedScore { get; private set; }

            public Task<AppleGameCenterPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new AppleGameCenterPlayer { Id = "ios-provider", DisplayName = "iOS Provider" });
            }

            public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SubmittedLeaderboardId = leaderboardId;
                SubmittedScore = score;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<AppleGameCenterScoreEntry>> GetTopScoresAsync(
                string leaderboardId,
                int maxResults,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ThrowOnTopScores)
                {
                    throw new InvalidOperationException("Simulated top score failure.");
                }

                return Task.FromResult<IReadOnlyList<AppleGameCenterScoreEntry>>(TopScores.Take(maxResults).ToList());
            }

            public Task<IReadOnlyDictionary<string, AppleGameCenterAchievementProgress>> GetAchievementProgressAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ThrowOnProgress)
                {
                    throw new InvalidOperationException("Simulated progress failure.");
                }

                return Task.FromResult(AchievementProgress);
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
