using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// The Steam back-end: sessions are advertised as Steam lobbies, and gameplay travels over Steam P2P
    /// (<see cref="SteamP2PTransport"/>). Everything above that is <see cref="NetworkSession"/>.
    /// </summary>
    /// <remarks>
    /// When Steam is not running, <see cref="SteamFallbackMode.PreferFallback"/> (the default) uses the
    /// LAN back-end instead, so a game still works in development; <see cref="SteamFallbackMode.Strict"/>
    /// fails instead.
    /// </remarks>
    public sealed class SteamBackend : INetworkBackend
    {
        private const string LobbyGameKey = "mgnet_game";
        private const string LobbyHostSteamIdKey = "host_steamid";

        private readonly string lobbyGameValue;
        private readonly SteamFallbackMode fallbackMode;
        private readonly UdpBackend lan = new UdpBackend();

        public SteamBackend(string gameTag = null, SteamFallbackMode fallbackMode = SteamFallbackMode.PreferFallback)
        {
            lobbyGameValue = NormalizeGameTag(gameTag);
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == SteamFallbackMode.Strict;

        public string Name => "Steam";

        public IPeerTransport CreateTransport()
        {
            if (SteamRuntime.IsInitialized)
                return new SteamP2PTransport();

            ThrowIfStrict("session creation or join");
            return lan.CreateTransport();
        }

        public async Task AdvertiseAsync(ISessionAdvertiser session, CancellationToken stopToken)
        {
            if (!SteamRuntime.IsInitialized)
            {
                ThrowIfStrict("session creation");
                await lan.AdvertiseAsync(session, stopToken).ConfigureAwait(false);
                return;
            }

            if (IsStrict)
                await AdvertiseLobbyAsync(session.GetAdvertisement().MaxGamers).ConfigureAwait(false);
            else
                _ = AdvertiseLobbyAsync(session.GetAdvertisement().MaxGamers);
        }

        public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (!SteamRuntime.IsInitialized)
            {
                ThrowIfStrict("session discovery");
                return await lan.FindAsync(sessionType, maxLocalGamers, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
                SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyGameKey, lobbyGameValue, ELobbyComparison.k_ELobbyComparisonEqual);
                var result = await AwaitCallResultAsync<LobbyMatchList_t>(SteamMatchmaking.RequestLobbyList()).ConfigureAwait(false);
                var localSteamId = SteamUser.GetSteamID();

                var found = new List<AvailableNetworkSession>();
                for (var i = 0; i < (int)result.m_nLobbiesMatching; i++)
                {
                    var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                    var lobbyGame = SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameKey);
                    if (!string.Equals(lobbyGame, lobbyGameValue, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                    if (ownerId == localSteamId)
                        continue;   // our own lobby: a same-account join cannot work

                    var ownerName = SteamFriends.GetFriendPersonaName(ownerId);
                    if (string.IsNullOrWhiteSpace(ownerName))
                        ownerName = ownerId.ToString();

                    int current = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                    int max = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
                    found.Add(new AvailableNetworkSession(
                        sessionName: ownerName,
                        hostGamertag: ownerName,
                        currentGamerCount: current,
                        openPublicGamerSlots: Math.Max(0, max - current),
                        openPrivateGamerSlots: 0,
                        sessionType: sessionType,
                        sessionProperties: new Dictionary<string, object>(),
                        sessionId: lobbyId.ToString(),
                        hostPeer: HostPeerOf(lobbyId)));
                }

                return found;
            }
            catch (Exception ex) when (!IsStrict)
            {
                Debug.WriteLine($"[Steam] Lobby search failed, no sessions found: {ex.Message}");
                return new List<AvailableNetworkSession>();
            }
        }

        public async Task PrepareJoinAsync(AvailableNetworkSession session, CancellationToken cancellationToken)
        {
            if (IsStrict && !SteamRuntime.IsInitialized)
                throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session join.");

            if (!SteamRuntime.IsInitialized)
                return;

            if (!ulong.TryParse(session.SessionId, out var lobbyIdRaw))
            {
                if (IsStrict)
                    throw new InvalidOperationException("Session id is not a valid Steam lobby id in strict mode.");
                return;
            }

            // Join the Steam lobby (for presence and invites).
            try
            {
                var lobbyId = new CSteamID(lobbyIdRaw);
                if (SteamMatchmaking.GetLobbyOwner(lobbyId) == SteamUser.GetSteamID())
                {
                    throw new InvalidOperationException(
                        "Cannot join a lobby owned by the local Steam account. " +
                        "Steam P2P requires distinct Steam identities for peers.");
                }

                await AwaitCallResultAsync<LobbyEnter_t>(SteamMatchmaking.JoinLobby(lobbyId)).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsStrict)
            {
                Debug.WriteLine($"[Steam] JoinLobby failed (non-fatal): {ex.Message}");
            }
        }

        private async Task AdvertiseLobbyAsync(int maxGamers)
        {
            try
            {
                var result = await AwaitCallResultAsync<LobbyCreated_t>(
                    SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxGamers)).ConfigureAwait(false);
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
            catch (Exception ex) when (!IsStrict)
            {
                Debug.WriteLine($"[Steam] Advertising the lobby failed: {ex.Message}");
            }
        }

        private static PeerId HostPeerOf(CSteamID lobbyId)
        {
            try
            {
                var host = SteamMatchmaking.GetLobbyOwner(lobbyId);
                if (host.IsValid())
                    return SteamP2PTransport.ToPeer(host);

                if (ulong.TryParse(SteamMatchmaking.GetLobbyData(lobbyId, LobbyHostSteamIdKey), out var raw))
                    return SteamP2PTransport.ToPeer(new CSteamID(raw));
            }
            catch
            {
                // no peer: the session cannot be joined
            }
            return PeerId.None;
        }

        private void ThrowIfStrict(string what)
        {
            if (IsStrict)
                throw new InvalidOperationException($"Steam runtime is not initialized for strict Steam {what}.");
        }

        private static string NormalizeGameTag(string gameTag) =>
            string.IsNullOrWhiteSpace(gameTag) ? "default" : gameTag.Trim().ToLowerInvariant();

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
}
