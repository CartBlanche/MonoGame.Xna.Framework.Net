using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The public API of the EOS package is compared with the baseline in PublicApiBaselines/ (see the same test in
    /// Tests/Core). Only the types in Microsoft.Xna.Framework are listed, not any vendored SDK code.
    /// </summary>
    [TestFixture]
    public class EOSPublicApiTests
    {
        [Test]
        public void TheEOSPublicApi_IsWhatTheBaselineSays()
        {
            string? diff = PublicApi.Compare(typeof(Microsoft.Xna.Framework.Net.EOS.EOSBackend).Assembly, "Microsoft.Xna.Framework", "MonoGame.Xna.Framework.Net.EOS.txt");
            Assert.That(diff, Is.Null, "The public API of MonoGame.Xna.Framework.Net.EOS changed:\n" + diff);
        }
    }
}
