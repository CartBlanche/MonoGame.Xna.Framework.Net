using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// The default back-end: sessions on the local network (SystemLink). Hosts announce themselves by
    /// UDP broadcast and carry gameplay over UDP.
    /// </summary>
    public sealed class UdpBackend : INetworkBackend
    {
        /// <inheritdoc/>
        public string Name => "UDP/SystemLink";

        // the LAN carries SystemLink only
        /// <summary>Returns true for <see cref="NetworkSessionType.SystemLink"/> only.</summary>
        public bool SupportsSessionType(NetworkSessionType sessionType) => sessionType == NetworkSessionType.SystemLink;

        // nobody invites you over a LAN broadcast
        /// <summary>Never raised: a LAN has no invitations. Handlers can be added and removed but are not kept.</summary>
        public event EventHandler<InviteReceivedEventArgs> InviteReceived
        {
            add { }
            remove { }
        }

        /// <summary>Creates a <see cref="UdpTransport"/> that prefers the SystemLink game port.</summary>
        public IPeerTransport CreateTransport() => new UdpTransport(SystemLinkSessionManager.GamePort);

        /// <summary>Starts the LAN broadcast announcing the session and returns at once. The announcements stop when <paramref name="stopToken"/> is cancelled or the session closes; <paramref name="transport"/> is not used.</summary>
        /// <exception cref="System.Net.Sockets.SocketException">The broadcast socket cannot be opened.</exception>
        public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            // opening the socket happens in this call, so a failure to start is thrown to the caller
            Task announcing = SystemLinkSessionManager.AdvertiseSessionAsync(session, stopToken);

            // the loop runs until stopToken is cancelled or the session closes; completing here only means it has started
            announcing.ContinueWith(
                t => NetworkLogging.Logger.LogError("[LAN] Advertising stopped unexpectedly", t.Exception!.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return Task.CompletedTask;
        }

        /// <summary>Finds SystemLink sessions announced on the LAN. Returns an empty list for any other session type.</summary>
        public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (sessionType != NetworkSessionType.SystemLink)
                return new List<AvailableNetworkSession>();

            return (await SystemLinkSessionManager.DiscoverSessionsAsync(maxLocalGamers, cancellationToken).ConfigureAwait(false)).ToList();
        }

        /// <summary>Does nothing and returns null: a LAN join needs no preparation, so there is nothing to undo.</summary>
        public Task<IDisposable?> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) => Task.FromResult<IDisposable?>(null);
    }
}
