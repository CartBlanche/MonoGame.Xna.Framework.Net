using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class SteamCompositionRootTests
    {
        private const byte TestMessageType = 241;

        private IGuideSignInProvider originalSignInProvider;
        private ILeaderboardProvider originalLiveProvider;
        private ILeaderboardProvider originalLocalProvider;
        private IAchievementProvider originalAchievementLiveProvider;
        private IAchievementProvider originalAchievementLocalProvider;
        private IAchievementMediaProvider originalAchievementMediaLiveProvider;
        private IAchievementMediaProvider originalAchievementMediaLocalProvider;
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

        private sealed class RecordingLeaderboardProvider : ILeaderboardProvider
        {
            public bool SubmitCalled { get; private set; }

            public Task SubmitAsync(LeaderboardWriter writer, CancellationToken cancellationToken = default)
            {
                SubmitCalled = true;
                return Task.CompletedTask;
            }

            public Task<LeaderboardReader> ReadAsync(LeaderboardIdentity identity, int pageStart, int pageSize, SignedInGamer pivotGamer = null, CancellationToken cancellationToken = default)
            {
                var reader = new LeaderboardReader(identity.Key, pageStart, 0, new System.Collections.Generic.List<LeaderboardEntry>());
                return Task.FromResult(reader);
            }
        }

        private sealed class RecordingAchievementProvider : IAchievementProvider
        {
            public bool UnlockCalled { get; private set; }

            public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new AchievementCollection(Array.Empty<Achievement>()));
            }

            public Task SetProgressAsync(SignedInGamer gamer, string achievementKey, float percentComplete, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                UnlockCalled = true;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingAchievementMediaProvider : IAchievementMediaProvider
        {
            public bool GetIconCalled { get; private set; }

            public Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                GetIconCalled = true;
                return Task.FromResult<AchievementIcon>(null);
            }
        }

        [SetUp]
        public void SetUp()
        {
            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);

            originalSignInProvider = Guide.SignInProvider;
            originalLiveProvider = LeaderboardService.LiveProvider;
            originalLocalProvider = LeaderboardService.LocalProvider;
            originalAchievementLiveProvider = AchievementService.LiveProvider;
            originalAchievementLocalProvider = AchievementService.LocalProvider;
            originalAchievementMediaLiveProvider = AchievementMediaService.LiveProvider;
            originalAchievementMediaLocalProvider = AchievementMediaService.LocalProvider;
            originalBackend = NetworkServiceProvider.Backend;

            SignedInGamer.Current.SetSignedInToLive(false);
            LeaderboardService.LiveProvider = null;
            AchievementService.LiveProvider = null;
            AchievementMediaService.LiveProvider = null;
            AchievementCatalog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Guide.SignInProvider = originalSignInProvider;
            LeaderboardService.LiveProvider = originalLiveProvider;
            LeaderboardService.LocalProvider = originalLocalProvider;
            AchievementService.LiveProvider = originalAchievementLiveProvider;
            AchievementService.LocalProvider = originalAchievementLocalProvider;
            AchievementMediaService.LiveProvider = originalAchievementMediaLiveProvider;
            AchievementMediaService.LocalProvider = originalAchievementMediaLocalProvider;

            NetworkServiceProvider.SetBackend(originalBackend);

            SignedInGamer.Current.SetSignedInToLive(false);
            AchievementCatalog.Clear();
        }

        [Test]
        public async Task Configure_ThenSignIn_ThenSessionFlow_WiresEndToEnd()
        {
            var stubSignIn = new StubSignInProvider(result: true);
            var recordingLive = new RecordingLeaderboardProvider();
            var recordingAchievementLive = new RecordingAchievementProvider();
            var recordingAchievementMediaLive = new RecordingAchievementMediaProvider();
            var definitions = new[]
            {
                new AchievementDefinition("composition-e2e-achievement", "Composition Achievement")
            };

            SteamPlatformBootstrap.Configure(
                gameName: "CompositionRootTests",
                signInProvider: stubSignIn,
                backend: new SteamBackend(),
                liveProviderFactoryOverride: () => recordingLive,
                achievementLiveProviderFactoryOverride: () => recordingAchievementLive,
                achievementMediaLiveProviderFactoryOverride: () => recordingAchievementMediaLive,
                achievementDefinitions: definitions);

            var signedIn = await SteamPlatformBootstrap.TrySignInAndEnableLiveAsync();

            Assert.That(signedIn, Is.True);
            Assert.That(SignedInGamer.Current.IsSignedInToLive, Is.True);
            Assert.That(Guide.SignInProvider, Is.SameAs(stubSignIn));
            Assert.That(LeaderboardService.LiveProvider, Is.SameAs(recordingLive));
            Assert.That(AchievementService.LiveProvider, Is.SameAs(recordingAchievementLive));
            Assert.That(AchievementMediaService.LiveProvider, Is.SameAs(recordingAchievementMediaLive));
            Assert.That(NetworkServiceProvider.Backend, Is.TypeOf<SteamBackend>());
            Assert.That(AchievementCatalog.Get("composition-e2e-achievement"), Is.Not.Null);

            var leaderboard = new LeaderboardIdentity("composition-e2e");
            var writer = new LeaderboardWriter(leaderboard, SignedInGamer.Current)
            {
                Score = 42
            };
            await SignedInGamer.Current.WriteLeaderboardAsync(writer);
            Assert.That(recordingLive.SubmitCalled, Is.True);

            await SignedInGamer.Current.UnlockAchievementAsync("composition-e2e-achievement");
            Assert.That(recordingAchievementLive.UnlockCalled, Is.True);

            await SignedInGamer.Current.GetAchievementIconAsync("composition-e2e-achievement");
            Assert.That(recordingAchievementMediaLive.GetIconCalled, Is.True);

            // with Steam not running the back-end falls back to the LAN, so this is a real session over loopback
            var host = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 2, 0, null);
            NetworkSession client = null;
            try
            {
                AvailableNetworkSession target = null;
                for (int attempt = 0; attempt < 10 && target == null; attempt++)
                {
                    using var found = await NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, null);
                    target = found.FirstOrDefault(s => s.HostGamertag == host.LocalGamers[0].Gamertag);
                }
                Assert.That(target, Is.Not.Null, "the host was not found");
                client = await NetworkSession.JoinAsync(target);

                for (int i = 0; i < 200 && host.AllGamers.Count < 2; i++)
                {
                    host.Update();
                    await Task.Delay(10);
                }

                host.LocalGamers[0].SendData(new byte[] { 1, 2, 3 }, SendDataOptions.ReliableInOrder);

                var buffer = new byte[16];
                int length = 0;
                for (int i = 0; i < 300 && length == 0; i++)
                {
                    client.Update();
                    if (client.LocalGamers[0].IsDataAvailable)
                        length = client.LocalGamers[0].ReceiveData(buffer, out _);
                    else
                        await Task.Delay(10);
                }

                Assert.That(buffer.Take(length), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally
            {
                client?.Dispose();
                host.Dispose();
            }
        }
    }
}
