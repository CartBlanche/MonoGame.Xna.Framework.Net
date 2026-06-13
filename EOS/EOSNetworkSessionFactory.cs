using Epic.OnlineServices;
using Lobby = Epic.OnlineServices.Lobby;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Factory and provider for EOS-backed sessions.
    /// The initial slice reuses SystemLink networking while preserving backend routing seams.
    /// </summary>
    public sealed class EOSNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private readonly EOSFallbackMode fallbackMode;

        public EOSNetworkSessionFactory(EOSFallbackMode fallbackMode = EOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == EOSFallbackMode.Strict;

        public string BackendName => "EOS";

        public INetworkSession CreateSession()
        {
            return new EOSNetworkSession();
        }

        public async Task<IEnumerable<SessionInfo>> FindSessionsAsync(NetworkSessionType sessionType)
        {
            if (!EOSRuntime.IsInitialized)
                return [];

            var platform = EOSRuntime.TryGetPlatform();
            var localUserId = EOSRuntime.TryGetCurrentProductUserId();
            if (platform == null || localUserId == null)
                return [];

            var lobbyInterface = platform.GetLobbyInterface();
            var bucketId = "MonoGame.Xna.Framework.Net";

            var createSearchOpts = new Lobby.CreateLobbySearchOptions { MaxResults = 50 };
            if (lobbyInterface.CreateLobbySearch(ref createSearchOpts, out var search) != Result.Success || search == null)
                return [];

            try
            {
                var setParamOpts = new Lobby.LobbySearchSetParameterOptions
                {
                    Parameter = new Lobby.AttributeData
                    {
                        Key = "bucket",
                        Value = new Lobby.AttributeDataValue { AsUtf8 = bucketId }
                    },
                    ComparisonOp = ComparisonOp.Equal
                };
                search.SetParameter(ref setParamOpts);

                var findOpts = new Lobby.LobbySearchFindOptions { LocalUserId = localUserId };
                var findTcs = new TaskCompletionSource<Lobby.LobbySearchFindCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                search.Find(ref findOpts, null, (ref Lobby.LobbySearchFindCallbackInfo info) =>
                {
                    findTcs.TrySetResult(info);
                });

                var findResult = await EOSClient.WaitForCallbackAsync(findTcs, EOSClient.GetCallbackTimeout(), CancellationToken.None, "LobbySearch.Find").ConfigureAwait(false);
                if (findResult.ResultCode != Result.Success)
                    return [];

                var countOpts = new Lobby.LobbySearchGetSearchResultCountOptions();
                var count = (int)search.GetSearchResultCount(ref countOpts);
                var sessions = new List<SessionInfo>(count);

                for (var i = 0; i < count; i++)
                {
                    var copyOpts = new Lobby.LobbySearchCopySearchResultByIndexOptions { LobbyIndex = (uint)i };
                    if (search.CopySearchResultByIndex(ref copyOpts, out var details) != Result.Success || details == null)
                        continue;

                    try
                    {
                        var infoOpts = new Lobby.LobbyDetailsCopyInfoOptions();
                        if (details.CopyInfo(ref infoOpts, out var lobbyInfo) == Result.Success && lobbyInfo.HasValue)
                        {
                            var info = lobbyInfo.Value;
                            sessions.Add(new SessionInfo
                            {
                                SessionId = info.LobbyId,
                                JoinAddress = info.LobbyId,
                                HostName = info.LobbyOwnerUserId?.ToString() ?? string.Empty,
                                CurrentPlayerCount = (int)(info.MaxMembers - info.AvailableSlots),
                                MaxPlayerCount = (int)info.MaxMembers,
                                IsPasswordProtected = false,
                                SessionType = sessionType
                            });
                        }
                    }
                    finally
                    {
                        details.Release();
                    }
                }

                return sessions;
            }
            finally
            {
                search.Release();
            }
        }

        public async Task<NetworkSession> CreateSessionAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            int maxGamers,
            int privateGamerSlots,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            if (IsStrict && !EOSRuntime.IsInitialized)
                throw new InvalidOperationException("EOS runtime is not initialized for strict session creation.");

            var session = await NetworkSession.CreateSystemLinkSessionAsync(
                sessionType,
                maxGamers,
                privateGamerSlots,
                cancellationToken).ConfigureAwait(false);

            session.AllowHostMigration = false;
            return session;
        }

        public async Task<AvailableNetworkSessionCollection> FindSessionsAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            if (IsStrict && !EOSRuntime.IsInitialized)
                throw new InvalidOperationException("EOS runtime is not initialized for strict session discovery.");

            var discovered = (await SystemLinkSessionManager
                .DiscoverSessionsAsync(maxLocalGamers, cancellationToken)
                .ConfigureAwait(false))
                .Where(x => x.SessionType == sessionType)
                .ToList();

            return new AvailableNetworkSessionCollection(discovered);
        }

        public async Task<NetworkSession> JoinSessionAsync(AvailableNetworkSession availableSession, CancellationToken cancellationToken = default)
        {
            if (availableSession == null)
                throw new ArgumentNullException(nameof(availableSession));

            if (IsStrict && !EOSRuntime.IsInitialized)
                throw new InvalidOperationException("EOS runtime is not initialized for strict session join.");

            var joined = await NetworkSession.JoinSystemLinkSessionAsync(availableSession, cancellationToken).ConfigureAwait(false);
            joined.AllowHostMigration = false;
            return joined;
        }
    }
}
