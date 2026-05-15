namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Factory and provider for iOS-backed sessions.
    /// The initial slice reuses SystemLink networking while preserving backend routing seams.
    /// </summary>
    public sealed class IOSNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private readonly IOSFallbackMode fallbackMode;

        public IOSNetworkSessionFactory(IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        private bool IsStrict => fallbackMode == IOSFallbackMode.Strict;

        public string BackendName => "iOS";

        public INetworkSession CreateSession()
        {
            return new IOSNetworkSession();
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
            if (IsStrict && !IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime is not initialized for strict session creation.");

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
            if (IsStrict && !IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime is not initialized for strict session discovery.");

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

            if (IsStrict && !IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime is not initialized for strict session join.");

            var joined = await NetworkSession.JoinSystemLinkSessionAsync(availableSession, cancellationToken).ConfigureAwait(false);
            joined.AllowHostMigration = false;
            return joined;
        }
    }
}