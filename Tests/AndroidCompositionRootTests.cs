using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.Android;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class AndroidCompositionRootTests
    {
        private const byte TestMessageType = 242;

        private IGuideSignInProvider originalSignInProvider;
        private ILeaderboardProvider originalLiveProvider;
        private IAchievementProvider originalAchievementLiveProvider;
        private IAchievementMediaProvider originalAchievementMediaLiveProvider;
        private INetworkSessionFactory originalSessionFactory;
        private bool hadSessionFactory;

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

            hadSessionFactory = NetworkServiceProvider.IsConfigured;
            if (hadSessionFactory)
            {
                originalSessionFactory = NetworkServiceProvider.SessionFactory;
            }

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

            if (hadSessionFactory && originalSessionFactory != null)
            {
                NetworkServiceProvider.SetSessionFactory(originalSessionFactory);
            }
            else
            {
                NetworkServiceProvider.ResetToDefault();
            }

            SignedInGamer.Current.SetSignedInToLive(false);
            AndroidRuntime.Shutdown();
        }

        [Test]
        public async Task Configure_ThenSignIn_EnablesLiveProvidersAndBackend()
        {
            AndroidRuntime.Initialize(initialPlayerId: "player-1", initialGamertag: "AndroidTester");

            AndroidPlatformBootstrap.Configure(
                gameName: "AndroidTests",
                signInProvider: new StubSignInProvider(result: true));

            var signedIn = await AndroidPlatformBootstrap.TrySignInAndEnableLiveAsync();

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(LeaderboardService.LiveProvider, Is.TypeOf<AndroidLeaderboardProvider>());
            Assert.That(AchievementService.LiveProvider, Is.TypeOf<AndroidAchievementProvider>());
            Assert.That(AchievementMediaService.LiveProvider, Is.TypeOf<AndroidAchievementMediaProvider>());
            Assert.That(NetworkServiceProvider.SessionFactory, Is.TypeOf<AndroidNetworkSessionFactory>());
            Assert.That(NetworkServiceProvider.ActiveBackendName, Is.EqualTo("Android"));
        }

        [Test]
        public async Task Networking_DefaultFactory_SupportsHostFindAndJoin()
        {
            AndroidRuntime.Initialize(initialPlayerId: "player-2", initialGamertag: "AndroidHost");
            var factory = new AndroidNetworkSessionFactory();
            NetworkServiceProvider.SetSessionFactory(factory);

            var host = factory.CreateSession();
            var client = factory.CreateSession();

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 2, privateGamerSlots: 0);

                SessionInfo sessionInfo = null;
                for (var attempt = 0; attempt < 8 && sessionInfo == null; attempt++)
                {
                    var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink).ConfigureAwait(false)).ToList();
                    sessionInfo = sessions.FirstOrDefault();
                    if (sessionInfo == null)
                    {
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                }

                Assert.That(sessionInfo, Is.Not.Null);

                await client.JoinAsync(sessionInfo.JoinAddress);
                Assert.That(host.State, Is.EqualTo(NetworkSessionState.Lobby));
                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Lobby));
                Assert.That(client.AllGamers.Count, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }
    }
}
