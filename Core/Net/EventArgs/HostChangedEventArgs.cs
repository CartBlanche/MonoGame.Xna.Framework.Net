namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Arguments of <see cref="NetworkSession.HostChanged"/>.
    /// </summary>
    public class HostChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the new host of the session.
        /// </summary>
        public NetworkGamer NewHost { get; }

        /// <summary>
        /// Gets the gamer who was the previous host.
        /// </summary>
        public NetworkGamer OldHost { get; }

        /// <summary>Initializes a new instance of <see cref="HostChangedEventArgs"/> for a change from <paramref name="oldHost"/> to <paramref name="newHost"/>.</summary>
        public HostChangedEventArgs(NetworkGamer newHost, NetworkGamer oldHost)
        {
            NewHost = newHost;
            OldHost = oldHost;
        }
    }
}
