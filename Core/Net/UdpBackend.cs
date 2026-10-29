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
        public string Name => "UDP/SystemLink";

        // the LAN carries SystemLink only
        public bool SupportsSessionType(NetworkSessionType sessionType) => sessionType == NetworkSessionType.SystemLink;

        // nobody invites you over a LAN broadcast
        public event EventHandler<InviteReceivedEventArgs> InviteReceived
        {
            add { }
            remove { }
        }

        public IPeerTransport CreateTransport() => new UdpTransport(SystemLinkSessionManager.GamePort);

        public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            // runs until stopToken is cancelled or the session closes; completing here only means it has started
            _ = SystemLinkSessionManager.AdvertiseSessionAsync(session, stopToken);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (sessionType != NetworkSessionType.SystemLink)
                return new List<AvailableNetworkSession>();

            return (await SystemLinkSessionManager.DiscoverSessionsAsync(maxLocalGamers, cancellationToken)).ToList();
        }

        public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) => Task.FromResult<IDisposable>(null);
    }
}
