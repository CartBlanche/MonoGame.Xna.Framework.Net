using System.Reflection;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The public API is the contract. This lists it and compares it with the baseline in PublicApiBaselines/, so a
    /// change cannot slip in by accident. If the change is intended, review the listed differences, decide what it means
    /// for the version (see VERSIONING.md), and run the tests with UPDATE_API_BASELINE=1 to accept it.
    /// </summary>
    [TestFixture]
    public class PublicApiTests
    {
        [Test]
        public void TheCorePublicApi_IsWhatTheBaselineSays()
        {
            string? diff = PublicApi.Compare(typeof(NetworkSession).Assembly, "Microsoft.Xna.Framework", "MonoGame.Xna.Framework.Net.txt");
            Assert.That(diff, Is.Null, "The public API of MonoGame.Xna.Framework.Net changed:\n" + diff);
        }
    }
}
