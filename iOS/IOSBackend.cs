namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// The iOS back-end: sessions are Game Center matches (<see cref="GameKitTransport"/>); everything
    /// above that is <see cref="NetworkSession"/>.
    /// </summary>
    /// <remarks>
    /// GameKit has no way to browse for matches, so <see cref="FindAsync"/> finds nothing: a game
    /// joins by building an <see cref="AvailableNetworkSession"/> from a session id it was given (an
    /// invite, a code) with <see cref="GameKitTransport.HostPeer"/> as its host. When the iOS runtime
    /// is not initialised, <see cref="IOSFallbackMode.PreferFallback"/> (the default) uses the LAN
    /// back-end instead; <see cref="IOSFallbackMode.Strict"/> fails instead. Not exercised on a device
    /// from this repository.
    /// </remarks>
    public sealed class IOSBackend : INetworkBackend
    {
        private readonly UdpBackend lan = new UdpBackend();
        private readonly IOSFallbackMode fallbackMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="IOSBackend"/> class.
        /// </summary>
        /// <param name="fallbackMode">What to do when <see cref="IOSRuntime"/> is not initialized: use the LAN back-end (default) or throw.</param>
        public IOSBackend(IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        /// <inheritdoc/>
        public string Name => "iOS";

        // Game Center matches are online; without the runtime this is the LAN, which carries SystemLink only
        /// <summary>
        /// Whether sessions of this type can be created or found right now.
        /// </summary>
        /// <remarks>
        /// <see cref="NetworkSessionType.SystemLink"/> is always supported (it uses the LAN when the iOS runtime is not initialized); every other type needs <see cref="IOSRuntime.Initialize"/>.
        /// </remarks>
        public bool SupportsSessionType(NetworkSessionType sessionType) =>
            sessionType == NetworkSessionType.SystemLink || IOSRuntime.IsInitialized;

        // not wired yet: Game Center delivers invitations through GKLocalPlayer's listener, which needs a
        // device to develop against (plan task 5.3)
        /// <summary>
        /// Declared to satisfy <see cref="INetworkBackend"/> but never raised: Game Center invitations are not wired up yet.
        /// </summary>
        public event EventHandler<InviteReceivedEventArgs> InviteReceived
        {
            add { }
            remove { }
        }

        private bool UseGameKit(string what)
        {
            if (IOSRuntime.IsInitialized)
                return true;
            if (fallbackMode == IOSFallbackMode.Strict)
                throw new InvalidOperationException($"iOS runtime is not initialized for strict {what}.");
            return false;
        }

        /// <summary>
        /// Creates a <see cref="GameKitTransport"/>, or a LAN transport when the iOS runtime is not initialized.
        /// </summary>
        /// <exception cref="InvalidOperationException">The runtime is not initialized and the mode is <see cref="IOSFallbackMode.Strict"/>.</exception>
        public IPeerTransport CreateTransport() =>
            UseGameKit("session creation or join") ? new GameKitTransport() : lan.CreateTransport();

        /// <summary>
        /// Starts a Game Center match as host, using the session id as the matchmaking queue name; with a non-GameKit transport it advertises on the LAN.
        /// </summary>
        /// <remarks>
        /// Returns once matchmaking has been requested; it does not wait for players to arrive.
        /// </remarks>
        public async Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
        {
            if (transport is GameKitTransport gameKit)
            {
                var ad = session.GetAdvertisement();
                await gameKit.StartMatchAsync(ad.SessionId, ad.MaxGamers, asHost: true).ConfigureAwait(false);
                return;
            }

            UseGameKit("session creation");
            await lan.AdvertiseAsync(session, transport, stopToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Always returns an empty list when Game Center is in use, because GameKit cannot browse for matches; with the LAN fallback it searches the LAN.
        /// </summary>
        /// <exception cref="InvalidOperationException">The runtime is not initialized and the mode is <see cref="IOSFallbackMode.Strict"/>.</exception>
        public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (!UseGameKit("session discovery"))
                return lan.FindAsync(sessionType, maxLocalGamers, cancellationToken);

            // GameKit does not expose a session-browse API
            return Task.FromResult<IReadOnlyList<AvailableNetworkSession>>(new List<AvailableNetworkSession>());
        }

        /// <summary>
        /// Joins the Game Center match for the session's id as a non-host, completing when a match has been found; returns <see langword="null"/> as there is nothing to release.
        /// </summary>
        /// <remarks>
        /// With a non-GameKit transport the LAN back-end handles the join.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
        public async Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (transport is GameKitTransport gameKit)
            {
                int max = session.CurrentGamerCount + session.OpenPublicGamerSlots + session.OpenPrivateGamerSlots;
                await gameKit.StartMatchAsync(session.SessionId, max, asHost: false).ConfigureAwait(false);
                return null;
            }

            UseGameKit("session join");
            return await lan.PrepareJoinAsync(session, transport, cancellationToken).ConfigureAwait(false);
        }
    }
}
