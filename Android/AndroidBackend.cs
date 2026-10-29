using Android.Gms.Nearby;
using Android.Gms.Nearby.Connection;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// The Android back-end: hosts advertise over Google Nearby Connections and clients discover them,
    /// so devices find each other without a network. Gameplay travels over Nearby
    /// (<see cref="AndroidNearbyTransport"/>); everything above that is <see cref="NetworkSession"/>.
    /// </summary>
    /// <remarks>
    /// When the Android runtime has no Activity, <see cref="AndroidFallbackMode.PreferFallback"/> (the
    /// default) uses the LAN back-end instead; <see cref="AndroidFallbackMode.Strict"/> fails instead.
    /// Needs a device with Google Play services; it has not been exercised on one from this repository.
    /// </remarks>
    public sealed class AndroidBackend : INetworkBackend
    {
        private const int DiscoveryScanMs = 2000;

        private readonly UdpBackend lan = new UdpBackend();
        private readonly AndroidFallbackMode fallbackMode;

        public AndroidBackend(AndroidFallbackMode fallbackMode = AndroidFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        public string Name => "Android";

        // Nearby is proximity play, like the LAN: no online session types
        public bool SupportsSessionType(NetworkSessionType sessionType) => sessionType == NetworkSessionType.SystemLink;

        // Nearby Connections has no invitations
        public event EventHandler<InviteReceivedEventArgs> InviteReceived
        {
            add { }
            remove { }
        }

        private bool UseNearby(string what)
        {
            if (AndroidRuntime.IsInitialized && AndroidRuntime.TryGetActivity(out _))
                return true;
            if (fallbackMode == AndroidFallbackMode.Strict)
                throw new InvalidOperationException($"Android runtime is not initialized for strict {what}.");
            return false;
        }

        public IPeerTransport CreateTransport() =>
            UseNearby("session creation or join") ? new AndroidNearbyTransport() : lan.CreateTransport();

        public async Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            if (transport is AndroidNearbyTransport nearby)
            {
                await nearby.StartAdvertisingAsync(GamerServices.SignedInGamer.Current?.Gamertag ?? "Host").ConfigureAwait(false);
                return;
            }

            UseNearby("session creation");
            await lan.AdvertiseAsync(session, transport, stopToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (!UseNearby("session discovery") || !AndroidRuntime.TryGetActivity(out var activity))
                return await lan.FindAsync(sessionType, maxLocalGamers, cancellationToken).ConfigureAwait(false);

            var connectionsClient = NearbyClass.GetConnectionsClient(activity);
            var found = new List<AvailableNetworkSession>();

            var discovery = new ScanCallback(
                onFound: (endpointId, info) =>
                {
                    lock (found)
                    {
                        found.Add(new AvailableNetworkSession(
                            sessionName: info.EndpointName,
                            hostGamertag: info.EndpointName,
                            currentGamerCount: 1,
                            openPublicGamerSlots: 7,
                            openPrivateGamerSlots: 0,
                            sessionType: sessionType,
                            sessionProperties: new Dictionary<string, object>(),
                            sessionId: endpointId,
                            hostPeer: AndroidNearbyTransport.ToPeer(endpointId)));
                    }
                });

            var options = new DiscoveryOptions.Builder().SetStrategy(Strategy.P2pStar).Build();
            try
            {
                await connectionsClient.StartDiscoveryAsync(AndroidNearbyTransport.ServiceId, discovery, options).ConfigureAwait(false);
                await Task.Delay(DiscoveryScanMs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                connectionsClient.StopDiscovery();
            }

            lock (found) return found.ToList();
        }

        // connecting happens on the first datagram to the host, so there is nothing to prepare
        public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (transport is not AndroidNearbyTransport)
                UseNearby("session join");
            return Task.FromResult<IDisposable>(null);
        }

        private sealed class ScanCallback : EndpointDiscoveryCallback
        {
            private readonly Action<string, DiscoveredEndpointInfo> onFound;

            internal ScanCallback(Action<string, DiscoveredEndpointInfo> onFound) => this.onFound = onFound;

            public override void OnEndpointFound(string endpointId, DiscoveredEndpointInfo info) => onFound(endpointId, info);

            public override void OnEndpointLost(string endpointId)
            {
            }
        }
    }
}
