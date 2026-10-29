using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.iOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class iOSCompositionRootTests
    {
        private const byte TestMessageType = 242;

        private IGuideSignInProvider originalSignInProvider;
        private ILeaderboardProvider originalLiveProvider;
        private IAchievementProvider originalAchievementLiveProvider;
        private IAchievementMediaProvider originalAchievementMediaLiveProvider;
        private INetworkBackend originalBackend;

        private sealed class TestReliableMessage : INetworkMessage
        {
            public byte MessageType => TestMessageType;

            public string Payload { get; set; }

            public void Serialize(PacketWriter writer)
            {
                writer.Write(Payload ?? string.Empty);
            }

            public void Deserialize(PacketReader reader)
            {
                Payload = reader.ReadString();
            }
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

        [SetUp]
        public void SetUp()
        {
            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);

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
            IOSRuntime.Shutdown();
        }

        [Test]
        public async Task Configure_ThenSignIn_EnablesLiveProvidersAndBackend()
        {
            IOSRuntime.Initialize();

            IOSPlatformBootstrap.Configure(
                gameName: "iOSTests",
                signInProvider: new StubSignInProvider(result: true));

            var signedIn = await IOSPlatformBootstrap.TrySignInAndEnableLiveAsync().ConfigureAwait(false);

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(LeaderboardService.LiveProvider, Is.TypeOf<IOSLeaderboardProvider>());
            Assert.That(AchievementService.LiveProvider, Is.TypeOf<IOSAchievementProvider>());
            Assert.That(AchievementMediaService.LiveProvider, Is.TypeOf<IOSAchievementMediaProvider>());
            Assert.That(NetworkServiceProvider.Backend, Is.TypeOf<IOSBackend>());
            Assert.That(NetworkServiceProvider.ActiveBackendName, Is.EqualTo("iOS"));
        }

    }
}
