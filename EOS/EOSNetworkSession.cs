using Epic.OnlineServices;
using Lobby = Epic.OnlineServices.Lobby;
using P2P = Epic.OnlineServices.P2P;
using Platform = Epic.OnlineServices.Platform;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.EOS
{
    public sealed class EOSNetworkSession : INetworkSession
    {
        private const string SocketName = "MGNET";
        private const byte GameChannel = 0;
        private const int MaxPacketBytes = P2P.P2PInterface.MAX_PACKET_SIZE;

        private readonly object gate = new();

        private Platform.PlatformInterface platform;
        private Lobby.LobbyInterface lobbyInterface;
        private P2P.P2PInterface p2pInterface;
        private ProductUserId localUserId;
        private P2P.SocketId socketId;

        private string lobbyId;
        private bool isHost;
        private bool disposed;
        private NetworkSessionState state = NetworkSessionState.Creating;

        private readonly Dictionary<string, EOSNetworkGamer> gamers = new(StringComparer.Ordinal);
        private EOSLocalNetworkGamer localGamer;

        private ulong notifyMemberStatus = Common.INVALID_NOTIFICATIONID;
        private ulong notifyConnectionRequest = Common.INVALID_NOTIFICATIONID;
        private ulong notifyConnectionClosed = Common.INVALID_NOTIFICATIONID;

        private readonly byte[] recvBuffer = new byte[MaxPacketBytes];

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

        public string SessionId => lobbyId;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<GamerJoinedEventArgs> GamerJoined;
        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<GameStartedEventArgs> GameStarted;
        public event EventHandler<GameEndedEventArgs> GameEnded;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        public async Task CreateAsync(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots)
        {
            ThrowIfDisposed();
            InitializePlatformReferences();

            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? localUserId.ToString();
            lock (gate)
            {
                localGamer = new EOSLocalNetworkGamer(localUserId, gamertag, isHost: true);
                gamers[localGamer.Id] = localGamer;
            }

            var bucketId = GetEffectiveBucketId();

            var createOpts = new Lobby.CreateLobbyOptions
            {
                LocalUserId = localUserId,
                MaxLobbyMembers = (uint)Math.Max(2, maxGamers),
                PermissionLevel = Lobby.LobbyPermissionLevel.Publicadvertised,
                BucketId = bucketId,
                DisableHostMigration = true,
                EnableJoinById = true,
                AllowInvites = false
            };

            var createTcs = new TaskCompletionSource<Lobby.CreateLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            lobbyInterface.CreateLobby(ref createOpts, null, (ref Lobby.CreateLobbyCallbackInfo info) =>
            {
                createTcs.TrySetResult(info);
            });

            var createResult = await EOSClient.WaitForCallbackAsync(createTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.CreateLobby").ConfigureAwait(false);
            if (createResult.ResultCode != Result.Success)
                throw new InvalidOperationException($"EOS CreateLobby failed: {createResult.ResultCode}");

            lobbyId = createResult.LobbyId;

            await SetBucketAttributeAsync(bucketId).ConfigureAwait(false);

            RegisterNotifications();
            isHost = true;
            state = NetworkSessionState.Lobby;
        }

        public async Task JoinAsync(string joinAddress)
        {
            ThrowIfDisposed();
            InitializePlatformReferences();

            var joinOpts = new Lobby.JoinLobbyByIdOptions
            {
                LobbyId = joinAddress,
                LocalUserId = localUserId
            };

            var joinTcs = new TaskCompletionSource<Lobby.JoinLobbyByIdCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            lobbyInterface.JoinLobbyById(ref joinOpts, null, (ref Lobby.JoinLobbyByIdCallbackInfo info) =>
            {
                joinTcs.TrySetResult(info);
            });

            var joinResult = await EOSClient.WaitForCallbackAsync(joinTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.JoinLobbyById").ConfigureAwait(false);
            if (joinResult.ResultCode != Result.Success)
                throw new InvalidOperationException($"EOS JoinLobbyById failed: {joinResult.ResultCode}");

            lobbyId = joinAddress;

            var gamertag = GamerServices.SignedInGamer.Current?.Gamertag ?? localUserId.ToString();
            lock (gate)
            {
                localGamer = new EOSLocalNetworkGamer(localUserId, gamertag, isHost: false);
                gamers[localGamer.Id] = localGamer;
            }

            SnapshotExistingMembers();
            RegisterNotifications();
            state = NetworkSessionState.Lobby;

            List<EOSNetworkGamer> peersToGreet;
            lock (gate) { peersToGreet = gamers.Values.Where(g => !g.IsLocal).ToList(); }
            foreach (var peer in peersToGreet)
                SendHello(peer.ProductUserId);
        }

        public void SendMessage(INetworkMessage message, INetworkGamer recipient)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));

            EOSNetworkGamer eosGamer;
            lock (gate) { gamers.TryGetValue(recipient.Id, out eosGamer); }

            if (eosGamer == null || eosGamer.IsLocal)
                return;

            var writer = new PacketWriter();
            message.Serialize(writer);
            var data = writer.GetData();
            if (data.Length > MaxPacketBytes)
                throw new InvalidOperationException($"Message too large ({data.Length} bytes). EOS P2P max is {MaxPacketBytes}.");

            SendRaw(eosGamer.ProductUserId, new ArraySegment<byte>(data));
        }

        public void BroadcastMessage(INetworkMessage message)
        {
            ThrowIfDisposed();
            if (message == null) throw new ArgumentNullException(nameof(message));

            var writer = new PacketWriter();
            message.Serialize(writer);
            var data = writer.GetData();

            List<EOSNetworkGamer> remotes;
            lock (gate) { remotes = gamers.Values.Where(g => !g.IsLocal).ToList(); }

            foreach (var remote in remotes)
                SendRaw(remote.ProductUserId, new ArraySegment<byte>(data));
        }

        public void Update(GameTime gameTime)
        {
            ThrowIfDisposed();

            if (p2pInterface == null || localUserId == null)
                return;

            while (true)
            {
                var sizeOpts = new P2P.GetNextReceivedPacketSizeOptions
                {
                    LocalUserId = localUserId,
                    RequestedChannel = GameChannel
                };

                if (p2pInterface.GetNextReceivedPacketSize(ref sizeOpts, out uint packetSize) != Result.Success)
                    break;

                var buf = packetSize <= recvBuffer.Length
                    ? new ArraySegment<byte>(recvBuffer, 0, (int)packetSize)
                    : new ArraySegment<byte>(new byte[packetSize]);

                var recvOpts = new P2P.ReceivePacketOptions
                {
                    LocalUserId = localUserId,
                    MaxDataSizeBytes = packetSize,
                    RequestedChannel = GameChannel
                };

                ProductUserId peerId = null;
                var rxSocket = new P2P.SocketId { SocketName = SocketName };
                if (p2pInterface.ReceivePacket(ref recvOpts, ref peerId, ref rxSocket, out byte _, buf, out uint written) != Result.Success)
                    break;

                if (written <= 1)
                    continue; // hello/handshake — discard

                var payload = buf.Array![(buf.Offset)..(buf.Offset + (int)written)];
                var reader = new PacketReader(payload);
                var typeId = reader.ReadByte();
                var msg = NetworkMessageRegistry.CreateMessage(typeId);
                if (msg == null) continue;
                msg.Deserialize(reader);

                EOSNetworkGamer sender;
                lock (gate) { gamers.TryGetValue(peerId?.ToString() ?? "", out sender); }

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(msg, null) { Sender = sender });
            }
        }

        public async Task CloseAsync()
        {
            if (disposed)
                return;

            state = NetworkSessionState.Ended;

            RemoveNotifications();

            if (p2pInterface != null && localUserId != null)
            {
                var closeOpts = new P2P.CloseConnectionsOptions
                {
                    LocalUserId = localUserId,
                    SocketId = socketId
                };
                p2pInterface.CloseConnections(ref closeOpts);
            }

            if (lobbyInterface != null && localUserId != null && lobbyId != null)
            {
                if (isHost)
                {
                    var destroyOpts = new Lobby.DestroyLobbyOptions
                    {
                        LocalUserId = localUserId,
                        LobbyId = lobbyId
                    };
                    var destroyTcs = new TaskCompletionSource<Lobby.DestroyLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                    lobbyInterface.DestroyLobby(ref destroyOpts, null, (ref Lobby.DestroyLobbyCallbackInfo info) =>
                    {
                        destroyTcs.TrySetResult(info);
                    });
                    try { await EOSClient.WaitForCallbackAsync(destroyTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.DestroyLobby").ConfigureAwait(false); }
                    catch { }
                }
                else
                {
                    var leaveOpts = new Lobby.LeaveLobbyOptions
                    {
                        LocalUserId = localUserId,
                        LobbyId = lobbyId
                    };
                    var leaveTcs = new TaskCompletionSource<Lobby.LeaveLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                    lobbyInterface.LeaveLobby(ref leaveOpts, null, (ref Lobby.LeaveLobbyCallbackInfo info) =>
                    {
                        leaveTcs.TrySetResult(info);
                    });
                    try { await EOSClient.WaitForCallbackAsync(leaveTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.LeaveLobby").ConfigureAwait(false); }
                    catch { }
                }
            }

            lock (gate)
            {
                gamers.Clear();
                lobbyId = null;
            }

            localGamer = null;

            SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(NetworkSessionEndReason.HostEndedSession));
            GameEnded?.Invoke(this, new GameEndedEventArgs());
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

        // --- Private helpers ---

        private void InitializePlatformReferences()
        {
            platform = EOSRuntime.TryGetPlatform()
                ?? throw new InvalidOperationException("EOS platform not available. Call EOSRuntime.Initialize() and sign in first.");
            localUserId = EOSRuntime.TryGetCurrentProductUserId()
                ?? throw new InvalidOperationException("EOS user not authenticated. Call EOSRuntime.SignInAsync() first.");
            lobbyInterface = platform.GetLobbyInterface();
            p2pInterface = platform.GetP2PInterface();
            socketId = new P2P.SocketId { SocketName = SocketName };
        }

        private void RegisterNotifications()
        {
            var connReqOpts = new P2P.AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = localUserId,
                SocketId = socketId
            };
            notifyConnectionRequest = p2pInterface.AddNotifyPeerConnectionRequest(ref connReqOpts, null, OnConnectionRequest);

            var connClosedOpts = new P2P.AddNotifyPeerConnectionClosedOptions
            {
                LocalUserId = localUserId,
                SocketId = socketId
            };
            notifyConnectionClosed = p2pInterface.AddNotifyPeerConnectionClosed(ref connClosedOpts, null, OnConnectionClosed);

            var memberStatusOpts = new Lobby.AddNotifyLobbyMemberStatusReceivedOptions();
            notifyMemberStatus = lobbyInterface.AddNotifyLobbyMemberStatusReceived(ref memberStatusOpts, null, OnMemberStatus);
        }

        private void RemoveNotifications()
        {
            if (p2pInterface != null)
            {
                if (notifyConnectionRequest != Common.INVALID_NOTIFICATIONID)
                    p2pInterface.RemoveNotifyPeerConnectionRequest(notifyConnectionRequest);
                if (notifyConnectionClosed != Common.INVALID_NOTIFICATIONID)
                    p2pInterface.RemoveNotifyPeerConnectionClosed(notifyConnectionClosed);
            }

            if (lobbyInterface != null && notifyMemberStatus != Common.INVALID_NOTIFICATIONID)
                lobbyInterface.RemoveNotifyLobbyMemberStatusReceived(notifyMemberStatus);

            notifyConnectionRequest = Common.INVALID_NOTIFICATIONID;
            notifyConnectionClosed = Common.INVALID_NOTIFICATIONID;
            notifyMemberStatus = Common.INVALID_NOTIFICATIONID;
        }

        private void OnConnectionRequest(ref P2P.OnIncomingConnectionRequestInfo info)
        {
            var acceptOpts = new P2P.AcceptConnectionOptions
            {
                LocalUserId = info.LocalUserId,
                RemoteUserId = info.RemoteUserId,
                SocketId = info.SocketId
            };
            p2pInterface.AcceptConnection(ref acceptOpts);
        }

        private void OnConnectionClosed(ref P2P.OnRemoteConnectionClosedInfo info)
        {
            RemoveGamer(info.RemoteUserId, NetworkSessionEndReason.Disconnected);
        }

        private void OnMemberStatus(ref Lobby.LobbyMemberStatusReceivedCallbackInfo info)
        {
            if (info.LobbyId != lobbyId)
                return;

            switch (info.CurrentStatus)
            {
                case Lobby.LobbyMemberStatus.Joined:
                    AddGamer(info.TargetUserId);
                    break;
                case Lobby.LobbyMemberStatus.Left:
                case Lobby.LobbyMemberStatus.Kicked:
                case Lobby.LobbyMemberStatus.Disconnected:
                case Lobby.LobbyMemberStatus.Closed:
                    RemoveGamer(info.TargetUserId, NetworkSessionEndReason.HostEndedSession);
                    break;
            }
        }

        private void AddGamer(ProductUserId userId)
        {
            if (userId == null || !userId.IsValid())
                return;

            var uid = userId.ToString();

            EOSNetworkGamer gamer;
            lock (gate)
            {
                if (uid == localUserId?.ToString() || gamers.ContainsKey(uid))
                    return;

                var ownerUserId = GetLobbyOwner();
                var gamerIsHost = ownerUserId != null && ownerUserId.ToString() == uid;
                gamer = new EOSNetworkGamer(userId, uid, isLocal: false, isHost: gamerIsHost);
                gamers[uid] = gamer;
            }

            GamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer));

            if (isHost)
                SendHello(userId);
        }

        private void RemoveGamer(ProductUserId userId, NetworkSessionEndReason reason)
        {
            if (userId == null) return;
            var uid = userId.ToString();

            EOSNetworkGamer gamer;
            bool hostLeft;
            lock (gate)
            {
                if (!gamers.TryGetValue(uid, out gamer)) return;
                gamers.Remove(uid);
                hostLeft = gamer.IsHost && !isHost;
            }

            GamerLeft?.Invoke(this, new GamerLeftEventArgs(gamer));

            if (p2pInterface != null && localUserId != null)
            {
                var closeOpts = new P2P.CloseConnectionOptions
                {
                    LocalUserId = localUserId,
                    RemoteUserId = userId,
                    SocketId = socketId
                };
                p2pInterface.CloseConnection(ref closeOpts);
            }

            if (hostLeft)
            {
                state = NetworkSessionState.Ended;
                SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(reason));
                GameEnded?.Invoke(this, new GameEndedEventArgs());
            }
        }

        private void SnapshotExistingMembers()
        {
            if (lobbyInterface == null || localUserId == null || lobbyId == null)
                return;

            var detailsOpts = new Lobby.CopyLobbyDetailsHandleOptions
            {
                LobbyId = lobbyId,
                LocalUserId = localUserId
            };

            if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOpts, out var details) != Result.Success || details == null)
                return;

            var joined = new List<EOSNetworkGamer>();
            try
            {
                var ownerOpts = new Lobby.LobbyDetailsGetLobbyOwnerOptions();
                var ownerUserId = details.GetLobbyOwner(ref ownerOpts);

                var countOpts = new Lobby.LobbyDetailsGetMemberCountOptions();
                var memberCount = details.GetMemberCount(ref countOpts);

                lock (gate)
                {
                    for (uint i = 0; i < memberCount; i++)
                    {
                        var memberOpts = new Lobby.LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                        var memberId = details.GetMemberByIndex(ref memberOpts);
                        if (memberId == null || memberId.ToString() == localUserId.ToString())
                            continue;

                        var uid = memberId.ToString();
                        var memberIsHost = ownerUserId != null && ownerUserId.ToString() == uid;
                        var gamer = new EOSNetworkGamer(memberId, uid, isLocal: false, isHost: memberIsHost);
                        gamers[uid] = gamer;
                        joined.Add(gamer);
                    }
                }
            }
            finally
            {
                details.Release();
            }

            foreach (var gamer in joined)
                GamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer));
        }

        private ProductUserId GetLobbyOwner()
        {
            if (lobbyInterface == null || localUserId == null || lobbyId == null)
                return null;

            var detailsOpts = new Lobby.CopyLobbyDetailsHandleOptions
            {
                LobbyId = lobbyId,
                LocalUserId = localUserId
            };

            if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOpts, out var details) != Result.Success || details == null)
                return null;

            try
            {
                var ownerOpts = new Lobby.LobbyDetailsGetLobbyOwnerOptions();
                return details.GetLobbyOwner(ref ownerOpts);
            }
            finally
            {
                details.Release();
            }
        }

        private async Task SetBucketAttributeAsync(string bucketId)
        {
            var modOpts = new Lobby.UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = localUserId
            };

            if (lobbyInterface.UpdateLobbyModification(ref modOpts, out var modification) != Result.Success || modification == null)
            {
                Debug.WriteLine("[EOSNetworkSession] UpdateLobbyModification failed — bucket attribute not set.");
                return;
            }

            try
            {
                var attrOpts = new Lobby.LobbyModificationAddAttributeOptions
                {
                    Attribute = new Lobby.AttributeData
                    {
                        Key = "bucket",
                        Value = new Lobby.AttributeDataValue { AsUtf8 = bucketId }
                    },
                    Visibility = Lobby.LobbyAttributeVisibility.Public
                };
                modification.AddAttribute(ref attrOpts);

                var updateOpts = new Lobby.UpdateLobbyOptions
                {
                    LobbyModificationHandle = modification
                };

                var updateTcs = new TaskCompletionSource<Lobby.UpdateLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                lobbyInterface.UpdateLobby(ref updateOpts, null, (ref Lobby.UpdateLobbyCallbackInfo info) =>
                {
                    updateTcs.TrySetResult(info);
                });

                var result = await EOSClient.WaitForCallbackAsync(updateTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.UpdateLobby").ConfigureAwait(false);
                if (result.ResultCode != Result.Success)
                    Debug.WriteLine($"[EOSNetworkSession] Lobby.UpdateLobby (bucket) failed: {result.ResultCode}");
            }
            finally
            {
                modification.Release();
            }
        }

        private void SendHello(ProductUserId remoteUserId)
        {
            SendRaw(remoteUserId, new ArraySegment<byte>(new byte[] { 0 }));
        }

        private void SendRaw(ProductUserId remoteUserId, ArraySegment<byte> data)
        {
            if (p2pInterface == null || localUserId == null) return;

            var opts = new P2P.SendPacketOptions
            {
                LocalUserId = localUserId,
                RemoteUserId = remoteUserId,
                SocketId = socketId,
                Channel = GameChannel,
                Data = data,
                AllowDelayedDelivery = true,
                Reliability = P2P.PacketReliability.ReliableOrdered,
                DisableAutoAcceptConnection = false
            };

            var result = p2pInterface.SendPacket(ref opts);
            if (result != Result.Success)
                Debug.WriteLine($"[EOSNetworkSession] SendPacket failed: {result}");
        }

        private static string GetEffectiveBucketId()
        {
            return "MonoGame.Xna.Framework.Net";
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(EOSNetworkSession));
        }
    }
}
