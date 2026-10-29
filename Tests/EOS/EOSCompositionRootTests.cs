using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSCompositionRootTests
    {
        private IGuideSignInProvider originalSignInProvider;
        private ILeaderboardProvider originalLiveProvider;
        private IAchievementProvider originalAchievementLiveProvider;
        private IAchievementMediaProvider originalAchievementMediaLiveProvider;
        private INetworkBackend originalBackend;

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

        [SetUp]
        public void SetUp()
        {
            originalSignInProvider = Guide.SignInProvider;
            originalLiveProvider = LeaderboardService.LiveProvider;
            originalAchievementLiveProvider = AchievementService.LiveProvider;
            originalAchievementMediaLiveProvider = AchievementMediaService.LiveProvider;

            originalBackend = NetworkServiceProvider.Backend;

            SignedInGamer.Current.SetSignedInToLive(false);
            LeaderboardService.LiveProvider = null;
            AchievementService.LiveProvider = null;
            AchievementMediaService.LiveProvider = null;
        }

        [TearDown]
        public void TearDown()
        {
            Guide.SignInProvider = originalSignInProvider;
            LeaderboardService.LiveProvider = originalLiveProvider;
            AchievementService.LiveProvider = originalAchievementLiveProvider;
            AchievementMediaService.LiveProvider = originalAchievementMediaLiveProvider;

            NetworkServiceProvider.SetBackend(originalBackend);

            SignedInGamer.Current.SetSignedInToLive(false);
            EOSRuntime.Shutdown();
        }

        [Test]
        public async Task Configure_ThenSignIn_EnablesLiveProvidersAndBackend()
        {
            EOSRuntime.Initialize();

            EOSPlatformBootstrap.Configure(
                gameName: "EOSTests",
                signInProvider: new StubSignInProvider(result: true));

            EOSRuntime.SetSignedInIdentity("eos-1", "EOSTester");
            var signedIn = await EOSPlatformBootstrap.TrySignInAndEnableLiveAsync().ConfigureAwait(false);

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(LeaderboardService.LiveProvider, Is.TypeOf<EOSLeaderboardProvider>());
            Assert.That(AchievementService.LiveProvider, Is.TypeOf<EOSAchievementProvider>());
            Assert.That(AchievementMediaService.LiveProvider, Is.TypeOf<EOSAchievementMediaProvider>());
            Assert.That(NetworkServiceProvider.Backend, Is.TypeOf<EOSBackend>());
            Assert.That(NetworkServiceProvider.ActiveBackendName, Is.EqualTo("EOS"));
        }
    }
}
