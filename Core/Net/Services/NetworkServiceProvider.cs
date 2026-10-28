using System;

namespace Microsoft.Xna.Framework.Net
{
    public static class NetworkServiceProvider
    {
        private static INetworkSessionFactory sessionFactory;
        private static INetworkBackend backend;
        private static readonly object lockObject = new object();

        public static INetworkSessionFactory SessionFactory
        {
            get
            {
                lock (lockObject)
                {
                    if (sessionFactory == null)
                        throw new InvalidOperationException("Network session factory not configured. Call NetworkServiceProvider.SetSessionFactory() during game initialization.");
                    return sessionFactory;
                }
            }
        }

        public static string ActiveBackendName
        {
            get
            {
                lock (lockObject)
                {
                    return sessionFactory?.BackendName;
                }
            }
        }

        public static void SetSessionFactory(INetworkSessionFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (lockObject)
            {
                sessionFactory = factory;
                System.Diagnostics.Debug.WriteLine($"[Network] Using {factory.BackendName} backend");
            }
        }

        public static void ResetToDefault()
        {
            lock (lockObject)
            {
                sessionFactory = new UdpNetworkSessionFactory();
                backend = new UdpBackend();
                System.Diagnostics.Debug.WriteLine("[Network] Reset to default UDP backend");
            }
        }

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

        public static bool IsConfigured
        {
            get
            {
                lock (lockObject)
                {
                    return sessionFactory != null;
                }
            }
        }
    }
}
