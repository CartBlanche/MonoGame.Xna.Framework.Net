using Android.Gms.Nearby;
using Android.Gms.Nearby.Connection;
using Android.Gms.Extensions;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.Android
{
    public sealed class AndroidNetworkSession : INetworkSession
    {
        internal const string ServiceId = "monogame.xna.framework.net";
        private const string LocalEndpointId = "local";
        private const int MaxPayloadBytes = 32 * 1024;

        private readonly object gate = new();

        private Activity activity;
        private ConnectionsClient connectionsClient;
        private string sessionId;
        private bool isHost;
        private bool disposed;
        private NetworkSessionState state = NetworkSessionState.Creating;

        private readonly Dictionary<string, AndroidNetworkGamer> gamers = new(StringComparer.Ordinal);
        private AndroidLocalNetworkGamer localGamer;

        private SessionLifecycleCallback lifecycleCallback;
        private SessionPayloadCallback payloadCallback;

        // endpointId → name, stored in OnConnectionInitiated and consumed in OnConnectionResult
        private readonly Dictionary<string, string> pendingEndpointNames = new(StringComparer.Ordinal);
        // endpointId → TCS, used by JoinAsync to await handshake completion
        private readonly Dictionary<string, TaskCompletionSource<bool>> pendingConnections = new(StringComparer.Ordinal);

        public IReadOnlyList<INetworkGamer> AllGamers
        {
            get
            {
                ThrowIfDisposed();
                lock (gate) { return gamers.Values.Cast<INetworkGamer>().ToList(); }
            }
        }

        public ILocalNetworkGamer LocalGamer
        {
            get { ThrowIfDisposed(); return localGamer; }
        }

        public NetworkSessionState State => state;
        public string SessionId => sessionId;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<GamerJoinedEventArgs> GamerJoined;
        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<GameStartedEventArgs> GameStarted;
        public event EventHandler<GameEndedEventArgs> GameEnded;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        public async Task CreateAsync(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots)
        {
            ThrowIfDisposed();
            InitializeReferences();

            sessionId = Guid.NewGuid().ToString("N");
            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? "AndroidHost";
            localGamer = new AndroidLocalNetworkGamer(LocalEndpointId, gamertag, isHost: true);
            lock (gate) { gamers[LocalEndpointId] = localGamer; }

            var options = new AdvertisingOptions.Builder()
                .SetStrategy(Strategy.P2pStar)
                .Build();

            await connectionsClient
                .StartAdvertising(gamertag, ServiceId, lifecycleCallback, options)
                .AsAsync<Java.Lang.Object>()
                .ConfigureAwait(false);

            isHost = true;
            state = NetworkSessionState.Lobby;
        }

        public async Task JoinAsync(string joinAddress)
        {
            ThrowIfDisposed();
            InitializeReferences();

            sessionId = joinAddress;
            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? "AndroidClient";
            localGamer = new AndroidLocalNetworkGamer(LocalEndpointId, gamertag, isHost: false);
            lock (gate) { gamers[LocalEndpointId] = localGamer; }

            var connectionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate) { pendingConnections[joinAddress] = connectionTcs; }

            await connectionsClient
                .RequestConnection(gamertag, joinAddress, lifecycleCallback)
                .AsAsync<Java.Lang.Object>()
                .ConfigureAwait(false);

            var success = await connectionTcs.Task.ConfigureAwait(false);
            if (!success)
                throw new InvalidOperationException($"Failed to connect to Nearby endpoint: {joinAddress}");

            state = NetworkSessionState.Lobby;
        }

        public void SendMessage(INetworkMessage message, INetworkGamer recipient)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));

            AndroidNetworkGamer target;
            lock (gate) { gamers.TryGetValue(recipient.Id, out target); }

            if (target == null || target.IsLocal)
                return;

            var writer = new PacketWriter();
            message.Serialize(writer);
            var data = writer.GetData();
            if (data.Length > MaxPayloadBytes)
                throw new InvalidOperationException($"Message too large ({data.Length} bytes). Nearby Connections max is {MaxPayloadBytes}.");

            SendRaw(target.EndpointId, data);
        }

        public void BroadcastMessage(INetworkMessage message)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));

            var writer = new PacketWriter();
            message.Serialize(writer);
            var data = writer.GetData();

            List<AndroidNetworkGamer> remotes;
            lock (gate) { remotes = gamers.Values.Where(g => !g.IsLocal).ToList(); }

            foreach (var remote in remotes)
                SendRaw(remote.EndpointId, data);
        }

        public void Update(GameTime gameTime)
        {
            ThrowIfDisposed();
            // Nearby Connections is push-based; messages arrive via PayloadCallback on a background thread.
        }

        public async Task CloseAsync()
        {
            if (disposed)
                return;

            state = NetworkSessionState.Ended;

            if (connectionsClient != null)
            {
                List<AndroidNetworkGamer> remotes;
                lock (gate) { remotes = gamers.Values.Where(g => !g.IsLocal).ToList(); }

                foreach (var remote in remotes)
                    connectionsClient.DisconnectFromEndpoint(remote.EndpointId);

                if (isHost)
                    connectionsClient.StopAdvertising();
            }

            lock (gate) { gamers.Clear(); }
            localGamer = null;

            SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(NetworkSessionEndReason.HostEndedSession));
            GameEnded?.Invoke(this, new GameEndedEventArgs());

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            CloseAsync().GetAwaiter().GetResult();
        }

        private void InitializeReferences()
        {
            if (!AndroidRuntime.TryGetActivity(out activity))
                throw new InvalidOperationException("Android runtime not initialized with an Activity. Call AndroidRuntime.Initialize(activity, ...) before creating sessions.");

            lifecycleCallback = new SessionLifecycleCallback(this);
            payloadCallback = new SessionPayloadCallback(this);
            connectionsClient = Nearby.GetConnectionsClient(activity);
        }

        internal void AddGamer(string endpointId, string gamertag, bool asHost)
        {
            AndroidNetworkGamer gamer;
            lock (gate)
            {
                if (gamers.ContainsKey(endpointId))
                    return;

                gamer = new AndroidNetworkGamer(endpointId, gamertag, isLocal: false, isHost: asHost);
                gamers[endpointId] = gamer;
            }

            GamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer));
        }

        internal void RemoveGamer(string endpointId, NetworkSessionEndReason reason)
        {
            AndroidNetworkGamer gamer;
            bool hostLeft;
            lock (gate)
            {
                if (!gamers.TryGetValue(endpointId, out gamer)) return;
                gamers.Remove(endpointId);
                hostLeft = gamer.IsHost && !isHost;
            }

            GamerLeft?.Invoke(this, new GamerLeftEventArgs(gamer));

            if (hostLeft)
            {
                state = NetworkSessionState.Ended;
                SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(NetworkSessionEndReason.HostEndedSession));
                GameEnded?.Invoke(this, new GameEndedEventArgs());
            }
        }

        private void SendRaw(string endpointId, byte[] data)
        {
            if (connectionsClient == null) return;

            try
            {
                connectionsClient.SendPayload(endpointId, Payload.FromBytes(data));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidNetworkSession] SendPayload to {endpointId} failed: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AndroidNetworkSession));
        }

        private sealed class SessionLifecycleCallback : ConnectionLifecycleCallback
        {
            private readonly AndroidNetworkSession session;

            internal SessionLifecycleCallback(AndroidNetworkSession session)
            {
                this.session = session;
            }

            public override void OnConnectionInitiated(string endpointId, ConnectionInfo connectionInfo)
            {
                lock (session.gate)
                    session.pendingEndpointNames[endpointId] = connectionInfo?.EndpointName ?? endpointId;

                // Auto-accept all incoming connections on our service.
                _ = session.connectionsClient
                    .AcceptConnection(endpointId, session.payloadCallback)
                    .AsAsync<Java.Lang.Object>();
            }

            public override void OnConnectionResult(string endpointId, ConnectionResolution resolution)
            {
                string gamertag;
                lock (session.gate)
                {
                    session.pendingEndpointNames.TryGetValue(endpointId, out gamertag);
                    session.pendingEndpointNames.Remove(endpointId);
                }

                if (resolution.Status.IsSuccess)
                {
                    session.AddGamer(endpointId, gamertag ?? endpointId, asHost: !session.isHost);

                    TaskCompletionSource<bool> tcs;
                    lock (session.gate)
                    {
                        session.pendingConnections.TryGetValue(endpointId, out tcs);
                        session.pendingConnections.Remove(endpointId);
                    }
                    tcs?.TrySetResult(true);
                }
                else
                {
                    TaskCompletionSource<bool> tcs;
                    lock (session.gate)
                    {
                        session.pendingConnections.TryGetValue(endpointId, out tcs);
                        session.pendingConnections.Remove(endpointId);
                    }
                    tcs?.TrySetResult(false);
                }
            }

            public override void OnDisconnected(string endpointId)
            {
                session.RemoveGamer(endpointId, NetworkSessionEndReason.Disconnected);
            }
        }

        private sealed class SessionPayloadCallback : PayloadCallback
        {
            private readonly AndroidNetworkSession session;

            internal SessionPayloadCallback(AndroidNetworkSession session)
            {
                this.session = session;
            }

            public override void OnPayloadReceived(string endpointId, Payload payload)
            {
                if (payload.PayloadType != Payload.Type.Bytes)
                    return;

                var data = payload.AsBytes();
                if (data == null || data.Length == 0)
                    return;

                var reader = new PacketReader(data);
                var typeId = reader.ReadByte();
                var msg = NetworkMessageRegistry.CreateMessage(typeId);
                if (msg == null) return;
                msg.Deserialize(reader);

                AndroidNetworkGamer sender;
                lock (session.gate) { session.gamers.TryGetValue(endpointId, out sender); }

                session.MessageReceived?.Invoke(session, new MessageReceivedEventArgs(msg, null) { Sender = sender });
            }

            public override void OnPayloadTransferUpdate(string endpointId, PayloadTransferUpdate update)
            {
                // Bytes payloads are atomic; no transfer progress tracking needed.
            }
        }
    }
}
