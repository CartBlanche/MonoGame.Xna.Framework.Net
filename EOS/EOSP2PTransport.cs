using Epic.OnlineServices;
using P2P = Epic.OnlineServices.P2P;
using Platform = Epic.OnlineServices.Platform;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Carries datagrams between Epic Online Services users over EOS P2P (relayed or direct, as EOS
    /// decides). A peer is a product user id. Everything is sent unreliably and unordered: the
    /// framework's reliable channel supplies reliability for every transport.
    /// </summary>
    public sealed class EOSP2PTransport : IPeerTransport
    {
        private const string SocketName = "MGNET";
        private const byte GameChannel = 0;
        private const string PeerPrefix = "eos:";

        private readonly Func<ProductUserId, bool> acceptPeer;
        private readonly ConcurrentDictionary<string, ProductUserId> knownUsers = new(StringComparer.Ordinal);

        // A joiner's connection request can reach us before our copy of the lobby lists them as a member
        // (EOS pushes member changes separately), so a stranger is given a little while to show up in the
        // lobby before the request is refused.
        private static readonly TimeSpan PendingWindow = TimeSpan.FromSeconds(10);
        private const int MaxPending = 32;
        private readonly List<PendingRequest> pending = new();

        private readonly struct PendingRequest
        {
            public PendingRequest(P2P.OnIncomingConnectionRequestInfo info, DateTime expires) { Info = info; Expires = expires; }
            public P2P.OnIncomingConnectionRequestInfo Info { get; }
            public DateTime Expires { get; }
        }

        private Platform.PlatformInterface platform;
        private P2P.P2PInterface p2p;
        private ProductUserId localUserId;
        private P2P.SocketId socketId;
        private ulong notifyConnectionRequest = Common.INVALID_NOTIFICATIONID;

        private volatile bool disposed;
        private volatile bool isBound;

        /// <param name="acceptPeer">
        /// Decides whether a stranger who opens a connection to us is let in. Null accepts everyone;
        /// <see cref="EOSBackend"/> passes a check that the stranger is in a lobby we are in.
        /// </param>
        public EOSP2PTransport(Func<ProductUserId, bool> acceptPeer = null)
        {
            this.acceptPeer = acceptPeer;
        }

        public bool IsBound => isBound;

        public PeerId LocalPeer => isBound && localUserId != null ? ToPeer(localUserId) : PeerId.None;

        /// <summary>The peer that stands for an EOS user.</summary>
        public static PeerId ToPeer(ProductUserId user) => new PeerId(PeerPrefix + user);

        /// <summary>The text of the product user id a peer stands for, or null if it is not an EOS peer.</summary>
        public static string TryGetUserIdText(PeerId peer)
        {
            if (!peer.IsValid || !peer.Value.StartsWith(PeerPrefix, StringComparison.Ordinal))
                return null;
            return peer.Value.Substring(PeerPrefix.Length);
        }

        public void Bind()
        {
            ThrowIfDisposed();
            if (isBound)
                return;

            platform = EOSRuntime.TryGetPlatform()
                ?? throw new InvalidOperationException("EOS platform not available. Call EOSRuntime.Initialize() and sign in first.");
            localUserId = EOSRuntime.TryGetCurrentProductUserId()
                ?? throw new InvalidOperationException("EOS user not authenticated. Call EOSRuntime.SignInAsync() first.");
            p2p = platform.GetP2PInterface();
            socketId = new P2P.SocketId { SocketName = SocketName };

            var requestOptions = new P2P.AddNotifyPeerConnectionRequestOptions { LocalUserId = localUserId, SocketId = socketId };
            notifyConnectionRequest = p2p.AddNotifyPeerConnectionRequest(ref requestOptions, null, OnConnectionRequest);
            isBound = true;
        }

        public void Send(PeerId peer, byte[] data, int length)
        {
            ThrowIfDisposed();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > P2P.P2PInterface.MAX_PACKET_SIZE)
                throw new ArgumentOutOfRangeException(nameof(length), $"EOS P2P carries at most {P2P.P2PInterface.MAX_PACKET_SIZE} bytes per packet.");
            if (!isBound)
                Bind();

            string text = TryGetUserIdText(peer) ?? throw new ArgumentException($"{peer} is not an EOS peer.", nameof(peer));
            ProductUserId remote = knownUsers.GetOrAdd(text, id => ProductUserId.FromString(id));

            var options = new P2P.SendPacketOptions
            {
                LocalUserId = localUserId,
                RemoteUserId = remote,
                SocketId = socketId,
                Channel = GameChannel,
                Data = new ArraySegment<byte>(data, 0, length),
                AllowDelayedDelivery = true,
                Reliability = P2P.PacketReliability.UnreliableUnordered,
                DisableAutoAcceptConnection = false
            };

            var result = p2p.SendPacket(ref options);
            if (result != Result.Success)
                throw new InvalidOperationException($"EOS P2P send to {text} failed: {result}");
        }

        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            ThrowIfDisposed();
            if (!isBound)
                Bind();

            while (!disposed)
            {
                // EOS hands over connection requests, lobby changes and invitations only when it is ticked, and
                // this loop runs for as long as the session does, so it is the one place that is always there to do it
                EOSRuntime.RunCallbacks();
                ResolvePendingRequests();

                var sizeOptions = new P2P.GetNextReceivedPacketSizeOptions { LocalUserId = localUserId, RequestedChannel = GameChannel };
                if (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint size) == Result.Success && size > 0)
                {
                    var buffer = new byte[size];
                    var receiveOptions = new P2P.ReceivePacketOptions { LocalUserId = localUserId, MaxDataSizeBytes = size, RequestedChannel = GameChannel };
                    ProductUserId sender = null;
                    var socket = new P2P.SocketId { SocketName = SocketName };

                    if (p2p.ReceivePacket(ref receiveOptions, ref sender, ref socket, out byte _, new ArraySegment<byte>(buffer), out uint written) == Result.Success
                        && sender != null)
                    {
                        if (written != size)
                            Array.Resize(ref buffer, (int)written);
                        knownUsers.TryAdd(sender.ToString(), sender);
                        return (buffer, ToPeer(sender));
                    }
                }

                await Task.Delay(5).ConfigureAwait(false);
            }

            throw new ObjectDisposedException(nameof(EOSP2PTransport));
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (p2p != null)
            {
                if (notifyConnectionRequest != Common.INVALID_NOTIFICATIONID)
                    p2p.RemoveNotifyPeerConnectionRequest(notifyConnectionRequest);

                if (localUserId != null)
                {
                    var close = new P2P.CloseConnectionsOptions { LocalUserId = localUserId, SocketId = socketId };
                    p2p.CloseConnections(ref close);
                }
            }

            notifyConnectionRequest = Common.INVALID_NOTIFICATIONID;
            isBound = false;
        }

        private void OnConnectionRequest(ref P2P.OnIncomingConnectionRequestInfo info)
        {
            if (acceptPeer == null || acceptPeer(info.RemoteUserId))
            {
                Accept(info);
                return;
            }

            // not in a lobby of ours (yet): see ResolvePendingRequests
            lock (pending)
            {
                string who = info.RemoteUserId?.ToString();
                if (pending.Count >= MaxPending || pending.Any(p => p.Info.RemoteUserId?.ToString() == who))
                    return;
                pending.Add(new PendingRequest(info, DateTime.UtcNow + PendingWindow));
            }
            NetworkLogging.Logger.LogInfo($"[EOS] Holding a P2P connection request from {info.RemoteUserId} until they show up in one of our lobbies");
        }

        private void Accept(P2P.OnIncomingConnectionRequestInfo info)
        {
            var accept = new P2P.AcceptConnectionOptions { LocalUserId = info.LocalUserId, RemoteUserId = info.RemoteUserId, SocketId = info.SocketId };
            p2p.AcceptConnection(ref accept);
        }

        // accepts a held request once its sender is in one of our lobbies, and refuses it when the wait is over
        private void ResolvePendingRequests()
        {
            PendingRequest[] waiting;
            lock (pending)
            {
                if (pending.Count == 0)
                    return;
                waiting = pending.ToArray();
            }

            foreach (var request in waiting)
            {
                bool admit = acceptPeer == null || acceptPeer(request.Info.RemoteUserId);
                if (!admit && DateTime.UtcNow < request.Expires)
                    continue;

                lock (pending)
                    pending.RemoveAll(p => p.Info.RemoteUserId?.ToString() == request.Info.RemoteUserId?.ToString());

                if (admit)
                    Accept(request.Info);
                else
                    NetworkLogging.Logger.LogWarning($"[EOS] Refused a P2P connection from {request.Info.RemoteUserId}: not in one of our lobbies");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(EOSP2PTransport));
        }
    }
}
