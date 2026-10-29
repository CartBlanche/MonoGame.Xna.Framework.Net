using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.EOS.Tests
{
    /// <summary>What the host republishes to the lobby: its properties and whether it wants a password.</summary>
    [TestFixture]
    public class EOSLobbyStateTests
    {
        private static Dictionary<string, object> Props(params (string, object)[] pairs) =>
            pairs.ToDictionary(p => p.Item1, p => p.Item2);

        [Test]
        public void NothingChanged_MeansNothingToRepublish()
        {
            var published = EOSBackend.DescribeAdvertisedState(false, Props(("mode", "ctf")));
            Assert.That(EOSBackend.AdvertisedStateChanged(false, Props(("mode", "ctf")), published), Is.False);
        }

        [Test]
        public void ASessionThatGainsAPassword_IsRepublished()
        {
            var published = EOSBackend.DescribeAdvertisedState(false, Props());
            Assert.That(EOSBackend.AdvertisedStateChanged(true, Props(), published), Is.True);
        }

        [Test]
        public void ASessionThatLosesItsPassword_IsRepublished()
        {
            var published = EOSBackend.DescribeAdvertisedState(true, Props());
            Assert.That(EOSBackend.AdvertisedStateChanged(false, Props(), published), Is.True);
        }

        [Test]
        public void AChangedOrRemovedProperty_IsStillRepublished()
        {
            var published = EOSBackend.DescribeAdvertisedState(false, Props(("mode", "ctf"), ("skill", 3)));
            Assert.That(EOSBackend.AdvertisedStateChanged(false, Props(("mode", "dm"), ("skill", 3)), published), Is.True);
            Assert.That(EOSBackend.AdvertisedStateChanged(false, Props(("mode", "ctf")), published), Is.True);
        }

        [Test]
        public void TheFlag_IsNeverTreatedAsAProperty()
        {
            // a property of the same name as the flag's key must not be mistaken for it
            var published = EOSBackend.DescribeAdvertisedState(false, Props(("pw", "1")));
            Assert.That(published, Does.Contain("pw=0"));
            Assert.That(published, Does.Contain("p.pw=" + SessionPropertyEncoding.Encode("1")));
        }
    }
}
