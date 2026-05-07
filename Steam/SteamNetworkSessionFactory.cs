using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Factory for Steam-backed sessions.
    /// Implements <see cref="INetworkSessionProvider"/> so that
    /// <see cref="NetworkSession.CreateAsync"/>, <see cref="NetworkSession.FindAsync"/>, and
    /// <see cref="NetworkSession.JoinAsync"/> automatically delegate to Steam when this factory
    /// is registered with <see cref="NetworkServiceProvider"/>.
    ///
    /// Session discovery uses Steam lobbies and gameplay data uses Steam P2P transport while
    /// preserving the existing NetworkSession API used by game code.
    /// </summary>
    public sealed class SteamNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private const string LobbyGameKey = "mgnet_game";
        private const string LobbyHostSteamIdKey = "host_steamid";
        private readonly string lobbyGameValue;
        private readonly SteamFallbackMode fallbackMode;

        public SteamNetworkSessionFactory(string gameTag = null, SteamFallbackMode fallbackMode = SteamFallbackMode.PreferFallback)
        {
            lobbyGameValue = NormalizeGameTag(gameTag);
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == SteamFallbackMode.Strict;

        public string BackendName => "Steam";

        public INetworkSession CreateSession()
        {
            return new SteamNetworkSession(fallbackMode);
        }

        public Task<IEnumerable<SessionInfo>> FindSessionsAsync(NetworkSessionType sessionType)
        {
            if (!SteamRuntime.IsInitialized)
            {
                if (IsStrict)
                {
                    throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session discovery.");
                }

                if (sessionType == NetworkSessionType.SystemLink)
                {
                    return FindSystemLinkSessionsAsync(sessionType);
                }

                return Task.FromResult(SteamSessionDirectory.FindSessions(sessionType));
            }

            return FindSteamLobbiesAsync(sessionType);
        }

        // -----------------------------------------------------------------------
        // INetworkSessionProvider — NetworkSession.CreateAsync/FindAsync/JoinAsync
        // delegate here when this factory is registered with NetworkServiceProvider.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a Steam-backed <see cref="NetworkSession"/> and advertises it as a
        /// Steam lobby so remote players can discover it via Steam matchmaking.
        /// </summary>
        public async Task<NetworkSession> CreateSessionAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            int maxGamers,
            int privateGamerSlots,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            if (IsStrict && !SteamRuntime.IsInitialized)
            {
                throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session creation.");
            }

            var session = SteamRuntime.IsInitialized
                ? await NetworkSession.CreateSystemLinkSessionAsync(sessionType, maxGamers, privateGamerSlots, new SteamP2PTransport(), advertiseOnLan: false, cancellationToken).ConfigureAwait(false)
                : await NetworkSession.CreateSystemLinkSessionAsync(sessionType, maxGamers, privateGamerSlots, cancellationToken).ConfigureAwait(false);

            // Steam vertical slice currently does not support host migration.
            session.AllowHostMigration = false;

            if (SteamRuntime.IsInitialized)
            {
                if (IsStrict)
                {
                    await AdvertiseSteamLobbyAsync(session, maxGamers).ConfigureAwait(false);
                }
                else
                {
                    _ = AdvertiseSteamLobbyAsync(session, maxGamers);
                }
            }

            return session;
        }

        /// <summary>
        /// Discovers sessions via Steam lobbies and converts them to
        /// <see cref="AvailableNetworkSession"/> objects that carry the host's Steam endpoint.
        /// </summary>
        public async Task<AvailableNetworkSessionCollection> FindSessionsAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            if (!SteamRuntime.IsInitialized)
            {
                if (IsStrict)
                {
                    throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session discovery.");
                }

                var lanSessions = (await SystemLinkSessionManager
                    .DiscoverSessionsAsync(maxLocalGamers, cancellationToken)
                    .ConfigureAwait(false))
                    .ToList();

                return new AvailableNetworkSessionCollection(lanSessions);
            }

            var infos = await FindSteamLobbiesAsync(sessionType).ConfigureAwait(false);

            var available = infos.Select(info => new AvailableNetworkSession(
                sessionName: info.HostName ?? "Steam Session",
                hostGamertag: info.HostName ?? "Host",
                currentGamerCount: info.CurrentPlayerCount,
                openPublicGamerSlots: Math.Max(0, info.MaxPlayerCount - info.CurrentPlayerCount),
                openPrivateGamerSlots: 0,
                sessionType: sessionType,
                sessionProperties: new Dictionary<string, object>(),
                sessionId: info.SessionId,
                hostEndpoint: TryGetHostEndpointFromLobby(info.SessionId)
            )).ToList();

            return new AvailableNetworkSessionCollection(available);
        }

        /// <summary>
        /// Joins a Steam lobby for presence tracking, then connects via Steam P2P using the host
        /// endpoint stored in the lobby metadata.
        /// </summary>
        public async Task<NetworkSession> JoinSessionAsync(
            AvailableNetworkSession availableSession,
            CancellationToken cancellationToken = default)
        {
            if (IsStrict && !SteamRuntime.IsInitialized)
            {
                throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session join.");
            }

            // Join the Steam lobby (for presence / invite tracking).
            if (SteamRuntime.IsInitialized &&
                ulong.TryParse(availableSession.SessionId, out var lobbyIdRaw))
            {
                try
                {
                    var lobbyId = new CSteamID(lobbyIdRaw);
                    var localSteamId = SteamUser.GetSteamID();
                    var lobbyOwner = SteamMatchmaking.GetLobbyOwner(lobbyId);
                    if (lobbyOwner == localSteamId)
                    {
                        throw new InvalidOperationException(
                            "Cannot join a lobby owned by the local Steam account. " +
                            "Steam P2P requires distinct Steam identities for peers.");
                    }

                    var call = SteamMatchmaking.JoinLobby(lobbyId);
                    await AwaitCallResultAsync<LobbyEnter_t>(call).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (IsStrict)
                    {
                        throw;
                    }

                    Debug.WriteLine($"[Steam] JoinLobby failed (non-fatal): {ex.Message}");
                }
            }
            else if (IsStrict)
            {
                throw new InvalidOperationException("Session id is not a valid Steam lobby id in strict mode.");
            }

            var joined = SteamRuntime.IsInitialized
                ? await NetworkSession.JoinSystemLinkSessionAsync(availableSession, new SteamP2PTransport(), cancellationToken).ConfigureAwait(false)
                : await NetworkSession.JoinSystemLinkSessionAsync(availableSession, cancellationToken).ConfigureAwait(false);

            // Steam vertical slice currently does not support host migration.
            joined.AllowHostMigration = false;

            return joined;
        }

        // -----------------------------------------------------------------------
        // Steam lobby helpers
        // -----------------------------------------------------------------------

        private async Task AdvertiseSteamLobbyAsync(NetworkSession session, int maxGamers)
        {
            try
            {
                var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxGamers);
                var result = await AwaitCallResultAsync<LobbyCreated_t>(call).ConfigureAwait(false);
                if (result.m_eResult != EResult.k_EResultOK)
                {
                    Debug.WriteLine($"[Steam] CreateLobby returned {result.m_eResult}");
                    return;
                }

                var lobbyId = new CSteamID(result.m_ulSteamIDLobby);
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyGameKey, lobbyGameValue);
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyHostSteamIdKey, SteamUser.GetSteamID().m_SteamID.ToString());

                Debug.WriteLine($"[Steam] Lobby created: {lobbyId}");
            }
            catch (Exception ex)
            {
                if (IsStrict)
                {
                    throw;
                }

                Debug.WriteLine($"[Steam] AdvertiseSteamLobbyAsync failed: {ex.Message}");
            }
        }

        private IPEndPoint TryGetHostEndpointFromLobby(string sessionId)
        {
            if (!SteamRuntime.IsInitialized) return null;
            if (!ulong.TryParse(sessionId, out var lobbyIdRaw)) return null;
            try
            {
                var lobbyId = new CSteamID(lobbyIdRaw);
                var lobbyGame = SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameKey);
                if (!string.Equals(lobbyGame, lobbyGameValue, StringComparison.OrdinalIgnoreCase))
                    return null;

                var hostSteamId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                if (hostSteamId.IsValid())
                    return SteamP2PTransport.ToEndpoint(hostSteamId);

                var hostSteamIdText = SteamMatchmaking.GetLobbyData(lobbyId, LobbyHostSteamIdKey);
                if (ulong.TryParse(hostSteamIdText, out var hostSteamIdRaw))
                    return SteamP2PTransport.ToEndpoint(new CSteamID(hostSteamIdRaw));
            }
            catch { }
            return null;
        }


        private async Task<IEnumerable<SessionInfo>> FindSteamLobbiesAsync(NetworkSessionType sessionType)
        {
            try
            {
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
                SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyGameKey, lobbyGameValue, ELobbyComparison.k_ELobbyComparisonEqual);
                var call = SteamMatchmaking.RequestLobbyList();
                var result = await AwaitCallResultAsync<LobbyMatchList_t>(call).ConfigureAwait(false);
                var localSteamId = SteamUser.GetSteamID();

                var lobbyCount = (int)result.m_nLobbiesMatching;
                var sessions = new List<SessionInfo>(lobbyCount);
                for (var i = 0; i < lobbyCount; i++)
                {
                    var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                    var lobbyGame = SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameKey);
                    if (!string.Equals(lobbyGame, lobbyGameValue, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                    if (ownerId == localSteamId)
                    {
                        // Exclude self-owned lobbies to avoid misleading same-account joins.
                        continue;
                    }

                    var ownerName = SteamFriends.GetFriendPersonaName(ownerId);

                    if (string.IsNullOrWhiteSpace(ownerName))
                    {
                        ownerName = ownerId.ToString();
                    }

                    sessions.Add(new SessionInfo
                    {
                        SessionId = lobbyId.ToString(),
                        JoinAddress = lobbyId.ToString(),
                        HostName = ownerName,
                        CurrentPlayerCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId),
                        MaxPlayerCount = SteamMatchmaking.GetLobbyMemberLimit(lobbyId),
                        IsPasswordProtected = false,
                        SessionType = sessionType
                    });
                }

                return sessions;
            }
            catch
            {
                if (IsStrict)
                {
                    throw;
                }

                // Keep vertical-slice behavior available if Steam lobby query fails.
                return SteamSessionDirectory.FindSessions(sessionType).ToList();
            }
        }

        private static async Task<IEnumerable<SessionInfo>> FindSystemLinkSessionsAsync(NetworkSessionType sessionType)
        {
            if (sessionType != NetworkSessionType.SystemLink)
            {
                return Enumerable.Empty<SessionInfo>();
            }

            var lanSessions = await SystemLinkSessionManager.DiscoverSessionsAsync(maxLocalGamers: 1, CancellationToken.None).ConfigureAwait(false);
            return lanSessions.Select(session => new SessionInfo
            {
                SessionId = session.SessionId,
                JoinAddress = session.HostEndpoint?.ToString() ?? string.Empty,
                HostName = session.HostGamertag,
                CurrentPlayerCount = session.CurrentGamerCount,
                MaxPlayerCount = session.CurrentGamerCount + session.OpenPublicGamerSlots + session.OpenPrivateGamerSlots,
                IsPasswordProtected = false,
                SessionType = session.SessionType
            }).ToList();
        }

        private static string NormalizeGameTag(string gameTag)
        {
            if (string.IsNullOrWhiteSpace(gameTag))
                return "default";

            return gameTag.Trim().ToLowerInvariant();
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
    }

    internal static class SteamSessionDirectory
    {
        private sealed class SteamLobby
        {
            public string SessionId;
            public NetworkSessionType SessionType;
            public int MaxGamers;
            public SteamNetworkSession Host;
            public List<SteamNetworkSession> Participants = new List<SteamNetworkSession>();
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SteamLobby> Lobbies = new Dictionary<string, SteamLobby>();
        private static readonly Dictionary<string, SteamNetworkSession> LocalOwnerByGamerId = new Dictionary<string, SteamNetworkSession>();

        public static void RegisterHost(SteamNetworkSession host, string sessionId, NetworkSessionType sessionType, int maxGamers)
        {
            lock (Gate)
            {
                var lobby = new SteamLobby
                {
                    SessionId = sessionId,
                    SessionType = sessionType,
                    MaxGamers = maxGamers,
                    Host = host
                };

                lobby.Participants.Add(host);
                Lobbies[sessionId] = lobby;
                LocalOwnerByGamerId[host.LocalGamer.Id] = host;
            }
        }

        public static IEnumerable<SessionInfo> FindSessions(NetworkSessionType sessionType)
        {
            lock (Gate)
            {
                var result = new List<SessionInfo>();
                foreach (var lobby in Lobbies.Values)
                {
                    if (lobby.SessionType != sessionType)
                    {
                        continue;
                    }

                    var currentPlayers = lobby.Participants.Count;
                    result.Add(new SessionInfo
                    {
                        SessionId = lobby.SessionId,
                        JoinAddress = lobby.SessionId,
                        HostName = lobby.Host.LocalGamer.Gamertag,
                        CurrentPlayerCount = currentPlayers,
                        MaxPlayerCount = lobby.MaxGamers,
                        IsPasswordProtected = false,
                        SessionType = lobby.SessionType
                    });
                }

                return result;
            }
        }

        public static void JoinLobby(string sessionId, SteamNetworkSession joiningSession)
        {
            List<SteamNetworkSession> toPromote;
            lock (Gate)
            {
                if (!Lobbies.TryGetValue(sessionId, out var lobby))
                {
                    throw new InvalidOperationException($"Steam session not found: {sessionId}");
                }

                if (lobby.Participants.Count >= lobby.MaxGamers)
                {
                    throw new InvalidOperationException("Steam session is full.");
                }

                foreach (var participant in lobby.Participants)
                {
                    joiningSession.EnsureRemoteGamer(participant.LocalGamer.Id, participant.LocalGamer.Gamertag, participant.IsHostSession);
                    participant.EnsureRemoteGamer(joiningSession.LocalGamer.Id, joiningSession.LocalGamer.Gamertag, isHost: false);
                    participant.NotifyGamerJoined(joiningSession.LocalGamer.Id);
                }

                lobby.Participants.Add(joiningSession);
                LocalOwnerByGamerId[joiningSession.LocalGamer.Id] = joiningSession;

                // Snapshot under lock; promote outside to avoid deadlock if a GameStarted
                // handler re-enters directory operations (e.g. CloseAsync → RemoveLobby).
                toPromote = new List<SteamNetworkSession>(lobby.Participants);
            }

            foreach (var participant in toPromote)
            {
                participant.PromoteToPlayingIfNeeded();
            }
        }

        public static void SendReliable(string recipientGamerId, byte[] data, string senderGamerId)
        {
            lock (Gate)
            {
                if (!LocalOwnerByGamerId.TryGetValue(recipientGamerId, out var owner))
                {
                    return;
                }

                owner.EnqueueInbound(data, senderGamerId);
            }
        }

        public static void RemoveSession(SteamNetworkSession session)
        {
            lock (Gate)
            {
                if (session?.SessionId == null || !Lobbies.TryGetValue(session.SessionId, out var lobby))
                {
                    return;
                }

                LocalOwnerByGamerId.Remove(session.LocalGamer.Id);

                if (ReferenceEquals(lobby.Host, session))
                {
                    foreach (var participant in lobby.Participants)
                    {
                        if (!ReferenceEquals(participant, session))
                        {
                            participant.ForceSessionEnded(NetworkSessionEndReason.HostEndedSession);
                            participant.RemoveRemoteGamer(session.LocalGamer.Id);
                        }
                    }

                    Lobbies.Remove(lobby.SessionId);
                    return;
                }

                lobby.Participants.Remove(session);

                foreach (var participant in lobby.Participants)
                {
                    participant.NotifyGamerLeft(session.LocalGamer.Id);
                    participant.RemoveRemoteGamer(session.LocalGamer.Id);
                }
            }
        }
    }
}
