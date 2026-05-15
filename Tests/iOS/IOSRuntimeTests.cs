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
    public class IOSRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            IOSRuntime.Shutdown();
            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [Test]
        public async Task SetAppleGameCenterClient_ThenSignInAsync_UsesInjectedClient()
        {
            IOSRuntime.Initialize();
            IOSRuntime.SetAppleGameCenterClient(new StubAppleGameCenterClient());

            var signedIn = await IOSRuntime.SignInAsync();

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(SignedInGamer.Current.Gamertag, Is.EqualTo("iOSTester"));
        }

        [Test]
        public async Task Configure_ThenSignIn_EnablesLiveProvidersAndBackend()
        {
            IOSRuntime.Initialize();

            IOSPlatformBootstrap.Configure(
                gameName: "IOSTests",
                signInProvider: new StubSignInProvider(result: true));

            var signedIn = await IOSPlatformBootstrap.TrySignInAndEnableLiveAsync();

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(LeaderboardService.LiveProvider, Is.TypeOf<IOSLeaderboardProvider>());
            Assert.That(AchievementService.LiveProvider, Is.TypeOf<IOSAchievementProvider>());
            Assert.That(AchievementMediaService.LiveProvider, Is.TypeOf<IOSAchievementMediaProvider>());
            Assert.That(NetworkServiceProvider.SessionFactory, Is.TypeOf<IOSNetworkSessionFactory>());
            Assert.That(NetworkServiceProvider.ActiveBackendName, Is.EqualTo("iOS"));
        }

        private sealed class StubSignInProvider : IGuideSignInProvider
        {
            private readonly bool result;

            public StubSignInProvider(bool result)
            {
                this.result = result;
            }

            public Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(result);
            }
        }

        private sealed class StubAppleGameCenterClient : IAppleGameCenterClient
        {
            public Task<AppleGameCenterPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new AppleGameCenterPlayer
                {
                    Id = "ios-player-1",
                    DisplayName = "iOSTester"
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
                return Task.FromResult<IReadOnlyDictionary<string, AppleGameCenterAchievementProgress>>(new Dictionary<string, AppleGameCenterAchievementProgress>());
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
