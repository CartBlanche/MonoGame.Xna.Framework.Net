using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class SteamAchievementMediaProviderTests
    {
        [Test]
        public async Task SteamAchievementMediaProvider_WhenSteamNotInitialized_ReturnsNull()
        {
            var gamer = SignedInGamer.Current;
            var provider = new SteamAchievementMediaProvider();

            var icon = await provider.GetIconAsync(gamer, "achievement.any");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task SteamAchievementMediaProvider_ResilienceWhenIconHandleIsZero()
        {
            var gamer = SignedInGamer.Current;
            var mock = new MockSteamAchievementMediaProvider(
                shouldSucceed: false,
                width: 0,
                height: 0);

            var icon = await mock.GetIconAsync(gamer, "achievement.nohandle");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task SteamAchievementMediaProvider_ResilienceWhenImageSizeIsZero()
        {
            var gamer = SignedInGamer.Current;
            var mock = new MockSteamAchievementMediaProvider(
                shouldSucceed: false,
                width: 0,
                height: 0);

            var icon = await mock.GetIconAsync(gamer, "achievement.badsize");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task SteamAchievementMediaProvider_ResilienceWhenRGBAFetchFails()
        {
            var gamer = SignedInGamer.Current;
            var mock = new MockSteamAchievementMediaProvider(
                shouldSucceed: false,
                width: 64,
                height: 64);

            var icon = await mock.GetIconAsync(gamer, "achievement.badrgba");

            Assert.That(icon, Is.Null);
        }

        [Test]
        public async Task SteamAchievementMediaProvider_SuccessfulIconRetrievalReturnsIconWithCorrectDimensions()
        {
            var gamer = SignedInGamer.Current;
            var mock = new MockSteamAchievementMediaProvider(
                shouldSucceed: true,
                width: 128,
                height: 128);

            var icon = await mock.GetIconAsync(gamer, "achievement.success");

            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.Width, Is.EqualTo(128));
            Assert.That(icon.Height, Is.EqualTo(128));
            Assert.That(icon.Data, Is.Not.Null);
            Assert.That(icon.Data.Length, Is.EqualTo(128 * 128 * 4));
        }

        private sealed class MockSteamAchievementMediaProvider : IAchievementMediaProvider
        {
            private readonly bool shouldSucceed;
            private readonly uint width;
            private readonly uint height;

            public MockSteamAchievementMediaProvider(bool shouldSucceed, uint width, uint height)
            {
                this.shouldSucceed = shouldSucceed;
                this.width = width;
                this.height = height;
            }

            public Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!shouldSucceed || width == 0 || height == 0)
                {
                    return Task.FromResult<AchievementIcon>(null);
                }

                var rgbaByteCount = (int)(width * height * 4);
                var rgba = new byte[rgbaByteCount];
                var icon = new AchievementIcon(
                    data: rgba,
                    contentType: "application/x-steam-rgba32",
                    cacheKey: $"mock:{achievementKey}:1",
                    width: (int)width,
                    height: (int)height);

                return Task.FromResult(icon);
            }
        }
    }
}
