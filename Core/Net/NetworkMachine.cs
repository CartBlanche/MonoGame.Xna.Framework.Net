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
        /// Gets whether this is the machine the code is running on.
        /// </summary>
        internal bool IsLocal => isLocal;
    }
}
