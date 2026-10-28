using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>NetworkGamer / LocalNetworkGamer members, over a real host and client.</summary>
    [TestFixture]
    public class GamerTests
    {
        SessionHarness lan;
        NetworkSession host;
        NetworkSession client;

        [SetUp]
        public async Task SetUp()
        {
            lan = new SessionHarness();
            host = await lan.StartHostAsync();
            client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
        }

        [TearDown]
        public void TearDown() => lan.Dispose();

        [Test]
        public void EveryGamer_HasAMachine_ThatListsIt()
        {
            foreach (NetworkGamer gamer in host.AllGamers)
            {
                Assert.That(gamer.Machine, Is.Not.Null);
                Assert.That(gamer.Machine.Gamers, Does.Contain(gamer));
            }
        }

        [Test]
        public void LocalGamers_ShareTheLocalMachine_AndRemoteGamersHaveTheirOwn()
        {
            NetworkGamer local = SessionHarness.LocalOf(host);
            NetworkGamer remote = host.RemoteGamers[0];

            Assert.That(local.Machine, Is.Not.SameAs(remote.Machine));
            Assert.That(remote.Machine, Is.SameAs(remote.Machine), "the machine must be stable");
            Assert.That(remote.Machine.Gamers.Count, Is.EqualTo(1));
            Assert.That(local.Machine.Gamers.Single(), Is.SameAs(local));
        }

        [Test]
        public void SignedInGamer_IsOnlyAvailableForLocalGamers()
        {
            Assert.That(SessionHarness.LocalOf(host).SignedInGamer, Is.Not.Null);
            Assert.That(host.RemoteGamers[0].SignedInGamer, Is.Null);
        }
    }
}
