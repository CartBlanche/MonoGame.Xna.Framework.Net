using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>XNA's NetworkSessionProperties, searches that use them, and the Begin/End methods.</summary>
    [TestFixture]
    public class SessionPropertiesTests
    {
        NetworkSession host;

        [TearDown]
        public void TearDown()
        {
            try { host?.Dispose(); } catch { }
        }

        // the XNA calling pattern: Begin with a callback, End inside it
        static Task<T> FromBeginEnd<T>(Func<AsyncCallback, IAsyncResult> begin, Func<IAsyncResult, T> end)
        {
            var done = new TaskCompletionSource<T>();
            begin(result =>
            {
                try { done.SetResult(end(result)); }
                catch (Exception ex) { done.SetException(ex); }
            });
            return done.Task;
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
            host = await FromBeginEnd(cb => NetworkSession.BeginCreate(NetworkSessionType.SystemLink, 1, 4, 0, made, cb, null),
                NetworkSession.EndCreate);
            string tag = host.LocalGamers[0].Gamertag;

            async Task<bool> FoundWith(NetworkSessionProperties search)
            {
                using (var found = await FromBeginEnd(cb => NetworkSession.BeginFind(NetworkSessionType.SystemLink, 1, search, cb, null),
                           NetworkSession.EndFind))
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

        [Test]
        public async Task BeginJoin_EndJoin_JoinTheSession()
        {
            host = await FromBeginEnd(cb => NetworkSession.BeginCreate(NetworkSessionType.SystemLink, 1, 4, 0, null, cb, null),
                NetworkSession.EndCreate);
            string tag = host.LocalGamers[0].Gamertag;
            AvailableNetworkSession target = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (target == null && DateTime.UtcNow < deadline)
                using (var found = await FromBeginEnd(cb => NetworkSession.BeginFind(NetworkSessionType.SystemLink, 1, null, cb, "state"),
                           NetworkSession.EndFind))
                    target = found.FirstOrDefault(s => s.HostGamertag == tag);
            Assert.That(target, Is.Not.Null);

            NetworkSession client = await FromBeginEnd(cb => NetworkSession.BeginJoin(target, cb, null), NetworkSession.EndJoin);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (host.AllGamers.Count < 2 && DateTime.UtcNow < until)
                {
                    host.Update();
                    await Task.Delay(10);
                }
                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
            }
            finally { client.Dispose(); }
        }

        [Test]
        public void EndWithAResultFromAnotherBegin_IsRejected()
        {
            var wrong = new AsyncResultStub();
            Assert.Throws<ArgumentException>(() => NetworkSession.EndCreate(wrong));
            Assert.Throws<ArgumentNullException>(() => NetworkSession.EndCreate(null));
        }

        sealed class AsyncResultStub : IAsyncResult
        {
            public object AsyncState => null;
            public WaitHandle AsyncWaitHandle => null;
            public bool CompletedSynchronously => false;
            public bool IsCompleted => true;
        }
    }
}
