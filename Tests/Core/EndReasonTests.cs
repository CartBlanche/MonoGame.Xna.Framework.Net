using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>Why a session ends, as XNA's NetworkSessionEndReason says it.</summary>
    [TestFixture]
    public class EndReasonTests
    {
        private sealed class FailingTransport : IPeerTransport
        {
            public bool IsBound { get; private set; }
            public PeerId LocalPeer => PeerId.None;
            public void Bind() => IsBound = true;
            public void Send(PeerId peer, byte[] data, int length) { }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => throw new IOException("the network went away");
            public void Dispose() { }
        }

        private sealed class FailingBackend : INetworkBackend
        {
            public string Name => "Failing";
            public bool SupportsSessionType(NetworkSessionType sessionType) => true;
            public event EventHandler<InviteReceivedEventArgs> InviteReceived { add { } remove { } }
            public IPeerTransport CreateTransport() => new FailingTransport();
            public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken) => Task.CompletedTask;
            public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<AvailableNetworkSession>>(new List<AvailableNetworkSession>());
            public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) =>
                Task.FromResult<IDisposable>(null);
        }

        [Test]
        public async Task ATransportThatKeepsFailing_EndsTheSessionAsNetworkError()
        {
            var original = NetworkServiceProvider.Backend;
            NetworkServiceProvider.SetBackend(new FailingBackend());
            NetworkSession session = null;
            try
            {
                session = await NetworkSession.CreateAsync(NetworkSessionType.PlayerMatch, 1, 4, 0, null);
                NetworkSessionEndReason? reason = null;
                session.SessionEnded += (s, e) => reason = e.EndReason;

                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (reason == null && DateTime.UtcNow < deadline)
                {
                    session.Update();
                    await Task.Delay(20);
                }

                Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.NetworkError));
                Assert.That(session.SessionState, Is.EqualTo(NetworkSessionState.Ended));
            }
            finally
            {
                session?.Dispose();
                NetworkServiceProvider.SetBackend(original);
            }
        }

        [Test]
        public async Task DisposingASession_EndsItAsClientSignedOut_Once()
        {
            var session = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            var reasons = new List<NetworkSessionEndReason>();
            session.SessionEnded += (s, e) => reasons.Add(e.EndReason);

            session.Dispose();
            session.Dispose();

            Assert.That(reasons, Is.EqualTo(new[] { NetworkSessionEndReason.ClientSignedOut }));
        }
    }
}
