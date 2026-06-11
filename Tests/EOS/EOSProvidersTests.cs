using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSProvidersTests
    {
        [SetUp]
        public void SetUp()
        {
            EOSRuntime.Shutdown();
            AchievementCatalog.Clear();
            SignedInGamer.Current.SetSignedInToLive(false);
            SignedInGamer.Current.SetGamertag("EOSProviderTester");
        }

        [TearDown]
        public void TearDown()
        {
            EOSRuntime.Shutdown();
            AchievementCatalog.Clear();
            SignedInGamer.Current.SetSignedInToLive(false);
        }

        [Test]
        public async Task EOSLeaderboardProvider_WhenRuntimeUnavailable_UsesLocalProjection()
        {
            var gamer = SignedInGamer.Current;
            var identity = new LeaderboardIdentity($"eos.lb.local.{Guid.NewGuid():N}");
            var provider = new EOSLeaderboardProvider();

            await provider.SubmitAsync(new LeaderboardWriter(identity, gamer) { Score = 4321 });
            using var reader = await provider.ReadAsync(identity, pageStart: 0, pageSize: 10, pivotGamer: gamer);

            Assert.That(reader.Count, Is.EqualTo(1));
            Assert.That(reader[0].Score, Is.EqualTo(4321));
            Assert.That(reader[0].Gamertag, Is.EqualTo(gamer.Gamertag));
        }

        [Test]
        public async Task EOSAchievementProvider_WhenRuntimeUnavailable_TracksLocalProgress()
        {
            var key = $"eos.achievement.local.{Guid.NewGuid():N}";
            AchievementCatalog.Register(new AchievementDefinition(
                key: key,
                displayName: "Local Achievement",
                description: "Local description",
                howToEarn: "Do local thing",
                gamerScore: 5,
                isHidden: false,
                iconKey: null,
                iconUri: null));

            var provider = new EOSAchievementProvider();
            await provider.SetProgressAsync(SignedInGamer.Current, key, 100f);

            var achievements = await provider.GetAchievementsAsync(SignedInGamer.Current);
            var achievement = achievements.Single(a => a.Key == key);

            Assert.That(achievement.IsEarned, Is.True);
            Assert.That(achievement.PercentComplete, Is.EqualTo(100f));
        }
    }
}
