using Epic.OnlineServices;
using Lobby = Epic.OnlineServices.Lobby;
using Platform = Epic.OnlineServices.Platform;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// The Epic Online Services back-end: sessions are advertised as EOS lobbies and gameplay travels
    /// over EOS P2P (<see cref="EOSP2PTransport"/>). Everything above that is <see cref="NetworkSession"/>.
    /// </summary>
    /// <remarks>
    /// When EOS is not initialised or nobody is signed in, <see cref="EOSFallbackMode.PreferFallback"/>
    /// (the default) uses the LAN back-end instead; <see cref="EOSFallbackMode.Strict"/> fails instead.
    /// </remarks>
    public sealed class EOSBackend : INetworkBackend
    {
        private const string BucketId = "MonoGame.Xna.Framework.Net";

        private readonly UdpBackend lan = new UdpBackend();
        private readonly EOSFallbackMode fallbackMode;
        private readonly object lobbyGate = new();
        private readonly HashSet<string> activeLobbies = new(StringComparer.Ordinal);

        public EOSBackend(EOSFallbackMode fallbackMode = EOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        public string Name => "EOS";

        private bool IsStrict => fallbackMode == EOSFallbackMode.Strict;

        // EOS can only be used once the SDK is up and a user is signed in
        private static bool Ready =>
            EOSRuntime.IsInitialized && EOSRuntime.TryGetPlatform() != null && EOSRuntime.TryGetCurrentProductUserId() != null;

        private bool UseEos(string what)
        {
            if (Ready)
                return true;
            if (IsStrict)
                throw new InvalidOperationException($"EOS runtime is not initialized for strict {what}.");
            return false;
        }

        public IPeerTransport CreateTransport()
        {
            return UseEos("session creation or join")
                ? new EOSP2PTransport(IsInActiveLobby)
                : lan.CreateTransport();
        }

        public async Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            if (!UseEos("session creation"))
            {
                await lan.AdvertiseAsync(session, transport, stopToken).ConfigureAwait(false);
                return;
            }

            var platform = EOSRuntime.TryGetPlatform();
            var localUserId = EOSRuntime.TryGetCurrentProductUserId();
            var lobbyInterface = platform.GetLobbyInterface();
            int maxGamers = session.GetAdvertisement().MaxGamers;

            var createOptions = new Lobby.CreateLobbyOptions
            {
                LocalUserId = localUserId,
                MaxLobbyMembers = (uint)Math.Max(2, maxGamers),
                PermissionLevel = Lobby.LobbyPermissionLevel.Publicadvertised,
                BucketId = BucketId,
                DisableHostMigration = true,
                EnableJoinById = true,
                AllowInvites = false
            };

            var created = new TaskCompletionSource<Lobby.CreateLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            lobbyInterface.CreateLobby(ref createOptions, null, (ref Lobby.CreateLobbyCallbackInfo info) => created.TrySetResult(info));
            var createResult = await EOSClient.WaitForCallbackAsync(created, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.CreateLobby").ConfigureAwait(false);
            if (createResult.ResultCode != Result.Success)
                throw new InvalidOperationException($"EOS CreateLobby failed: {createResult.ResultCode}");

            string lobbyId = createResult.LobbyId;
            lock (lobbyGate) activeLobbies.Add(lobbyId);
            await SetBucketAttributeAsync(lobbyInterface, localUserId, lobbyId).ConfigureAwait(false);

            // the session ending (its token is cancelled) closes the lobby
            stopToken.Register(() => _ = DestroyLobbyAsync(lobbyInterface, localUserId, lobbyId));
        }

        public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (!UseEos("session discovery"))
                return await lan.FindAsync(sessionType, maxLocalGamers, cancellationToken).ConfigureAwait(false);

            var platform = EOSRuntime.TryGetPlatform();
            var localUserId = EOSRuntime.TryGetCurrentProductUserId();
            var lobbyInterface = platform.GetLobbyInterface();
            var found = new List<AvailableNetworkSession>();

            var searchOptions = new Lobby.CreateLobbySearchOptions { MaxResults = 50 };
            if (lobbyInterface.CreateLobbySearch(ref searchOptions, out var search) != Result.Success || search == null)
                return found;

            try
            {
                var parameter = new Lobby.LobbySearchSetParameterOptions
                {
                    Parameter = new Lobby.AttributeData { Key = "bucket", Value = new Lobby.AttributeDataValue { AsUtf8 = BucketId } },
                    ComparisonOp = ComparisonOp.Equal
                };
                search.SetParameter(ref parameter);

                var findOptions = new Lobby.LobbySearchFindOptions { LocalUserId = localUserId };
                var finished = new TaskCompletionSource<Lobby.LobbySearchFindCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                search.Find(ref findOptions, null, (ref Lobby.LobbySearchFindCallbackInfo info) => finished.TrySetResult(info));
                var findResult = await EOSClient.WaitForCallbackAsync(finished, EOSClient.GetCallbackTimeout(), cancellationToken, "LobbySearch.Find").ConfigureAwait(false);
                if (findResult.ResultCode != Result.Success)
                    return found;

                var countOptions = new Lobby.LobbySearchGetSearchResultCountOptions();
                int count = (int)search.GetSearchResultCount(ref countOptions);
                for (int i = 0; i < count; i++)
                {
                    var copyOptions = new Lobby.LobbySearchCopySearchResultByIndexOptions { LobbyIndex = (uint)i };
                    if (search.CopySearchResultByIndex(ref copyOptions, out var details) != Result.Success || details == null)
                        continue;

                    try
                    {
                        var infoOptions = new Lobby.LobbyDetailsCopyInfoOptions();
                        if (details.CopyInfo(ref infoOptions, out var lobbyInfo) != Result.Success || !lobbyInfo.HasValue)
                            continue;

                        var info = lobbyInfo.Value;
                        if (info.LobbyOwnerUserId == null || info.LobbyOwnerUserId.ToString() == localUserId.ToString())
                            continue;   // our own lobby

                        int max = (int)info.MaxMembers;
                        int current = max - (int)info.AvailableSlots;
                        found.Add(new AvailableNetworkSession(
                            sessionName: info.LobbyOwnerUserId.ToString(),
                            hostGamertag: info.LobbyOwnerUserId.ToString(),
                            currentGamerCount: current,
                            openPublicGamerSlots: (int)info.AvailableSlots,
                            openPrivateGamerSlots: 0,
                            sessionType: sessionType,
                            sessionProperties: new Dictionary<string, object>(),
                            sessionId: info.LobbyId,
                            hostPeer: EOSP2PTransport.ToPeer(info.LobbyOwnerUserId)));
                    }
                    finally
                    {
                        details.Release();
                    }
                }
            }
            finally
            {
                search.Release();
            }

            return found;
        }

        public async Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!UseEos("session join"))
                return await lan.PrepareJoinAsync(session, transport, cancellationToken).ConfigureAwait(false);

            var platform = EOSRuntime.TryGetPlatform();
            var localUserId = EOSRuntime.TryGetCurrentProductUserId();
            var lobbyInterface = platform.GetLobbyInterface();

            var joinOptions = new Lobby.JoinLobbyByIdOptions { LobbyId = session.SessionId, LocalUserId = localUserId };
            var joined = new TaskCompletionSource<Lobby.JoinLobbyByIdCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            lobbyInterface.JoinLobbyById(ref joinOptions, null, (ref Lobby.JoinLobbyByIdCallbackInfo info) => joined.TrySetResult(info));
            var joinResult = await EOSClient.WaitForCallbackAsync(joined, EOSClient.GetCallbackTimeout(), cancellationToken, "Lobby.JoinLobbyById").ConfigureAwait(false);
            if (joinResult.ResultCode != Result.Success)
                throw new InvalidOperationException($"EOS JoinLobbyById failed: {joinResult.ResultCode}");

            lock (lobbyGate) activeLobbies.Add(session.SessionId);
            return new LobbyMembership(this, lobbyInterface, localUserId, session.SessionId);
        }

        // --- lobbies ---

        // Strangers may open P2P connections to us; only those in a lobby we are in are let through
        private bool IsInActiveLobby(ProductUserId user)
        {
            if (user == null)
                return false;

            string[] lobbies;
            lock (lobbyGate) lobbies = activeLobbies.ToArray();
            if (lobbies.Length == 0)
                return false;

            var lobbyInterface = EOSRuntime.TryGetPlatform()?.GetLobbyInterface();
            var localUserId = EOSRuntime.TryGetCurrentProductUserId();
            if (lobbyInterface == null || localUserId == null)
                return false;

            foreach (string lobbyId in lobbies)
            {
                var detailsOptions = new Lobby.CopyLobbyDetailsHandleOptions { LobbyId = lobbyId, LocalUserId = localUserId };
                if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) != Result.Success || details == null)
                    continue;

                try
                {
                    var countOptions = new Lobby.LobbyDetailsGetMemberCountOptions();
                    uint count = details.GetMemberCount(ref countOptions);
                    for (uint i = 0; i < count; i++)
                    {
                        var memberOptions = new Lobby.LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                        if (details.GetMemberByIndex(ref memberOptions)?.ToString() == user.ToString())
                            return true;
                    }
                }
                finally
                {
                    details.Release();
                }
            }

            return false;
        }

        private async Task DestroyLobbyAsync(Lobby.LobbyInterface lobbyInterface, ProductUserId localUserId, string lobbyId)
        {
            lock (lobbyGate) activeLobbies.Remove(lobbyId);
            try
            {
                var options = new Lobby.DestroyLobbyOptions { LocalUserId = localUserId, LobbyId = lobbyId };
                var done = new TaskCompletionSource<Lobby.DestroyLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                lobbyInterface.DestroyLobby(ref options, null, (ref Lobby.DestroyLobbyCallbackInfo info) => done.TrySetResult(info));
                await EOSClient.WaitForCallbackAsync(done, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.DestroyLobby").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EOS] Destroying the lobby failed: {ex.Message}");
            }
        }

        private async Task LeaveLobbyAsync(Lobby.LobbyInterface lobbyInterface, ProductUserId localUserId, string lobbyId)
        {
            lock (lobbyGate) activeLobbies.Remove(lobbyId);
            try
            {
                var options = new Lobby.LeaveLobbyOptions { LocalUserId = localUserId, LobbyId = lobbyId };
                var done = new TaskCompletionSource<Lobby.LeaveLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                lobbyInterface.LeaveLobby(ref options, null, (ref Lobby.LeaveLobbyCallbackInfo info) => done.TrySetResult(info));
                await EOSClient.WaitForCallbackAsync(done, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.LeaveLobby").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EOS] Leaving the lobby failed: {ex.Message}");
            }
        }

        private static async Task SetBucketAttributeAsync(Lobby.LobbyInterface lobbyInterface, ProductUserId localUserId, string lobbyId)
        {
            var modification = new Lobby.UpdateLobbyModificationOptions { LobbyId = lobbyId, LocalUserId = localUserId };
            if (lobbyInterface.UpdateLobbyModification(ref modification, out var handle) != Result.Success || handle == null)
            {
                Debug.WriteLine("[EOS] UpdateLobbyModification failed: the bucket attribute is not set, so searches will not find this lobby.");
                return;
            }

            try
            {
                var attribute = new Lobby.LobbyModificationAddAttributeOptions
                {
                    Attribute = new Lobby.AttributeData { Key = "bucket", Value = new Lobby.AttributeDataValue { AsUtf8 = BucketId } },
                    Visibility = Lobby.LobbyAttributeVisibility.Public
                };
                handle.AddAttribute(ref attribute);

                var update = new Lobby.UpdateLobbyOptions { LobbyModificationHandle = handle };
                var done = new TaskCompletionSource<Lobby.UpdateLobbyCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                lobbyInterface.UpdateLobby(ref update, null, (ref Lobby.UpdateLobbyCallbackInfo info) => done.TrySetResult(info));
                var result = await EOSClient.WaitForCallbackAsync(done, EOSClient.GetCallbackTimeout(), CancellationToken.None, "Lobby.UpdateLobby").ConfigureAwait(false);
                if (result.ResultCode != Result.Success)
                    Debug.WriteLine($"[EOS] Lobby.UpdateLobby (bucket) failed: {result.ResultCode}");
            }
            finally
            {
                handle.Release();
            }
        }

        private sealed class LobbyMembership : IDisposable
        {
            private readonly EOSBackend owner;
            private readonly Lobby.LobbyInterface lobbyInterface;
            private readonly ProductUserId localUserId;
            private readonly string lobbyId;

            public LobbyMembership(EOSBackend owner, Lobby.LobbyInterface lobbyInterface, ProductUserId localUserId, string lobbyId)
            {
                this.owner = owner;
                this.lobbyInterface = lobbyInterface;
                this.localUserId = localUserId;
                this.lobbyId = lobbyId;
            }

            public void Dispose() => _ = owner.LeaveLobbyAsync(lobbyInterface, localUserId, lobbyId);
        }
    }
}
