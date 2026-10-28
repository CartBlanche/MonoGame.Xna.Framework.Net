using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Represents a machine in a network session: all the gamers playing on one device. The local
    /// machine holds every local gamer; each remote machine holds the gamer that joined from it.
    /// </summary>
    public class NetworkMachine
    {
        private readonly NetworkSession session;
        private readonly bool isLocal;

        internal NetworkMachine(NetworkSession session, bool isLocal)
        {
            this.session = session;
            this.isLocal = isLocal;
        }

        /// <summary>
        /// Gets the gamers on this machine that are in the session now.
        /// </summary>
        public GamerCollection Gamers =>
            new GamerCollection(session.AllGamers.Where(g => g.Machine == this).ToList());

        /// <summary>
        /// Removes this machine, and so its gamers, from the session. Only the host may, and not from itself.
        /// The machine's session ends with <see cref="NetworkSessionEndReason.RemovedByHost"/>.
        /// </summary>
        public void RemoveFromSession()
        {
            session.RemoveMachine(this);
        }

        /// <summary>
        /// Gets whether this is the machine the code is running on.
        /// </summary>
        internal bool IsLocal => isLocal;
    }
}
