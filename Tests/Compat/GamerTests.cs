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
        public async Task RoundtripTime_IsMeasuredOnceDataIsFlowing()
        {
            NetworkGamer remote = host.RemoteGamers[0];
            LocalNetworkGamer local = SessionHarness.LocalOf(host);

            // reliable traffic is acknowledged, and the acknowledgements time the link
            Assert.That(await lan.WaitUntilAsync(() =>
            {
                local.SendData(new byte[] { 1 }, SendDataOptions.ReliableInOrder, remote);
                return remote.RoundtripTime > System.TimeSpan.Zero;
            }), "no roundtrip time was ever measured");
            Assert.That(remote.RoundtripTime, Is.LessThan(System.TimeSpan.FromSeconds(1)));
        }

        [Test]
        public async Task GamerJoined_IsNotReplayedToALateSubscriber()
        {
            await lan.WaitUntilAsync(() => false, 500);     // let everything already queued be raised
            int raised = 0;

            host.GamerJoined += (s, e) => raised++;
            await lan.WaitUntilAsync(() => false, 500);

            Assert.That(raised, Is.Zero, "gamers already in the session must not be raised again");
        }

        [Test]
        public async Task DataSentToEveryone_AlsoReachesTheSender_AsTheSender()
        {
            // XNA: "Sends a network packet to all gamers in a session. This includes the gamer sending the packet."
            // NetRumble's host only sets up its world from the packet it sends to everyone.
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            hostGamer.SendData(new byte[] { 0xAB }, SendDataOptions.ReliableInOrder);

            Assert.That(await lan.WaitUntilAsync(() => clientGamer.IsDataAvailable), "the client never got it");
            Assert.That(hostGamer.IsDataAvailable, Is.True, "the sender did not receive its own packet");

            var buffer = new byte[16];
            int length = hostGamer.ReceiveData(buffer, out NetworkGamer sender);
            Assert.That(length, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(0xAB));
            Assert.That(sender, Is.SameAs(hostGamer));
            Assert.That(sender.IsLocal, Is.True);
            Assert.That(hostGamer.IsDataAvailable, Is.False, "it arrived twice");
        }

        [Test]
        public async Task DataSentToOneGamer_DoesNotComeBackToTheSender()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            hostGamer.SendData(new byte[] { 0xCD }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);

            Assert.That(await lan.WaitUntilAsync(() => clientGamer.IsDataAvailable), "the client never got it");
            await lan.WaitUntilAsync(() => false, 500);
            Assert.That(hostGamer.IsDataAvailable, Is.False, "a packet for one gamer was also given to the sender");
        }

        [Test]
        public async Task AClientsDataForEveryone_ReachesTheClientItselfToo()
        {
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            clientGamer.SendData(new byte[] { 0xEF }, SendDataOptions.ReliableInOrder);

            Assert.That(await lan.WaitUntilAsync(() => hostGamer.IsDataAvailable), "the host never got it");
            Assert.That(clientGamer.IsDataAvailable, Is.True, "the client did not receive its own packet");
        }

        [Test]
        public void SignedInGamer_IsOnlyAvailableForLocalGamers()
        {
            Assert.That(SessionHarness.LocalOf(host).SignedInGamer, Is.Not.Null);
            Assert.That(host.RemoteGamers[0].SignedInGamer, Is.Null);
        }
    }
}
