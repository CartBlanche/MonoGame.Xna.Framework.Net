using GameKit;
using Foundation;
using UIKit;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.iOS
{
    public sealed class IOSNetworkSession : INetworkSession
    {
        private const string LocalPlayerId = "local";

        private readonly object gate = new();

        private GKMatch gkMatch;
        private string matchId;
        // ID of the remote peer we treat as host (set during JoinAsync snapshot).
        private string remoteHostPlayerId;
        private bool isHost;
        private bool disposed;
        private NetworkSessionState state = NetworkSessionState.Creating;

        private readonly Dictionary<string, IOSNetworkGamer> gamers = new(StringComparer.Ordinal);
        private IOSLocalNetworkGamer localGamer;
        private SessionMatchDelegate matchDelegate;

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
        public string SessionId => matchId;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<GamerJoinedEventArgs> GamerJoined;
        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<GameStartedEventArgs> GameStarted;
        public event EventHandler<GameEndedEventArgs> GameEnded;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        public Task CreateAsync(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots)
        {
            ThrowIfDisposed();

            if (!IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime not initialized. Call IOSRuntime.Initialize() first.");

            matchId = Guid.NewGuid().ToString("N");
            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? "IOSHost";
            localGamer = new IOSLocalNetworkGamer(LocalPlayerId, gamertag, isHost: true);
            matchDelegate = new SessionMatchDelegate(this);

            lock (gate) { gamers[LocalPlayerId] = localGamer; }

            var request = new GKMatchRequest
            {
                MinPlayers = 2,
                MaxPlayers = (nint)Math.Max(2, maxGamers),
                QueueName = matchId
            };

            // Fire and forget: GKMatchmaker blocks until minPlayers join.
            // GamerJoined fires via SessionMatchDelegate as peers connect.
            _ = ConnectMatchAsync(request, hostSide: true);

            isHost = true;
            state = NetworkSessionState.Lobby;
            return Task.CompletedTask;
        }

        public async Task JoinAsync(string joinAddress)
        {
            ThrowIfDisposed();

            if (!IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime not initialized. Call IOSRuntime.Initialize() first.");

            matchId = joinAddress;
            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? "IOSClient";
            localGamer = new IOSLocalNetworkGamer(LocalPlayerId, gamertag, isHost: false);
            matchDelegate = new SessionMatchDelegate(this);

            lock (gate) { gamers[LocalPlayerId] = localGamer; }

            var request = new GKMatchRequest
            {
                MinPlayers = 2,
                MaxPlayers = 8,
                QueueName = joinAddress
            };

            // Await: host is already advertising so GameKit matches quickly.
            await ConnectMatchAsync(request, hostSide: false).ConfigureAwait(false);

            state = NetworkSessionState.Lobby;
        }

        public void SendMessage(INetworkMessage message, INetworkGamer recipient)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));

            IOSNetworkGamer target;
            lock (gate) { gamers.TryGetValue(recipient.Id, out target); }

            if (target == null || target.IsLocal)
                return;

            var writer = new PacketWriter();
            message.Serialize(writer);
            SendRaw(new[] { target }, writer.GetData());
        }

        public void BroadcastMessage(INetworkMessage message)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));

            var writer = new PacketWriter();
            message.Serialize(writer);
            var data = writer.GetData();

            List<IOSNetworkGamer> remotes;
            lock (gate) { remotes = gamers.Values.Where(g => !g.IsLocal).ToList(); }

            if (remotes.Count > 0)
                SendRaw(remotes, data);
        }

        public void Update(GameTime gameTime)
        {
            ThrowIfDisposed();
            // GKMatch is push-based; messages arrive via GKMatchDelegate on the main thread.
        }

        public async Task CloseAsync()
        {
            if (disposed)
                return;

            state = NetworkSessionState.Ended;

            GKMatch matchToClose;
            lock (gate)
            {
                matchToClose = gkMatch;
                gkMatch = null;
                gamers.Clear();
            }

            localGamer = null;

            if (matchToClose != null)
            {
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    matchToClose.Delegate = null;
                    matchToClose.Disconnect();
                    GKMatchmaker.SharedMatchmaker.Cancel();
                });
            }

            SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(NetworkSessionEndReason.HostEndedSession));
            GameEnded?.Invoke(this, new GameEndedEventArgs());

            await Task.CompletedTask;
        }

        public void PromoteToPlayingIfNeeded()
        {
            bool raiseStarted;
            lock (gate)
            {
                if (state != NetworkSessionState.Lobby && state != NetworkSessionState.Joining)
                    return;
                state = NetworkSessionState.Playing;
                raiseStarted = true;
            }

            if (raiseStarted)
                GameStarted?.Invoke(this, new GameStartedEventArgs());
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            CloseAsync().GetAwaiter().GetResult();
        }

        private async Task ConnectMatchAsync(GKMatchRequest request, bool hostSide)
        {
            var tcs = new TaskCompletionSource<GKMatch>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                GKMatchmaker.SharedMatchmaker.FindMatch(request, (match, error) =>
                {
                    if (error != null)
                        tcs.TrySetException(new InvalidOperationException($"GKMatchmaker.FindMatch failed: {error.LocalizedDescription}"));
                    else if (match == null)
                        tcs.TrySetException(new InvalidOperationException("GKMatchmaker returned null match."));
                    else
                        tcs.TrySetResult(match);
                });
            });

            GKMatch resolvedMatch;
            try
            {
                resolvedMatch = await tcs.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOSNetworkSession] ConnectMatchAsync failed: {ex.Message}");
                if (!hostSide) throw;
                return;
            }

            lock (gate) { gkMatch = resolvedMatch; }
            resolvedMatch.Delegate = matchDelegate;

            // Snapshot any players already present. For the joining side, the first player
            // is the host; record them so RemoveGamer can detect host departure.
            bool firstPlayerForJoiner = !hostSide;
            if (resolvedMatch.Players != null)
            {
                foreach (var player in resolvedMatch.Players)
                {
                    var uid = PlayerUid(player);
                    if (string.IsNullOrEmpty(uid)) continue;

                    if (firstPlayerForJoiner)
                    {
                        lock (gate) { remoteHostPlayerId = uid; }
                        firstPlayerForJoiner = false;
                    }

                    AddGamer(player);
                }
            }
        }

        internal void AddGamer(GKPlayer player)
        {
            if (player == null) return;

            var uid = PlayerUid(player);
            if (string.IsNullOrEmpty(uid)) return;

            IOSNetworkGamer gamer;
            lock (gate)
            {
                if (gamers.ContainsKey(uid))
                    return;

                gamer = new IOSNetworkGamer(uid, player.DisplayName ?? player.Alias ?? uid, isLocal: false, isHost: false);
                gamers[uid] = gamer;
            }

            GamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer));
        }

        internal void RemoveGamer(string playerId, NetworkSessionEndReason reason)
        {
            IOSNetworkGamer gamer;
            bool hostLeft;
            lock (gate)
            {
                if (!gamers.TryGetValue(playerId, out gamer)) return;
                gamers.Remove(playerId);
                hostLeft = !isHost && string.Equals(playerId, remoteHostPlayerId, StringComparison.Ordinal);
            }

            GamerLeft?.Invoke(this, new GamerLeftEventArgs(gamer));

            if (hostLeft)
            {
                state = NetworkSessionState.Ended;
                SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(NetworkSessionEndReason.HostEndedSession));
                GameEnded?.Invoke(this, new GameEndedEventArgs());
            }
        }

        private void SendRaw(IEnumerable<IOSNetworkGamer> targets, byte[] data)
        {
            GKMatch match;
            lock (gate) { match = gkMatch; }
            if (match == null || match.Players == null) return;

            var players = targets
                .Select(t => match.Players.FirstOrDefault(p =>
                    string.Equals(PlayerUid(p), t.PlayerId, StringComparison.Ordinal)))
                .Where(p => p != null)
                .ToArray();

            if (players.Length == 0) return;

            var nsData = NSData.FromArray(data);
            // CA1422: GKMatchSendDataMode.Reliable is tagged [ObsoletedOSPlatform("ios7.0")] in the binding
            // because Apple deprecated the older string-playerID overload in iOS 7. The GKPlayer[] overload
            // we call here IS the current API; there is no alternative that avoids the mode parameter.
#pragma warning disable CA1422
            match.SendData(nsData, players, GKMatchSendDataMode.Reliable, out var error);
#pragma warning restore CA1422
            if (error != null)
                Debug.WriteLine($"[IOSNetworkSession] GKMatch.SendData failed: {error.LocalizedDescription}");
        }

        private static string PlayerUid(GKPlayer player) =>
            player?.GamePlayerId ?? player?.DisplayName ?? player?.Alias;

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(IOSNetworkSession));
        }

        private sealed class SessionMatchDelegate : GKMatchDelegate
        {
            private readonly IOSNetworkSession session;

            internal SessionMatchDelegate(IOSNetworkSession session)
            {
                this.session = session;
            }

            public override void DataReceivedFromPlayer(GKMatch match, NSData data, GKPlayer player)
            {
                if (data == null || data.Length == 0)
                    return;

                var bytes = data.ToArray();
                var reader = new PacketReader(bytes);
                var typeId = reader.ReadByte();
                var msg = NetworkMessageRegistry.CreateMessage(typeId);
                if (msg == null) return;
                msg.Deserialize(reader);

                IOSNetworkGamer sender;
                var uid = PlayerUid(player);
                lock (session.gate) { session.gamers.TryGetValue(uid ?? "", out sender); }

                session.MessageReceived?.Invoke(session, new MessageReceivedEventArgs(msg, null) { Sender = sender });
            }

            public override void StateChangedForPlayer(GKMatch match, GKPlayer player, GKPlayerConnectionState state)
            {
                if (player == null) return;

                var uid = PlayerUid(player);
                if (string.IsNullOrEmpty(uid)) return;

                switch (state)
                {
                    case GKPlayerConnectionState.Connected:
                        session.AddGamer(player);
                        break;

                    case GKPlayerConnectionState.Disconnected:
                        session.RemoveGamer(uid, NetworkSessionEndReason.Disconnected);
                        break;
                }
            }
        }
    }
}
