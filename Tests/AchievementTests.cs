using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class AchievementTests
    {
        private IAchievementProvider originalLiveProvider;
        private IAchievementProvider originalLocalProvider;
        private IAchievementMediaProvider originalMediaLiveProvider;
        private IAchievementMediaProvider originalMediaLocalProvider;

        [SetUp]
        public void SetUp()
        {
            originalLiveProvider = AchievementService.LiveProvider;
            originalLocalProvider = AchievementService.LocalProvider;

            AchievementService.LiveProvider = null;
            AchievementService.LocalProvider = new InMemoryAchievementProvider();
            originalMediaLiveProvider = AchievementMediaService.LiveProvider;
            originalMediaLocalProvider = AchievementMediaService.LocalProvider;
            AchievementMediaService.LiveProvider = null;
            AchievementMediaService.LocalProvider = new InMemoryAchievementMediaProvider();
            AchievementCatalog.Clear();

            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [TearDown]
        public void TearDown()
        {
            AchievementService.LiveProvider = originalLiveProvider;
            AchievementService.LocalProvider = originalLocalProvider;
            AchievementMediaService.LiveProvider = originalMediaLiveProvider;
            AchievementMediaService.LocalProvider = originalMediaLocalProvider;
            AchievementCatalog.Clear();
            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [Test]
        public async Task UnlockAchievementAsync_SetsEarnedAndProgressTo100()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.unlock.{Guid.NewGuid():N}";

            await gamer.UnlockAchievementAsync(key);
            var achievements = await gamer.GetAchievementsAsync();
            var unlocked = achievements[key];

            Assert.That(unlocked, Is.Not.Null);
            Assert.That(unlocked.IsEarned, Is.True);
            Assert.That(unlocked.PercentComplete, Is.EqualTo(100f));
            Assert.That(unlocked.EarnedDate, Is.Not.Null);
        }

        [Test]
        public async Task SetAchievementProgressAsync_TracksHighestProgressOnly()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.progress.{Guid.NewGuid():N}";

            await gamer.SetAchievementProgressAsync(key, 25f);
            await gamer.SetAchievementProgressAsync(key, 10f);

            var achievements = await gamer.GetAchievementsAsync();
            var value = achievements[key];

            Assert.That(value, Is.Not.Null);
            Assert.That(value.IsEarned, Is.False);
            Assert.That(value.PercentComplete, Is.EqualTo(25f));
        }

        [Test]
        public async Task SetAchievementProgressAsync_Reaching100Unlocks()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.complete.{Guid.NewGuid():N}";

            await gamer.SetAchievementProgressAsync(key, 100f);

            var achievements = await gamer.GetAchievementsAsync();
            var value = achievements[key];

            Assert.That(value, Is.Not.Null);
            Assert.That(value.IsEarned, Is.True);
            Assert.That(value.PercentComplete, Is.EqualTo(100f));
            Assert.That(value.EarnedDate, Is.Not.Null);
        }

        [Test]
        public async Task AchievementRouting_WhenSignedOut_UsesLocalProvider()
        {
            var gamer = SignedInGamer.Current;
            gamer.SetSignedInToLive(false);

            var local = new RecordingAchievementProvider();
            var live = new RecordingAchievementProvider();
            AchievementService.LocalProvider = local;
            AchievementService.LiveProvider = live;

            var key = $"achievement.route.local.{Guid.NewGuid():N}";

            await gamer.SetAchievementProgressAsync(key, 10f);
            await gamer.UnlockAchievementAsync(key);
            var _ = await gamer.GetAchievementsAsync();

            Assert.That(local.SetProgressCallCount, Is.EqualTo(1));
            Assert.That(local.UnlockCallCount, Is.EqualTo(1));
            Assert.That(local.GetCallCount, Is.EqualTo(1));
            Assert.That(live.SetProgressCallCount, Is.EqualTo(0));
            Assert.That(live.UnlockCallCount, Is.EqualTo(0));
            Assert.That(live.GetCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task AchievementRouting_WhenSignedIn_UsesLiveProvider()
        {
            var gamer = SignedInGamer.Current;
            gamer.SetSignedInToLive(true);

            var local = new RecordingAchievementProvider();
            var live = new RecordingAchievementProvider();
            AchievementService.LocalProvider = local;
            AchievementService.LiveProvider = live;

            var key = $"achievement.route.live.{Guid.NewGuid():N}";

            await gamer.SetAchievementProgressAsync(key, 5f);
            await gamer.UnlockAchievementAsync(key);
            var _ = await gamer.GetAchievementsAsync();

            Assert.That(local.SetProgressCallCount, Is.EqualTo(0));
            Assert.That(local.UnlockCallCount, Is.EqualTo(0));
            Assert.That(local.GetCallCount, Is.EqualTo(0));
            Assert.That(live.SetProgressCallCount, Is.EqualTo(1));
            Assert.That(live.UnlockCallCount, Is.EqualTo(1));
            Assert.That(live.GetCallCount, Is.EqualTo(1));
        }

        [Test]
        public void AchievementAsyncApis_RespectCancellation()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.cancel.{Guid.NewGuid():N}";

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(() => gamer.SetAchievementProgressAsync(key, 1f, cts.Token));
            Assert.ThrowsAsync<OperationCanceledException>(() => gamer.UnlockAchievementAsync(key, cts.Token));
            Assert.ThrowsAsync<OperationCanceledException>(() => gamer.GetAchievementsAsync(cts.Token));
        }

        [Test]
        public void AchievementSyncWrappers_Work()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.sync.{Guid.NewGuid():N}";

            gamer.SetAchievementProgress(key, 50f);
            gamer.UnlockAchievement(key);
            var achievements = gamer.GetAchievements();
            var unlocked = achievements[key];

            Assert.That(unlocked, Is.Not.Null);
            Assert.That(unlocked.IsEarned, Is.True);
            Assert.That(unlocked.PercentComplete, Is.EqualTo(100f));
        }

        [Test]
        public async Task CatalogMetadata_IsProjectedIntoAchievements()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.catalog.{Guid.NewGuid():N}";

            AchievementCatalog.Register(new AchievementDefinition(
                key: key,
                displayName: "Catalog Display",
                description: "Catalog Description",
                howToEarn: "Do the thing",
                gamerScore: 25,
                isHidden: true,
                iconKey: "catalog_icon",
                iconUri: "https://example/icon.png"));

            await gamer.SetAchievementProgressAsync(key, 25f);

            var achievements = await gamer.GetAchievementsAsync();
            var value = achievements[key];

            Assert.That(value, Is.Not.Null);
            Assert.That(value.DisplayName, Is.EqualTo("Catalog Display"));
            Assert.That(value.Description, Is.EqualTo("Catalog Description"));
            Assert.That(value.HowToEarn, Is.EqualTo("Do the thing"));
            Assert.That(value.GamerScore, Is.EqualTo(25));
            Assert.That(value.IsHidden, Is.True);
            Assert.That(value.IconKey, Is.EqualTo("catalog_icon"));
            Assert.That(value.IconUri, Is.EqualTo("https://example/icon.png"));
            Assert.That(value.PercentComplete, Is.EqualTo(25f));
        }

        [Test]
        public async Task PersistentLocalProvider_PersistsAcrossProviderInstances()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"mgnet.ach.{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);
            var storagePath = Path.Combine(tempRoot, "achievements.json");

            try
            {
                var gamer = SignedInGamer.Current;
                var key = $"achievement.persist.{Guid.NewGuid():N}";

                var providerA = new PersistentLocalAchievementProvider(storagePath);
                await providerA.SetProgressAsync(gamer, key, 45f);

                var providerB = new PersistentLocalAchievementProvider(storagePath);
                var achievements = await providerB.GetAchievementsAsync(gamer);
                var value = achievements[key];

                Assert.That(value, Is.Not.Null);
                Assert.That(value.IsEarned, Is.False);
                Assert.That(value.PercentComplete, Is.EqualTo(45f));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void UsePersistentLocalStorage_UsesGameFolderName()
        {
            var gameName = $"RoyalFlushRush.Achievements.{Guid.NewGuid():N}";

            AchievementService.UsePersistentLocalStorage(gameName);

            var provider = AchievementService.LocalProvider as PersistentLocalAchievementProvider;
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.StoragePath, Does.Contain(gameName));
        }

        [Test]
        public async Task GetAchievementIconAsync_UsesLocalMediaProviderAndIconKeyMapping()
        {
            var gamer = SignedInGamer.Current;
            var key = $"achievement.icon.{Guid.NewGuid():N}";

            AchievementCatalog.Register(new AchievementDefinition(
                key: key,
                displayName: "Icon Achievement",
                iconKey: "icon_key_local"));

            var media = new InMemoryAchievementMediaProvider();
            var iconData = new byte[] { 1, 2, 3, 4 };
            media.RegisterByIconKey("icon_key_local", new AchievementIcon(iconData, "image/png", "cache-icon"));
            AchievementMediaService.LocalProvider = media;

            var icon = await gamer.GetAchievementIconAsync(key);

            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.ContentType, Is.EqualTo("image/png"));
            Assert.That(icon.CacheKey, Is.EqualTo("cache-icon"));
            Assert.That(icon.Data, Is.EqualTo(iconData));
        }

        [Test]
        public async Task GetAchievementIconAsync_RoutesToLiveMediaProviderWhenSignedIn()
        {
            var gamer = SignedInGamer.Current;
            gamer.SetSignedInToLive(true);

            var local = new RecordingAchievementMediaProvider();
            var live = new RecordingAchievementMediaProvider();
            AchievementMediaService.LocalProvider = local;
            AchievementMediaService.LiveProvider = live;

            var _ = await gamer.GetAchievementIconAsync("achievement.any");

            Assert.That(local.GetCallCount, Is.EqualTo(0));
            Assert.That(live.GetCallCount, Is.EqualTo(1));
        }

        private sealed class RecordingAchievementProvider : IAchievementProvider
        {
            public int GetCallCount { get; private set; }
            public int SetProgressCallCount { get; private set; }
            public int UnlockCallCount { get; private set; }

            public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GetCallCount++;
                return Task.FromResult(new AchievementCollection(Array.Empty<Achievement>()));
            }

            public Task SetProgressAsync(SignedInGamer gamer, string achievementKey, float percentComplete, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetProgressCallCount++;
                return Task.CompletedTask;
            }

            public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UnlockCallCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingAchievementMediaProvider : IAchievementMediaProvider
        {
            public int GetCallCount { get; private set; }

            public Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GetCallCount++;
                return Task.FromResult<AchievementIcon>(null);
            }
        }
    }
}
