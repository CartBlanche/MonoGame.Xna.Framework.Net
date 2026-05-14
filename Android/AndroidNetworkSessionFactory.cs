namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Factory and provider for Android-backed sessions.
    /// The initial slice reuses SystemLink networking while preserving backend routing seams.
    /// </summary>
    public sealed class AndroidNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private readonly AndroidFallbackMode fallbackMode;

        public AndroidNetworkSessionFactory(AndroidFallbackMode fallbackMode = AndroidFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == AndroidFallbackMode.Strict;

        public string BackendName => "Android";

        public INetworkSession CreateSession()
        {
            return new AndroidNetworkSession();
        }

        public async Task<IEnumerable<SessionInfo>> FindSessionsAsync(NetworkSessionType sessionType)
        {
            var found = await FindSessionsAsync(sessionType, 1, null).ConfigureAwait(false);

            return found.Select(session => new SessionInfo
            {
                SessionId = session.SessionId,
                JoinAddress = session.HostEndpoint?.ToString() ?? string.Empty,
                HostName = session.HostGamertag,
                CurrentPlayerCount = session.CurrentGamerCount,
                MaxPlayerCount = session.CurrentGamerCount + session.OpenPublicGamerSlots + session.OpenPrivateGamerSlots,
                IsPasswordProtected = false,
                SessionType = sessionType
            }).ToList();
        }

        public async Task<NetworkSession> CreateSessionAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            int maxGamers,
            int privateGamerSlots,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default)
        {
            if (IsStrict && !AndroidRuntime.IsInitialized)
                throw new InvalidOperationException("Android runtime is not initialized for strict session creation.");

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
            if (IsStrict && !AndroidRuntime.IsInitialized)
                throw new InvalidOperationException("Android runtime is not initialized for strict session discovery.");

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

            if (IsStrict && !AndroidRuntime.IsInitialized)
                throw new InvalidOperationException("Android runtime is not initialized for strict session join.");

            var joined = await NetworkSession.JoinSystemLinkSessionAsync(availableSession, cancellationToken).ConfigureAwait(false);
            joined.AllowHostMigration = false;
            return joined;
        }
    }
}
