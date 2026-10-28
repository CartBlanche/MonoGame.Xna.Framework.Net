using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// The Android back-end. Until a Android transport exists (plan task 4.4) sessions run over the
    /// LAN (UDP) exactly as they did through the old provider; what this adds is strict mode, which
    /// refuses to create, find or join sessions when the Android runtime is not initialised.
    /// </summary>
    public sealed class AndroidBackend : INetworkBackend
    {
        private readonly UdpBackend lan = new UdpBackend();
        private readonly AndroidFallbackMode fallbackMode;

        public AndroidBackend(AndroidFallbackMode fallbackMode = AndroidFallbackMode.PreferFallback)
        {
            this.fallbackMode = fallbackMode;
        }

        public string Name => "Android";

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
            if (fallbackMode == AndroidFallbackMode.Strict && !AndroidRuntime.IsInitialized)
                throw new InvalidOperationException("Android runtime is not initialized for strict {what}.".Replace("{what}", what));
        }
    }
}
