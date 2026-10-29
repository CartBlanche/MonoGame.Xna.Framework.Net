using System;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Chooses the back-end that carries <see cref="NetworkSession"/>s. A networking plugin's bootstrap
    /// sets its back-end at start-up; until then sessions run over the LAN.
    /// </summary>
    public static class NetworkServiceProvider
    {
        private static INetworkBackend backend;
        private static readonly object lockObject = new object();

        /// <summary>
        /// The back-end that carries sessions: LAN (UDP) unless a plugin has set another.
        /// </summary>
        public static INetworkBackend Backend
        {
            get
            {
                lock (lockObject)
                    return backend ??= new UdpBackend();
            }
        }

        /// <summary>
        /// The name of the back-end in use, e.g. "Steam".
        /// </summary>
        public static string ActiveBackendName => Backend.Name;

        /// <summary>
        /// Chooses the back-end that <see cref="NetworkSession"/> creates, finds and joins sessions through.
        /// </summary>
        public static void SetBackend(INetworkBackend newBackend)
        {
            if (newBackend == null) throw new ArgumentNullException(nameof(newBackend));
            lock (lockObject)
            {
                backend = newBackend;
                System.Diagnostics.Debug.WriteLine($"[Network] Using {newBackend.Name} backend");
            }
        }

        /// <summary>
        /// Goes back to the default LAN (UDP) back-end.
        /// </summary>
        public static void ResetToDefault()
        {
            lock (lockObject)
            {
                backend = new UdpBackend();
                System.Diagnostics.Debug.WriteLine("[Network] Reset to default UDP backend");
            }
        }
    }
}
