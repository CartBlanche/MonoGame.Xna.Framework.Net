using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Steam vertical-slice INetworkSession implementation.
    ///
    /// This implementation provides a Steam-like host/find/join/reliable-message flow using
    /// an in-memory backend so the integration seam is testable before wiring native Steamworks APIs.
    /// </summary>
    public sealed class SteamNetworkSession : INetworkSession
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, SteamNetworkGamer> gamers = new Dictionary<string, SteamNetworkGamer>();
        private readonly Queue<(byte[] Data, string SenderId)> inboundPackets = new Queue<(byte[] Data, string SenderId)>();

        private bool disposed;
        private NetworkSessionState state;
        private NetworkSessionType sessionType;

        public SteamNetworkSession()
        {
            state = NetworkSessionState.Creating;
        }

        public IReadOnlyList<INetworkGamer> AllGamers
        {
            get
            {
                lock (gate)
                {
                    return gamers.Values.Cast<INetworkGamer>().ToList();
                }
            }
        }

        public ILocalNetworkGamer LocalGamer { get; private set; }

        public NetworkSessionState State => state;

        public string SessionId { get; private set; }

        internal bool IsHostSession => LocalGamer != null && LocalGamer.IsHost;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<GamerJoinedEventArgs> GamerJoined;
        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<GameStartedEventArgs> GameStarted;
        public event EventHandler<GameEndedEventArgs> GameEnded;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        public async Task CreateAsync(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots)
        {
            ThrowIfDisposed();

            if (maxGamers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxGamers));
            }

            this.sessionType = sessionType;
            SessionId = Guid.NewGuid().ToString("N");

            var local = CreateLocalGamer("steam_host", isHost: true);

            lock (gate)
            {
                LocalGamer = local;
                gamers[local.Id] = local;
            }

            if (SteamRuntime.IsInitialized)
            {
                try
                {
                    var createCall = SteamMatchmaking.CreateLobby(ToSteamLobbyType(sessionType), maxGamers);
                    var created = await AwaitCallResultAsync<LobbyCreated_t>(createCall).ConfigureAwait(false);
                    if (created.m_eResult == EResult.k_EResultOK)
                    {
                        SessionId = created.m_ulSteamIDLobby.ToString();
                    }
                }
                catch
                {
                    // Keep vertical-slice behavior available if Steam lobby creation fails.
                }
            }

            state = NetworkSessionState.Lobby;
            SteamSessionDirectory.RegisterHost(this, SessionId, sessionType, maxGamers);
        }

        public async Task JoinAsync(string hostAddress)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(hostAddress))
            {
                throw new ArgumentException("hostAddress is required.", nameof(hostAddress));
            }

            SessionId = hostAddress.Trim();
            var local = CreateLocalGamer("steam_client", isHost: false);

            lock (gate)
            {
                LocalGamer = local;
                gamers[local.Id] = local;
                state = NetworkSessionState.Joining;
            }

            if (SteamRuntime.IsInitialized && ulong.TryParse(SessionId, out var lobbyIdRaw))
            {
                try
                {
                    var joinCall = SteamMatchmaking.JoinLobby(new CSteamID(lobbyIdRaw));
                    var entered = await AwaitCallResultAsync<LobbyEnter_t>(joinCall).ConfigureAwait(false);
                    if (entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                    {
                        throw new InvalidOperationException("Failed to join Steam lobby.");
                    }
                }
                catch
                {
                    // Keep vertical-slice behavior available if Steam lobby join fails.
                }
            }

            try
            {
                SteamSessionDirectory.JoinLobby(SessionId, this);
            }
            catch
            {
                // If host is not in local directory (different process), keep joined lobby state.
                state = NetworkSessionState.Lobby;
            }
        }

        public void SendMessage(INetworkMessage message, INetworkGamer recipient)
        {
            ThrowIfDisposed();
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (recipient == null)
            {
                throw new ArgumentNullException(nameof(recipient));
            }

            if (LocalGamer == null)
            {
                throw new InvalidOperationException("Session is not initialized.");
            }

            var payload = SerializeMessage(message);

            if (TrySendViaSteamP2P(recipient.Id, payload))
            {
                return;
            }

            SteamSessionDirectory.SendReliable(recipient.Id, payload, LocalGamer.Id);
        }

        public void BroadcastMessage(INetworkMessage message)
        {
            ThrowIfDisposed();
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (LocalGamer == null)
            {
                throw new InvalidOperationException("Session is not initialized.");
            }

            var payload = SerializeMessage(message);

            if (TryBroadcastViaSteamP2P(payload))
            {
                return;
            }

            var remoteRecipientIds = AllGamers.Where(g => !g.IsLocal).Select(g => g.Id).ToList();
            foreach (var recipientId in remoteRecipientIds)
            {
                SteamSessionDirectory.SendReliable(recipientId, payload, LocalGamer.Id);
            }
        }

        public void Update(GameTime gameTime)
        {
            ThrowIfDisposed();
            ProcessSteamP2PPackets();
            SyncSteamLobbyMembership();
            ProcessInboundPackets();
        }

        public Task CloseAsync()
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            if (SteamRuntime.IsInitialized && ulong.TryParse(SessionId, out var lobbyIdRaw))
            {
                try
                {
                    var lobbyId = new CSteamID(lobbyIdRaw);
                    var memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                    for (var i = 0; i < memberCount; i++)
                    {
                        var member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                        if (LocalGamer != null && member.ToString() == LocalGamer.Id)
                        {
                            continue;
                        }

                        SteamNetworking.CloseP2PSessionWithUser(member);
                    }

                    SteamMatchmaking.LeaveLobby(lobbyId);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }

            SteamSessionDirectory.RemoveSession(this);
            ForceSessionEnded(NetworkSessionEndReason.ClientSignedOut);
            disposed = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CloseAsync().GetAwaiter().GetResult();
        }

        internal void EnsureRemoteGamer(string gamerId, string gamertag, bool isHost)
        {
            lock (gate)
            {
                if (LocalGamer != null && gamerId == LocalGamer.Id)
                {
                    return;
                }

                if (!gamers.ContainsKey(gamerId))
                {
                    gamers[gamerId] = new SteamNetworkGamer(gamerId, gamertag, isLocal: false, isHost: isHost);
                }
            }
        }

        internal void NotifyGamerJoined(string gamerId)
        {
            INetworkGamer joined;
            lock (gate)
            {
                gamers.TryGetValue(gamerId, out var resolved);
                joined = resolved;
            }

            if (joined != null)
            {
                GamerJoined?.Invoke(this, new GamerJoinedEventArgs(joined));
            }
        }

        internal void NotifyGamerLeft(string gamerId)
        {
            INetworkGamer left;
            lock (gate)
            {
                gamers.TryGetValue(gamerId, out var resolved);
                left = resolved;
            }

            if (left != null)
            {
                GamerLeft?.Invoke(this, new GamerLeftEventArgs(left));
            }
        }

        internal void RemoveRemoteGamer(string gamerId)
        {
            lock (gate)
            {
                if (LocalGamer != null && LocalGamer.Id == gamerId)
                {
                    return;
                }

                gamers.Remove(gamerId);
            }
        }

        internal void EnqueueInbound(byte[] data, string senderId)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);

            lock (gate)
            {
                inboundPackets.Enqueue((copy, senderId));
            }
        }

        internal void ForceSessionEnded(NetworkSessionEndReason reason)
        {
            if (state == NetworkSessionState.Ended)
            {
                return;
            }

            state = NetworkSessionState.Ended;
            SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(reason));
            GameEnded?.Invoke(this, new GameEndedEventArgs());
        }

        internal void PromoteToPlayingIfNeeded()
        {
            bool raiseStarted = false;

            lock (gate)
            {
                if (state == NetworkSessionState.Lobby || state == NetworkSessionState.Joining)
                {
                    state = NetworkSessionState.Playing;
                    raiseStarted = true;
                }
            }

            if (raiseStarted)
            {
                GameStarted?.Invoke(this, new GameStartedEventArgs());
            }
        }

        private void ProcessInboundPackets()
        {
            while (true)
            {
                (byte[] Data, string SenderId) next;
                lock (gate)
                {
                    if (inboundPackets.Count == 0)
                    {
                        break;
                    }

                    next = inboundPackets.Dequeue();
                }

                if (next.Data.Length < 1)
                {
                    continue;
                }

                var reader = new PacketReader(next.Data);
                var typeId = reader.ReadByte();
                var message = NetworkMessageRegistry.CreateMessage(typeId);
                if (message == null)
                {
                    continue;
                }

                message.Deserialize(reader);

                INetworkGamer sender = null;
                lock (gate)
                {
                    gamers.TryGetValue(next.SenderId, out var resolvedSender);
                    sender = resolvedSender;
                }

                var args = new MessageReceivedEventArgs(message, null)
                {
                    Sender = sender
                };

                MessageReceived?.Invoke(this, args);
            }
        }

        private void SyncSteamLobbyMembership()
        {
            if (!SteamRuntime.IsInitialized || !ulong.TryParse(SessionId, out var lobbyIdRaw))
            {
                return;
            }

            try
            {
                var lobbyId = new CSteamID(lobbyIdRaw);
                var memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                if (memberCount <= 0)
                {
                    return;
                }

                var lobbyOwnerId = SteamMatchmaking.GetLobbyOwner(lobbyId).ToString();
                var liveMemberIds = new HashSet<string>();
                var joinedGamers = new List<INetworkGamer>();
                var leftGamers = new List<INetworkGamer>();

                lock (gate)
                {
                    for (var i = 0; i < memberCount; i++)
                    {
                        var memberSteamId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                        var memberId = memberSteamId.ToString();
                        liveMemberIds.Add(memberId);

                        var isHost = memberId == lobbyOwnerId;
                        if (gamers.TryGetValue(memberId, out var existing))
                        {
                            existing.SetHost(isHost);
                            continue;
                        }

                        if (LocalGamer != null && memberId == LocalGamer.Id)
                        {
                            if (LocalGamer is SteamNetworkGamer localSteamGamer)
                            {
                                localSteamGamer.SetHost(isHost);
                            }

                            continue;
                        }

                        var gamertag = SteamFriends.GetFriendPersonaName(memberSteamId);
                        if (string.IsNullOrWhiteSpace(gamertag))
                        {
                            gamertag = memberId;
                        }

                        var joined = new SteamNetworkGamer(memberId, gamertag, isLocal: false, isHost: isHost);
                        gamers[memberId] = joined;
                        joinedGamers.Add(joined);
                    }

                    var departedIds = gamers.Keys
                        .Where(id => (LocalGamer == null || id != LocalGamer.Id) && !liveMemberIds.Contains(id))
                        .ToList();

                    foreach (var departedId in departedIds)
                    {
                        if (gamers.TryGetValue(departedId, out var departed))
                        {
                            gamers.Remove(departedId);
                            leftGamers.Add(departed);
                        }
                    }
                }

                foreach (var joined in joinedGamers)
                {
                    GamerJoined?.Invoke(this, new GamerJoinedEventArgs(joined));
                }

                foreach (var left in leftGamers)
                {
                    GamerLeft?.Invoke(this, new GamerLeftEventArgs(left));
                }
            }
            catch
            {
                // Lobby sync is best-effort; existing fallback behavior stays active.
            }
        }

        private bool TrySendViaSteamP2P(string recipientId, byte[] payload)
        {
            if (!SteamRuntime.IsInitialized || !ulong.TryParse(recipientId, out var recipientRaw))
            {
                return false;
            }

            try
            {
                return SteamNetworking.SendP2PPacket(
                    new CSteamID(recipientRaw),
                    payload,
                    (uint)payload.Length,
                    EP2PSend.k_EP2PSendReliable,
                    0);
            }
            catch
            {
                return false;
            }
        }

        private bool TryBroadcastViaSteamP2P(byte[] payload)
        {
            if (!SteamRuntime.IsInitialized || !ulong.TryParse(SessionId, out var lobbyIdRaw))
            {
                return false;
            }

            try
            {
                var lobbyId = new CSteamID(lobbyIdRaw);
                var memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                var sentToAnyone = false;
                for (var i = 0; i < memberCount; i++)
                {
                    var member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                    if (LocalGamer != null && member.ToString() == LocalGamer.Id)
                    {
                        continue;
                    }

                    var sent = SteamNetworking.SendP2PPacket(
                        member,
                        payload,
                        (uint)payload.Length,
                        EP2PSend.k_EP2PSendReliable,
                        0);

                    sentToAnyone = sentToAnyone || sent;
                }

                return sentToAnyone;
            }
            catch
            {
                return false;
            }
        }

        private void ProcessSteamP2PPackets()
        {
            if (!SteamRuntime.IsInitialized)
            {
                return;
            }

            while (SteamNetworking.IsP2PPacketAvailable(out var packetSize, 0))
            {
                if (packetSize == 0)
                {
                    break;
                }

                var data = new byte[packetSize];
                if (!SteamNetworking.ReadP2PPacket(data, packetSize, out var bytesRead, out var senderId, 0) || bytesRead == 0)
                {
                    continue;
                }

                var senderKey = senderId.ToString();
                EnsureSteamRemoteGamer(senderId);
                EnqueueInbound(data, senderKey);
            }
        }

        private void EnsureSteamRemoteGamer(CSteamID senderId)
        {
            var senderKey = senderId.ToString();
            var gamertag = SteamFriends.GetFriendPersonaName(senderId);
            if (string.IsNullOrWhiteSpace(gamertag))
            {
                gamertag = senderKey;
            }

            EnsureRemoteGamer(senderKey, gamertag, isHost: false);
        }

        private static byte[] SerializeMessage(INetworkMessage message)
        {
            var writer = new PacketWriter();
            writer.Write(message.MessageType);
            message.Serialize(writer);
            return writer.GetData();
        }

        private static ELobbyType ToSteamLobbyType(NetworkSessionType sessionType)
        {
            return sessionType switch
            {
                NetworkSessionType.PlayerMatch => ELobbyType.k_ELobbyTypePublic,
                NetworkSessionType.Ranked => ELobbyType.k_ELobbyTypePublic,
                NetworkSessionType.SystemLink => ELobbyType.k_ELobbyTypeFriendsOnly,
                _ => ELobbyType.k_ELobbyTypePrivate
            };
        }

        private static Task<T> AwaitCallResultAsync<T>(SteamAPICall_t apiCall) where T : struct
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var callResult = CallResult<T>.Create((result, ioFailure) =>
            {
                if (ioFailure)
                {
                    tcs.TrySetException(new InvalidOperationException($"Steam API call failed for {typeof(T).Name}."));
                    return;
                }

                tcs.TrySetResult(result);
            });

            callResult.Set(apiCall);
            return tcs.Task;
        }

        private static SteamLocalNetworkGamer CreateLocalGamer(string fallbackPrefix, bool isHost)
        {
            if (TryGetSteamIdentity(out var gamerId, out var gamertag))
            {
                return new SteamLocalNetworkGamer(gamerId, gamertag, isHost);
            }

            return new SteamLocalNetworkGamer(NewGamerId(), BuildDefaultGamertag(fallbackPrefix), isHost);
        }

        private static bool TryGetSteamIdentity(out string gamerId, out string gamertag)
        {
            gamerId = null;
            gamertag = null;

            if (!SteamRuntime.IsInitialized)
            {
                return false;
            }

            try
            {
                if (!SteamRuntime.RefreshSignedInGamerIdentity())
                {
                    return false;
                }

                var steamId = SteamUser.GetSteamID();
                gamerId = steamId.ToString();
                gamertag = SteamFriends.GetPersonaName();
                return !string.IsNullOrWhiteSpace(gamerId);
            }
            catch
            {
                gamerId = null;
                gamertag = null;
                return false;
            }
        }

        private static string NewGamerId() => Guid.NewGuid().ToString("N");

        private static string BuildDefaultGamertag(string prefix)
        {
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user))
            {
                user = prefix;
            }

            return $"{user}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SteamNetworkSession));
            }
        }
    }
}
