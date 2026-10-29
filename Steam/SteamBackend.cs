using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Steam
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
        private const string LobbyTypeKey = "mgnet_type";
        // present (as "1") on a lobby whose host wants a password; the password itself is never published
        private const string LobbyPasswordKey = "mgnet_pw";
        private const string LobbyPropertyPrefix = "p.";

        private readonly string lobbyGameValue;
        private readonly SteamFallbackMode fallbackMode;
        private readonly UdpBackend lan = new UdpBackend();

        /// <summary>
        /// Initializes a new instance of the <see cref="SteamBackend"/> class.
        /// </summary>
        /// <param name="gameTag">
        /// Identifies this game among the lobbies Steam returns, so only your game's sessions are found and your lobbies are not shown to other games. Null or blank selects a default tag.
        /// </param>
        /// <param name="fallbackMode">What to do when Steam is not initialized: use the LAN back-end (default) or throw.</param>
        public SteamBackend(string gameTag = null, SteamFallbackMode fallbackMode = SteamFallbackMode.PreferFallback)
        {
            lobbyGameValue = NormalizeGameTag(gameTag);
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == SteamFallbackMode.Strict;

        /// <inheritdoc/>
        public string Name => "Steam";

        // lobbies carry every type; without Steam running this is the LAN, which carries SystemLink only
        /// <summary>
        /// Whether sessions of this type can be created or found right now.
        /// </summary>
        /// <remarks>
        /// <see cref="NetworkSessionType.SystemLink"/> is always supported (it uses the LAN when Steam is not running); every other type needs <see cref="SteamRuntime.IsInitialized"/>.
        /// </remarks>
        public bool SupportsSessionType(NetworkSessionType sessionType) =>
            sessionType == NetworkSessionType.SystemLink || SteamRuntime.IsInitialized;

        private EventHandler<InviteReceivedEventArgs> inviteReceived;
        private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private bool launchInviteChecked;

        /// <summary>
        /// Raised when the user accepts a Steam invitation or "Join Game" (a friend's invite, the friends
        /// list, or the game being started with <c>+connect_lobby</c>). Needs <c>SteamRuntime.RunCallbacks()</c>
        /// called regularly, as Steam does for every callback.
        /// </summary>
        public event EventHandler<InviteReceivedEventArgs> InviteReceived
        {
            add
            {
                inviteReceived += value;
                EnsureInviteListening();
            }
            remove => inviteReceived -= value;
        }

        private void EnsureInviteListening()
        {
            if (!SteamRuntime.IsInitialized)
                return;

            lobbyJoinRequested ??= Callback<GameLobbyJoinRequested_t>.Create(request => _ = RaiseInviteAsync(request.m_steamIDLobby));

            // started from Steam's "Join Game" with the lobby on the command line
            if (!launchInviteChecked)
            {
                launchInviteChecked = true;
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "+connect_lobby");
                if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out var lobbyRaw))
                    _ = RaiseInviteAsync(new CSteamID(lobbyRaw));
            }
        }

        // an invitation names a lobby: join it so Steam gives us its data, then describe it as a session
        internal async Task RaiseInviteAsync(CSteamID lobbyId)
        {
            try
            {
                await AwaitCallResultAsync<LobbyEnter_t>(SteamMatchmaking.JoinLobby(lobbyId)).ConfigureAwait(false);
                var session = DescribeLobby(lobbyId, NetworkSessionType.PlayerMatch);
                if (session != null)
                    inviteReceived?.Invoke(this, new InviteReceivedEventArgs(session));
            }
            catch (Exception ex)
            {
                NetworkLogging.Logger.LogWarning($"[Steam] Could not follow the invitation to lobby {lobbyId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a <see cref="SteamP2PTransport"/>, or a LAN transport when Steam is not initialized.
        /// </summary>
        /// <exception cref="InvalidOperationException">Steam is not initialized and the mode is <see cref="SteamFallbackMode.Strict"/>.</exception>
        public IPeerTransport CreateTransport()
        {
            if (SteamRuntime.IsInitialized)
                return new SteamP2PTransport();

            ThrowIfStrict("session creation or join");
            return lan.CreateTransport();
        }

        /// <summary>
        /// Publishes the session as a Steam lobby, or on the LAN when Steam is not initialized.
        /// </summary>
        /// <remarks>
        /// In <see cref="SteamFallbackMode.Strict"/> the lobby is created before this completes and failures are thrown, including a lobby Steam refuses to create; otherwise it is created in the background and a failure is logged as a warning, not reported to the caller, so the session is simply not listed on Steam.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Steam is not initialized and the mode is <see cref="SteamFallbackMode.Strict"/>.</exception>
        public async Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            if (!SteamRuntime.IsInitialized)
            {
                ThrowIfStrict("session creation");
                await lan.AdvertiseAsync(session, transport, stopToken).ConfigureAwait(false);
                return;
            }

            if (IsStrict)
                await AdvertiseLobbyAsync(session, stopToken).ConfigureAwait(false);
            else
                _ = AdvertiseLobbyAsync(session, stopToken);
        }

        /// <summary>
        /// Searches Steam lobbies (at most 50) for this game's sessions of the given type.
        /// </summary>
        /// <remarks>
        /// Lobbies owned by the local Steam account are left out. If the search fails and the mode is not <see cref="SteamFallbackMode.Strict"/>, the failure is logged and the result is empty. When Steam is not initialized the LAN is searched instead.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Steam is not initialized and the mode is <see cref="SteamFallbackMode.Strict"/>.</exception>
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
                SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyTypeKey, sessionType.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
                var result = await AwaitCallResultAsync<LobbyMatchList_t>(SteamMatchmaking.RequestLobbyList()).ConfigureAwait(false);
                var localSteamId = SteamUser.GetSteamID();

                var found = new List<AvailableNetworkSession>();
                for (var i = 0; i < (int)result.m_nLobbiesMatching; i++)
                {
                    var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                    if (SteamMatchmaking.GetLobbyOwner(lobbyId) == localSteamId)
                        continue;   // our own lobby: a same-account join cannot work

                    var described = DescribeLobby(lobbyId, sessionType);
                    if (described != null)
                        found.Add(described);
                }

                return found;
            }
            catch (Exception ex) when (!IsStrict)
            {
                NetworkLogging.Logger.LogWarning($"[Steam] Lobby search failed, no sessions found: {ex.Message}");
                return new List<AvailableNetworkSession>();
            }
        }

        /// <summary>
        /// Joins the Steam lobby named by the session's id and returns a handle that leaves it when disposed.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="null"/> when Steam is not initialized, or when the session id is not a lobby id, outside <see cref="SteamFallbackMode.Strict"/>; a failed join is likewise logged and returns <see langword="null"/>. Joining a lobby owned by the local Steam account is refused, because Steam P2P needs distinct identities.
        /// </remarks>
        /// <exception cref="InvalidOperationException">In <see cref="SteamFallbackMode.Strict"/>: Steam is not initialized, the id is not a lobby id, or the join fails.</exception>
        public async Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken)
        {
            if (IsStrict && !SteamRuntime.IsInitialized)
                throw new InvalidOperationException("Steam runtime is not initialized for strict Steam session join.");

            if (!SteamRuntime.IsInitialized)
                return null;

            if (!ulong.TryParse(session.SessionId, out var lobbyIdRaw))
            {
                if (IsStrict)
                    throw new InvalidOperationException("Session id is not a valid Steam lobby id in strict mode.");
                return null;
            }

            // Join the Steam lobby (for presence and invites); leave it again when the session ends.
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
                return new LobbyMembership(lobbyId);
            }
            catch (Exception ex) when (!IsStrict)
            {
                NetworkLogging.Logger.LogError($"[Steam] JoinLobby failed (non-fatal): {ex.Message}");
                return null;
            }
        }

        private sealed class LobbyMembership : IDisposable
        {
            private readonly CSteamID lobbyId;
            public LobbyMembership(CSteamID lobbyId) => this.lobbyId = lobbyId;
            public void Dispose() => SteamMatchmaking.LeaveLobby(lobbyId);
        }

        // describes a Steam lobby as a joinable session, or null if it is not one of this game's
        private AvailableNetworkSession DescribeLobby(CSteamID lobbyId, NetworkSessionType fallbackType)
        {
            if (!string.Equals(SteamMatchmaking.GetLobbyData(lobbyId, LobbyGameKey), lobbyGameValue, StringComparison.OrdinalIgnoreCase))
                return null;

            var ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
            var ownerName = SteamFriends.GetFriendPersonaName(ownerId);
            if (string.IsNullOrWhiteSpace(ownerName))
                ownerName = ownerId.ToString();

            var type = Enum.TryParse(SteamMatchmaking.GetLobbyData(lobbyId, LobbyTypeKey), out NetworkSessionType parsed) ? parsed : fallbackType;
            bool needsPassword = SteamMatchmaking.GetLobbyData(lobbyId, LobbyPasswordKey) == "1";

            // the properties the host set: every lobby entry under the property prefix
            var properties = new Dictionary<string, object>();
            int entries = SteamMatchmaking.GetLobbyDataCount(lobbyId);
            for (int i = 0; i < entries; i++)
            {
                if (SteamMatchmaking.GetLobbyDataByIndex(lobbyId, i, out string key, 256, out string value, 1024)
                    && key.StartsWith(LobbyPropertyPrefix, StringComparison.Ordinal)
                    && SessionPropertyEncoding.TryDecode(value, out object decoded))
                {
                    properties[key.Substring(LobbyPropertyPrefix.Length)] = decoded;
                }
            }

            int current = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int max = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
            return new AvailableNetworkSession(
                sessionName: ownerName,
                hostGamertag: ownerName,
                currentGamerCount: current,
                openPublicGamerSlots: Math.Max(0, max - current),
                openPrivateGamerSlots: 0,
                sessionType: type,
                sessionProperties: properties,
                sessionId: lobbyId.ToString(),
                hostPeer: HostPeerOf(lobbyId),
                isPasswordProtected: needsPassword);
        }

        private async Task AdvertiseLobbyAsync(ISessionAdvertiser session, CancellationToken stopToken)
        {
            try
            {
                var ad = session.GetAdvertisement();
                var result = await AwaitCallResultAsync<LobbyCreated_t>(
                    SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, ad.MaxGamers)).ConfigureAwait(false);
                if (result.m_eResult != EResult.k_EResultOK)
                {
                    if (IsStrict)
                        throw new InvalidOperationException($"Steam could not create the lobby: {result.m_eResult}.");

                    NetworkLogging.Logger.LogWarning($"[Steam] CreateLobby returned {result.m_eResult}; the session is not listed on Steam");
                    return;
                }

                var lobbyId = new CSteamID(result.m_ulSteamIDLobby);
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyGameKey, lobbyGameValue);
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyHostSteamIdKey, SteamUser.GetSteamID().m_SteamID.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyTypeKey, ad.SessionType.ToString());
                PublishPasswordFlag(lobbyId, ad.IsPasswordProtected);
                var published = PublishProperties(lobbyId, ad.Properties, new HashSet<string>());
                NetworkLogging.Logger.LogInfo($"[Steam] Lobby created: {lobbyId}");

                // the host can change the session's properties later; keep the lobby in step until the session ends
                _ = Task.Run(async () =>
                {
                    while (!stopToken.IsCancellationRequested)
                    {
                        try { await Task.Delay(1000, stopToken).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                        var latest = session.GetAdvertisement();
                        PublishPasswordFlag(lobbyId, latest.IsPasswordProtected);
                        published = PublishProperties(lobbyId, latest.Properties, published);
                    }
                    SteamMatchmaking.LeaveLobby(lobbyId);
                });
            }
            catch (Exception ex) when (!IsStrict)
            {
                NetworkLogging.Logger.LogWarning($"[Steam] Advertising the lobby failed: {ex.Message}");
            }
        }

        private static void PublishPasswordFlag(CSteamID lobbyId, bool passwordProtected)
        {
            string wanted = passwordProtected ? "1" : "";
            if (SteamMatchmaking.GetLobbyData(lobbyId, LobbyPasswordKey) == wanted)
                return;
            if (passwordProtected)
                SteamMatchmaking.SetLobbyData(lobbyId, LobbyPasswordKey, wanted);
            else
                SteamMatchmaking.DeleteLobbyData(lobbyId, LobbyPasswordKey);
        }

        private HashSet<string> PublishProperties(CSteamID lobbyId, IReadOnlyDictionary<string, object> properties, HashSet<string> previouslyPublished)
        {
            var now = new HashSet<string>();
            foreach (var property in properties)
            {
                string key = LobbyPropertyPrefix + property.Key;
                string encoded = SessionPropertyEncoding.Encode(property.Value);
                if (SteamMatchmaking.GetLobbyData(lobbyId, key) != encoded)
                    SteamMatchmaking.SetLobbyData(lobbyId, key, encoded);
                now.Add(key);
            }

            // a property the host removed disappears from the lobby too (Steam deletes a key set to empty)
            foreach (string gone in previouslyPublished.Where(k => !now.Contains(k)))
                SteamMatchmaking.DeleteLobbyData(lobbyId, gone);
            return now;
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
