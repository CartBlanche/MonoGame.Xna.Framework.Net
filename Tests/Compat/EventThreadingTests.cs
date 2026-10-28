using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// XNA raised session events, and made received data available, only inside NetworkSession.Update(),
    /// on the game's thread. Games rely on it to touch their own state without locks.
    /// </summary>
    [TestFixture]
    public class EventThreadingTests
    {
        SessionHarness lan;

        [SetUp]
        public void SetUp() => lan = new SessionHarness();

        [TearDown]
        public void TearDown() => lan.Dispose();

        // Waits in real time without calling Update(), so anything that arrives has to be held back
        static Task Idle(int ms) => Task.Delay(ms);

        [Test]
        public async Task GamerJoined_IsRaisedOnlyFromUpdate_OnTheCallersThread()
        {
            NetworkSession host = await lan.StartHostAsync();
            var joined = new List<int>();
            host.GamerJoined += (s, e) =>
            {
                if (!e.Gamer.IsLocal)
                    lock (joined) joined.Add(Environment.CurrentManagedThreadId);
            };

            await lan.JoinClientAsync();
            await Idle(1500);
            lock (joined)
                Assert.That(joined, Is.Empty, "GamerJoined was raised before Update() was called");

            // Update() and the condition run back to back on one thread within a pass, so the thread
            // seen by the condition that succeeds is the one that called Update()
            int updateThread = -1;
            Assert.That(await lan.WaitUntilAsync(() =>
            {
                updateThread = Environment.CurrentManagedThreadId;
                lock (joined) return joined.Count == 1;
            }));
            lock (joined)
                Assert.That(joined[0], Is.EqualTo(updateThread), "GamerJoined was not raised on the thread calling Update()");
        }

        [Test]
        public async Task ReceivedData_BecomesAvailableOnlyAfterUpdate()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
            LocalNetworkGamer hostGamer = SessionHarness.LocalOf(host);
            LocalNetworkGamer clientGamer = SessionHarness.LocalOf(client);

            hostGamer.SendData(new byte[] { 0xAA, 1, 2, 3 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
            await Idle(1500);
            Assert.That(clientGamer.IsDataAvailable, Is.False, "data was available before Update()");

            client.Update();
            Assert.That(clientGamer.IsDataAvailable, Is.True, "data was not available after Update()");
        }

        [Test]
        public async Task TheHostLeaving_RaisesSessionEndedFromUpdateOnly_AndOnce()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
            var reasons = new List<NetworkSessionEndReason>();
            client.SessionEnded += (s, e) => { lock (reasons) reasons.Add(e.EndReason); };

            host.Leave();
            await Idle(1500);
            lock (reasons)
                Assert.That(reasons, Is.Empty, "SessionEnded was raised before Update()");

            Assert.That(await lan.WaitUntilAsync(() => { lock (reasons) return reasons.Count > 0; }));
            client.Dispose(); // a game normally disposes the session from this handler
            client.Update();
            lock (reasons)
                Assert.That(reasons, Is.EqualTo(new[] { NetworkSessionEndReason.HostEndedSession }));
        }

        [Test]
        public async Task ImmediateMode_RaisesEventsWithoutUpdate()
        {
            NetworkSession host = await lan.StartHostAsync();
            host.EventDispatchMode = EventDispatchMode.Immediate;
            int joined = 0;
            host.GamerJoined += (s, e) => { if (!e.Gamer.IsLocal) Interlocked.Increment(ref joined); };

            await lan.JoinClientAsync();

            // no Update() calls: only polling the counter
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref joined) == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.That(joined, Is.EqualTo(1));
        }

        [Test]
        public async Task AHandlerThatThrows_DoesNotStopTheRestOfTheQueue()
        {
            NetworkSession host = await lan.StartHostAsync();
            NetworkSession client = await lan.JoinClientAsync();
            Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2));
            int ended = 0;
            client.GamerLeft += (s, e) => throw new InvalidOperationException("handler bug");
            client.SessionEnded += (s, e) => ended++;

            host.Leave();

            Assert.That(await lan.WaitUntilAsync(() => ended == 1), "SessionEnded was lost behind a throwing GamerLeft handler");
        }
    }
}
