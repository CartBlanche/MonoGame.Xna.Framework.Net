using Android.Gms.Nearby;
using Android.Gms.Nearby.Connection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Activity = Android.App.Activity;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Carries datagrams between Android devices over Google Nearby Connections (P2P star). A peer is a
    /// Nearby endpoint id. The host advertises and accepts every connection; a client's first datagram to
    /// the host endpoint it discovered opens the connection, and datagrams sent meanwhile wait for it.
    /// </summary>
    public sealed class AndroidNearbyTransport : IPeerTransport
    {
        internal const string ServiceId = "monogame.xna.framework.net";
        private const string PeerPrefix = "nearby:";

        // Nearby carries up to 32 KB per byte payload; the framework never sends more than ~1.2 KB.
        private readonly Channel<(byte[] Data, PeerId Sender)> incoming = Channel.CreateUnbounded<(byte[], PeerId)>();
        private readonly ConcurrentDictionary<string, Link> links = new(StringComparer.Ordinal);

        private IConnectionsClient connectionsClient;
        private LifecycleCallback lifecycleCallback;
        private DataCallback dataCallback;
        private volatile bool disposed;
        private volatile bool isBound;
        private bool advertising;

        private sealed class Link
        {
            public readonly object Gate = new();
            public bool Connected;
            public readonly List<byte[]> WaitingForConnection = new();
        }

        public bool IsBound => isBound;

        /// <summary>Nearby gives a device no id of its own, so this is always <see cref="PeerId.None"/>.</summary>
        public PeerId LocalPeer => PeerId.None;

        /// <summary>The peer that stands for a Nearby endpoint.</summary>
        public static PeerId ToPeer(string endpointId) => new PeerId(PeerPrefix + endpointId);

        /// <summary>The Nearby endpoint id a peer stands for, or null if it is not a Nearby peer.</summary>
        public static string TryGetEndpointId(PeerId peer) =>
            peer.IsValid && peer.Value.StartsWith(PeerPrefix, StringComparison.Ordinal) ? peer.Value.Substring(PeerPrefix.Length) : null;

        public void Bind()
        {
            ThrowIfDisposed();
            if (isBound)
                return;

            if (!AndroidRuntime.TryGetActivity(out Activity activity))
                throw new InvalidOperationException("Android runtime not initialized with an Activity. Call AndroidRuntime.Initialize(activity, ...) before creating sessions.");

            lifecycleCallback = new LifecycleCallback(this);
            dataCallback = new DataCallback(this);
            connectionsClient = NearbyClass.GetConnectionsClient(activity);
            isBound = true;
        }

        /// <summary>
        /// Starts advertising so other devices can find and connect to this one, which is how a host
        /// becomes joinable.
        /// </summary>
        internal async Task StartAdvertisingAsync(string name)
        {
            ThrowIfDisposed();
            if (!isBound)
                Bind();

            var options = new AdvertisingOptions.Builder().SetStrategy(Strategy.P2pStar).Build();
            await connectionsClient.StartAdvertisingAsync(name, ServiceId, lifecycleCallback, options).ConfigureAwait(false);
            advertising = true;
        }

        public void Send(PeerId peer, byte[] data, int length)
        {
            ThrowIfDisposed();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            if (!isBound)
                Bind();

            string endpointId = TryGetEndpointId(peer) ?? throw new ArgumentException($"{peer} is not a Nearby peer.", nameof(peer));
            var bytes = new byte[length];
            Buffer.BlockCopy(data, 0, bytes, 0, length);

            bool openConnection = false;
            var link = links.GetOrAdd(endpointId, _ =>
            {
                openConnection = true;
                return new Link();
            });

            lock (link.Gate)
            {
                if (!link.Connected)
                {
                    link.WaitingForConnection.Add(bytes);
                    if (!openConnection)
                        return;
                }
            }

            if (openConnection)
            {
                string name = GamerServices.SignedInGamer.Current?.Gamertag ?? "Player";
                _ = RequestConnectionAsync(name, endpointId);
                return;
            }

            SendPayload(endpointId, bytes);
        }

        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            ThrowIfDisposed();
            try
            {
                return await incoming.Reader.ReadAsync().ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                throw new ObjectDisposedException(nameof(AndroidNearbyTransport));
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (connectionsClient != null)
            {
                if (advertising)
                    connectionsClient.StopAdvertising();
                connectionsClient.StopAllEndpoints();
            }

            incoming.Writer.TryComplete();
            isBound = false;
        }

        private async Task RequestConnectionAsync(string name, string endpointId)
        {
            try
            {
                await connectionsClient.RequestConnectionAsync(name, endpointId, lifecycleCallback).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NetworkLogging.Logger.LogWarning($"[Nearby] Requesting a connection to {endpointId} failed: {ex.Message}");
                links.TryRemove(endpointId, out _);
            }
        }

        private void SendPayload(string endpointId, byte[] bytes)
        {
            try
            {
                _ = connectionsClient.SendPayloadAsync(endpointId, Payload.FromBytes(bytes));
            }
            catch (Exception ex)
            {
                NetworkLogging.Logger.LogWarning($"[Nearby] Sending to {endpointId} failed: {ex.Message}");
            }
        }

        private void OnConnected(string endpointId)
        {
            var link = links.GetOrAdd(endpointId, _ => new Link());
            byte[][] waiting;
            lock (link.Gate)
            {
                link.Connected = true;
                waiting = link.WaitingForConnection.ToArray();
                link.WaitingForConnection.Clear();
            }

            foreach (var bytes in waiting)
                SendPayload(endpointId, bytes);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AndroidNearbyTransport));
        }

        private sealed class LifecycleCallback : ConnectionLifecycleCallback
        {
            private readonly AndroidNearbyTransport transport;

            internal LifecycleCallback(AndroidNearbyTransport transport) => this.transport = transport;

            // both sides must accept; whether the peer may play is the session's call, not the transport's
            public override void OnConnectionInitiated(string endpointId, ConnectionInfo connectionInfo)
            {
                _ = transport.connectionsClient.AcceptConnectionAsync(endpointId, transport.dataCallback);
            }

            public override void OnConnectionResult(string endpointId, ConnectionResolution resolution)
            {
                if (resolution.Status.IsSuccess)
                    transport.OnConnected(endpointId);
                else
                    transport.links.TryRemove(endpointId, out _);
            }

            public override void OnDisconnected(string endpointId)
            {
                transport.links.TryRemove(endpointId, out _);
            }
        }

        private sealed class DataCallback : PayloadCallback
        {
            private readonly AndroidNearbyTransport transport;

            internal DataCallback(AndroidNearbyTransport transport) => this.transport = transport;

            public override void OnPayloadReceived(string endpointId, Payload payload)
            {
                if (payload.PayloadType != Payload.Type.Bytes)
                    return;

                var data = payload.AsBytes();
                if (data != null && data.Length > 0)
                    transport.incoming.Writer.TryWrite((data, ToPeer(endpointId)));
            }

            public override void OnPayloadTransferUpdate(string endpointId, PayloadTransferUpdate update)
            {
                // byte payloads arrive whole
            }
        }
    }
}
