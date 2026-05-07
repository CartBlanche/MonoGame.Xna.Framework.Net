using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    /// Session discovery uses Steam lobbies. The actual game data transport remains UDP so that
    /// existing packet-routing code continues to work unchanged (Phase 2a). Steam P2P transport
    /// can be layered in as a separate phase.
    /// </summary>
    public sealed class SteamNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private const string LobbyGameKey = "mgnet_game";
        private const string LobbyHostIpKey = "host_ip";
        private const string LobbyHostPortKey = "host_port";
        private readonly string lobbyGameValue;

        public SteamNetworkSessionFactory(string gameTag = null)
        {
            lobbyGameValue = NormalizeGameTag(gameTag);
        }

        public string BackendName => "Steam";

        public INetworkSession CreateSession()
        {
            return new SteamNetworkSession();
        }

        public Task<IEnumerable<SessionInfo>> FindSessionsAsync(NetworkSessionType sessionType)
        {
            if (!SteamRuntime.IsInitialized)
            {
                return Task.FromResult(SteamSessionDirectory.FindSessions(sessionType));
            }

            return FindSteamLobbiesAsync(sessionType);
        }

        // -----------------------------------------------------------------------
        // INetworkSessionProvider — NetworkSession.CreateAsync/FindAsync/JoinAsync
        // delegate here when this factory is registered with NetworkServiceProvider.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a UDP-backed <see cref="NetworkSession"/> that is also advertised as a
        /// Steam lobby so that remote players can discover it via Steam matchmaking.
        /// </summary>
        public async Task<NetworkSession> CreateSessionAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            int maxGamers,
            int privateGamerSlots,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            // Build the standard UDP session (handles gamer tracking + packet routing).
            var session = await NetworkSession.CreateSystemLinkSessionAsync(sessionType, maxGamers, privateGamerSlots, cancellationToken);

            // Advertise concurrently via Steam lobby so remote players can find us.
            if (SteamRuntime.IsInitialized)
                _ = AdvertiseSteamLobbyAsync(session, maxGamers);

            return session;
        }

        /// <summary>
        /// Discovers sessions via Steam lobbies and converts them to
        /// <see cref="AvailableNetworkSession"/> objects that carry the host's UDP endpoint
        /// (stored as Steam lobby metadata), so the existing UDP join path works transparently.
        /// </summary>
        public async Task<AvailableNetworkSessionCollection> FindSessionsAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<SessionInfo> infos;
            if (SteamRuntime.IsInitialized)
                infos = await FindSteamLobbiesAsync(sessionType).ConfigureAwait(false);
            else
                infos = SteamSessionDirectory.FindSessions(sessionType);

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
        /// Joins a Steam lobby for presence tracking, then connects via UDP using the host
        /// endpoint stored in the lobby metadata.
        /// </summary>
        public async Task<NetworkSession> JoinSessionAsync(
            AvailableNetworkSession availableSession,
            CancellationToken cancellationToken = default)
        {
            // Join the Steam lobby (for presence / invite tracking).
            if (SteamRuntime.IsInitialized &&
                ulong.TryParse(availableSession.SessionId, out var lobbyIdRaw))
            {
                try
                {
                    var lobbyId = new CSteamID(lobbyIdRaw);
                    var call = SteamMatchmaking.JoinLobby(lobbyId);
                    await AwaitCallResultAsync<LobbyEnter_t>(call).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Steam] JoinLobby failed (non-fatal): {ex.Message}");
                }
            }

            // Connect via UDP (uses HostEndpoint populated from lobby metadata).
            return await NetworkSession.JoinSystemLinkSessionAsync(availableSession, cancellationToken).ConfigureAwait(false);
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

                // Store the UDP endpoint so joiners can connect directly.
                var localEndpoint = session.NetworkTransport?.LocalEndPoint;
                if (localEndpoint != null)
                {
                    var hostIp = ResolveAdvertisedHostIp(localEndpoint);
                    if (hostIp != null)
                    {
                        SteamMatchmaking.SetLobbyData(lobbyId, LobbyHostIpKey, hostIp.ToString());
                        SteamMatchmaking.SetLobbyData(lobbyId, LobbyHostPortKey, localEndpoint.Port.ToString());
                        Debug.WriteLine($"[Steam] Advertising UDP endpoint: {hostIp}:{localEndpoint.Port}");
                    }
                }

                Debug.WriteLine($"[Steam] Lobby created: {lobbyId}");
            }
            catch (Exception ex)
            {
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

                var ip = SteamMatchmaking.GetLobbyData(lobbyId, LobbyHostIpKey);
                var portStr = SteamMatchmaking.GetLobbyData(lobbyId, LobbyHostPortKey);
                if (!string.IsNullOrEmpty(ip) && int.TryParse(portStr, out var port))
                    return new IPEndPoint(IPAddress.Parse(ip), port);
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
                // Keep vertical-slice behavior available if Steam lobby query fails.
                return SteamSessionDirectory.FindSessions(sessionType).ToList();
            }
        }

        private static IPAddress ResolveAdvertisedHostIp(IPEndPoint localEndpoint)
        {
            if (localEndpoint == null)
                return null;

            var ip = localEndpoint.Address;
            if (ip != null && !IPAddress.Any.Equals(ip) && !IPAddress.IPv6Any.Equals(ip) && !IPAddress.IsLoopback(ip))
                return ip;

            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());
                var candidate = addresses.FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(a));

                return candidate;
            }
            catch
            {
                return null;
            }
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
                    participant.RemoveRemoteGamer(session.LocalGamer.Id);
                    participant.NotifyGamerLeft(session.LocalGamer.Id);
                }
            }
        }
    }
}
