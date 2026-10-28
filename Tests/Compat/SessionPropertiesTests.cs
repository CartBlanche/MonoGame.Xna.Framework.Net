using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>XNA's NetworkSessionProperties, and searches that use them.</summary>
    [TestFixture]
    public class SessionPropertiesTests
    {
        NetworkSession host;

        [TearDown]
        public void TearDown()
        {
            try { host?.Dispose(); } catch { }
        }

        [Test]
        public void ThereAreEightSlots_AllNullToStart_AndTheSizeIsFixed()
        {
            var properties = new NetworkSessionProperties();

            Assert.That(properties.Count, Is.EqualTo(8));
            Assert.That(properties.All(p => p == null));
            properties[3] = 42;
            Assert.That(properties[3], Is.EqualTo(42));
            Assert.Throws<ArgumentOutOfRangeException>(() => { var x = properties[8]; });
            Assert.Throws<NotSupportedException>(() => properties.Add(1));
            properties.Clear();
            Assert.That(properties[3], Is.Null);
        }

        [Test]
        public async Task ASearch_OnlyFindsSessionsWhoseProperties_MatchItsOwn()
        {
            var made = new NetworkSessionProperties { [0] = 7, [1] = 2 };
            host = NetworkSession.Create(NetworkSessionType.SystemLink, 1, 4, 0, made);
            string tag = host.LocalGamers[0].Gamertag;

            async Task<bool> FoundWith(NetworkSessionProperties search)
            {
                using (var found = await Task.Run(() => NetworkSession.Find(NetworkSessionType.SystemLink, 1, search)))
                    return found.Any(s => s.HostGamertag == tag);
            }

            // discovery is by broadcast: wait for the first advertisement before trusting a "not found"
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (!await FoundWith(null) && DateTime.UtcNow < deadline) { }

            Assert.That(await FoundWith(null), "no search properties should match everything");
            Assert.That(await FoundWith(new NetworkSessionProperties { [0] = 7 }), "one matching slot");
            Assert.That(await FoundWith(new NetworkSessionProperties { [0] = 7, [1] = 2 }), "every slot matching");
            Assert.That(await FoundWith(new NetworkSessionProperties { [0] = 8 }), Is.False, "a different value");
            Assert.That(await FoundWith(new NetworkSessionProperties { [5] = 1 }), Is.False, "a slot the session never set");
        }
    }
}
