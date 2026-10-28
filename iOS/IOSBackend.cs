using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// The iOS back-end. Until a iOS transport exists (plan task 4.4) sessions run over the
    /// LAN (UDP) exactly as they did through the old provider; what this adds is strict mode, which
    /// refuses to create, find or join sessions when the iOS runtime is not initialised.
    /// </summary>
    public sealed class IOSBackend : INetworkBackend
    {
        private readonly UdpBackend lan = new UdpBackend();
        private readonly IOSFallbackMode fallbackMode;

        public IOSBackend(IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        public string Name => "iOS";

        public IPeerTransport CreateTransport()
        {
            Check("session creation or join");
            return lan.CreateTransport();
        }

        public Task AdvertiseAsync(ISessionAdvertiser session, CancellationToken stopToken)
        {
            Check("session creation");
            return lan.AdvertiseAsync(session, stopToken);
        }

        public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
        {
            Check("session discovery");
            return lan.FindAsync(sessionType, maxLocalGamers, cancellationToken);
        }

        public Task PrepareJoinAsync(AvailableNetworkSession session, CancellationToken cancellationToken)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            Check("session join");
            return lan.PrepareJoinAsync(session, cancellationToken);
        }

        private void Check(string what)
        {
            if (fallbackMode == IOSFallbackMode.Strict && !IOSRuntime.IsInitialized)
                throw new InvalidOperationException("iOS runtime is not initialized for strict {what}.".Replace("{what}", what));
        }
    }
}
