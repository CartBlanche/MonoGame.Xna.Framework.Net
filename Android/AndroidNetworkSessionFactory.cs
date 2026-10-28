using Android.Gms.Nearby;
using Android.Gms.Nearby.Connection;

namespace Microsoft.Xna.Framework.Net.Android
{
    public sealed class AndroidNetworkSessionFactory : INetworkSessionFactory
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
