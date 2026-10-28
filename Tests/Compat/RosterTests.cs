using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>A host and two clients: everyone knows everyone and can send to anyone.</summary>
    [TestFixture]
    public class RosterTests
    {
        SessionHarness lan;
        NetworkSession host;
        NetworkSession clientA;
        NetworkSession clientB;

        [SetUp]
        public async Task SetUp()
        {
            lan = new SessionHarness();
            host = await lan.StartHostAsync(maxGamers: 4);
            clientA = await lan.JoinClientAsync();
            clientB = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => lan.All.All(s => s.AllGamers.Count == 3)),
                "not every machine sees all three gamers");
        }

        [TearDown]
        public void TearDown() => lan.Dispose();

        static NetworkGamer Find(NetworkSession session, string gamerId) =>
            session.AllGamers.Single(g => g.GamerId == gamerId);

        [Test]
        public void EveryMachine_AgreesOnEachGamersId()
        {
            foreach (NetworkGamer onHost in host.AllGamers)
                foreach (NetworkSession other in new[] { clientA, clientB })
                    Assert.That(Find(other, onHost.GamerId).Id, Is.EqualTo(onHost.Id),
                        $"{onHost.Gamertag} has a different id on another machine");

            Assert.That(host.AllGamers.Select(g => g.Id).Distinct().Count(), Is.EqualTo(3), "ids must be unique");
        }

        [Test]
        public void TheHostIsTheSameGamerEverywhere()
        {
            string hostId = SessionHarness.LocalOf(host).GamerId;
            Assert.That(clientA.AllGamers.Host.GamerId, Is.EqualTo(hostId));
            Assert.That(clientB.AllGamers.Host.GamerId, Is.EqualTo(hostId));
        }

        [Test]
        public async Task OneClient_CanSendDataToAnother_ThroughTheHost()
        {
            LocalNetworkGamer a = SessionHarness.LocalOf(clientA);
            LocalNetworkGamer b = SessionHarness.LocalOf(clientB);
            NetworkGamer bSeenByA = Find(clientA, b.GamerId);

            a.SendData(new byte[] { 7, 8, 9 }, SendDataOptions.ReliableInOrder, bSeenByA);

            var buffer = new byte[16];
            NetworkGamer sender = null;
            int length = 0;
            Assert.That(await lan.WaitUntilAsync(() =>
            {
                if (!b.IsDataAvailable) return false;
                length = b.ReceiveData(buffer, out sender);
                return true;
            }), "the other client never received it");

            Assert.That(buffer.Take(length), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(sender.GamerId, Is.EqualTo(a.GamerId), "the packet must say who really sent it");
            Assert.That(sender, Is.SameAs(Find(clientB, a.GamerId)));
        }

        [Test]
        public async Task DataSentToEveryone_ReachesEveryoneElse_AndNotTheSender()
        {
            LocalNetworkGamer a = SessionHarness.LocalOf(clientA);
            LocalNetworkGamer b = SessionHarness.LocalOf(clientB);
            LocalNetworkGamer h = SessionHarness.LocalOf(host);

            a.SendData(new byte[] { 1 }, SendDataOptions.ReliableInOrder);

            Assert.That(await lan.WaitUntilAsync(() => b.IsDataAvailable && h.IsDataAvailable), "someone missed it");
            await lan.WaitUntilAsync(() => false, 500);
            Assert.That(a.IsDataAvailable, Is.False, "the sender received its own packet");
        }

        [Test]
        public async Task WhenAClientLeaves_TheOtherClientIsToldToo()
        {
            string leaverId = SessionHarness.LocalOf(clientA).GamerId;
            var left = new List<string>();
            clientB.GamerLeft += (s, e) => { lock (left) left.Add(((NetworkGamer)e.Gamer).GamerId); };

            clientA.Leave();

            Assert.That(await lan.WaitUntilAsync(() => { lock (left) return left.Contains(leaverId); }),
                "the other client never heard the gamer had left");
            Assert.That(clientB.AllGamers.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task TheHost_CanRemoveAMachine_AndItsSessionEndsAsRemovedByHost()
        {
            NetworkGamer targetOnHost = host.RemoteGamers.First();
            NetworkSession removed = targetOnHost.GamerId == SessionHarness.LocalOf(clientA).GamerId ? clientA : clientB;
            NetworkSession other = removed == clientA ? clientB : clientA;
            NetworkSessionEndReason? reason = null;
            removed.SessionEnded += (s, e) => reason = e.EndReason;

            targetOnHost.Machine.RemoveFromSession();

            Assert.That(await lan.WaitUntilAsync(() => reason != null), "the removed machine was never told");
            Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.RemovedByHost));
            Assert.That(host.AllGamers.Count, Is.EqualTo(2));
            Assert.That(await lan.WaitUntilAsync(() => other.AllGamers.Count == 2), "the other client was not told");
        }

        [Test]
        public void OnlyTheHost_CanRemoveAMachine_AndNotItself()
        {
            Assert.Throws<System.InvalidOperationException>(() => SessionHarness.LocalOf(host).Machine.RemoveFromSession());
            Assert.Throws<System.InvalidOperationException>(() => clientA.RemoteGamers.First().Machine.RemoveFromSession());
        }
    }
}
