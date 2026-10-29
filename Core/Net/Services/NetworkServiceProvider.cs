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

        // the invitation the user last accepted in the platform's UI, until the game joins it
        private static AvailableNetworkSession pendingInvite;
        private static bool inviteNotificationDue;

        /// <summary>
        /// The back-end that carries sessions: LAN (UDP) unless a plugin has set another.
        /// </summary>
        public static INetworkBackend Backend
        {
            get
            {
                lock (lockObject)
                {
                    if (backend == null)
                    {
                        backend = new UdpBackend();
                        backend.InviteReceived += OnInviteReceived;
                    }
                    return backend;
                }
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
                if (backend != null)
                    backend.InviteReceived -= OnInviteReceived;
                backend = newBackend;
                backend.InviteReceived += OnInviteReceived;
                System.Diagnostics.Debug.WriteLine($"[Network] Using {newBackend.Name} backend");
            }
        }

        private static void OnInviteReceived(object sender, InviteReceivedEventArgs e)
        {
            lock (lockObject)
            {
                pendingInvite = e.Session;
                inviteNotificationDue = true;
            }
        }

        /// <summary>
        /// Raises <see cref="NetworkSession.InviteAccepted"/> for an invitation that arrived since the last
        /// call, on the calling thread. Called from <c>NetworkSession.Update()</c> and
        /// <c>GamerServicesComponent.Update()</c>, so it is raised on the game's thread, as in XNA.
        /// </summary>
        internal static void DispatchInvites()
        {
            AvailableNetworkSession invite;
            lock (lockObject)
            {
                if (!inviteNotificationDue)
                    return;
                inviteNotificationDue = false;
                invite = pendingInvite;
            }

            if (NetworkSession.RaiseInviteAccepted(invite))
            {
                // already in that session: nothing left to join
                lock (lockObject)
                {
                    if (pendingInvite == invite)
                        pendingInvite = null;
                }
            }
        }

        /// <summary>The invitation waiting to be joined, which is then no longer pending; null if none.</summary>
        internal static AvailableNetworkSession TakePendingInvite()
        {
            lock (lockObject)
            {
                var invite = pendingInvite;
                pendingInvite = null;
                return invite;
            }
        }

        /// <summary>
        /// Goes back to the default LAN (UDP) back-end.
        /// </summary>
        public static void ResetToDefault()
        {
            SetBackend(new UdpBackend());
        }
    }
}
