using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Collection of network gamers in a session.
	/// </summary>
	public class GamerCollection : GamerCollection<NetworkGamer>
    {
        internal GamerCollection(IList<NetworkGamer> list) : base(list) { }

        /// <summary>
        /// Finds a gamer by their session-unique id.
        /// </summary>
        /// <param name="id">The <see cref="NetworkGamer.Id"/> to search for.</param>
        /// <returns>The gamer with that id, or null if not found.</returns>
        public NetworkGamer? FindGamerById(byte id)
        {
            foreach (var gamer in this)
            {
                if (gamer.Id == id)
                    return gamer;
            }
            return null;
        }

        /// <summary>
        /// Gets the host gamer of the session.
        /// </summary>
        public NetworkGamer? Host
        {
            get
            {
                foreach (var gamer in this)
                {
                    if (gamer.IsHost)
                        return gamer;
                }
                return null;
            }
        }
    }
}