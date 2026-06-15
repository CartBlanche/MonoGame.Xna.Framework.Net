using Android.Gms.Nearby;
using Android.Gms.Nearby.Connection;

namespace Microsoft.Xna.Framework.Net.Android
{
    public sealed class AndroidNetworkSessionFactory : INetworkSessionFactory, INetworkSessionProvider
    {
        private const int DiscoveryScanMs = 2000;

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
            if (IsStrict && !AndroidRuntime.IsInitialized)
                throw new InvalidOperationException("Android runtime is not initialized for strict session discovery.");

            if (!AndroidRuntime.IsInitialized || !AndroidRuntime.TryGetActivity(out var activity))
                return [];

            var connectionsClient = NearbyClass.GetConnectionsClient(activity);
            var sessions = new List<SessionInfo>();

            var discoveryCallback = new ScanEndpointDiscoveryCallback(
                onFound: (endpointId, info) =>
                {
                    lock (sessions)
                    {
                        sessions.Add(new SessionInfo
                        {
                            SessionId = endpointId,
                            JoinAddress = endpointId,
                            HostName = info.EndpointName,
                            CurrentPlayerCount = 1,
                            MaxPlayerCount = 8,
                            IsPasswordProtected = false,
                            SessionType = sessionType
                        });
                    }
                },
                onLost: _ => { }
            );

            var options = new DiscoveryOptions.Builder()
                .SetStrategy(Strategy.P2pStar)
                .Build();

            try
            {
                await connectionsClient
                    .StartDiscoveryAsync(AndroidNetworkSession.ServiceId, discoveryCallback, options)
                    .ConfigureAwait(false);

                await Task.Delay(DiscoveryScanMs).ConfigureAwait(false);
            }
            finally
            {
                connectionsClient.StopDiscovery();
            }

            lock (sessions) { return sessions.ToList(); }
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

        private sealed class ScanEndpointDiscoveryCallback : EndpointDiscoveryCallback
        {
            private readonly Action<string, DiscoveredEndpointInfo> onFound;
            private readonly Action<string> onLost;

            internal ScanEndpointDiscoveryCallback(
                Action<string, DiscoveredEndpointInfo> onFound,
                Action<string> onLost)
            {
                this.onFound = onFound;
                this.onLost = onLost;
            }

            public override void OnEndpointFound(string endpointId, DiscoveredEndpointInfo info)
                => onFound(endpointId, info);

            public override void OnEndpointLost(string endpointId)
                => onLost(endpointId);
        }
    }
}
