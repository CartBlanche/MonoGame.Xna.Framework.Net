using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSAchievementMediaProviderTests
    {
        [TearDown]
        public void TearDown()
        {
            EOSRuntime.Shutdown();
        }

        [Test]
        public async Task EOSAchievementMediaProvider_WhenRuntimeUnavailable_ReturnsNull()
        {
            var provider = new EOSAchievementMediaProvider();
            var icon = await provider.GetIconAsync(SignedInGamer.Current, "achievement.any");

            Assert.That(icon, Is.Null);
        }
    }
}
