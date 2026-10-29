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

        public IOSBackend(IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        public string Name => "iOS";

        // Game Center matches are online; without the runtime this is the LAN, which carries SystemLink only
        public bool SupportsSessionType(NetworkSessionType sessionType) =>
            sessionType == NetworkSessionType.SystemLink || IOSRuntime.IsInitialized;

        // not wired yet: Game Center delivers invitations through GKLocalPlayer's listener, which needs a
        // device to develop against (plan task 5.3)
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

        public IPeerTransport CreateTransport() =>
            UseGameKit("session creation or join") ? new GameKitTransport() : lan.CreateTransport();

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

        public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            if (!UseGameKit("session discovery"))
                return lan.FindAsync(sessionType, maxLocalGamers, cancellationToken);

            // GameKit does not expose a session-browse API
            return Task.FromResult<IReadOnlyList<AvailableNetworkSession>>(new List<AvailableNetworkSession>());
        }

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
