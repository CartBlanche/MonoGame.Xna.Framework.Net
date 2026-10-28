namespace Microsoft.Xna.Framework.Net.iOS
{
    public sealed class IOSNetworkSessionFactory : INetworkSessionFactory
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

        public Task<IEnumerable<SessionInfo>> FindSessionsAsync(NetworkSessionType sessionType)
        {
            if (IsStrict && !IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime is not initialized for strict session discovery.");

            // Phase 1: GameKit does not expose a session-browse API.
            // Phase 2 will add CloudKit-backed session discovery.
            return Task.FromResult(Enumerable.Empty<SessionInfo>());
        }
    }
}
