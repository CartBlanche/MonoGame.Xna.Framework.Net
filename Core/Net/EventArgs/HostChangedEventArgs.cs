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

        public HostChangedEventArgs(NetworkGamer newHost, NetworkGamer oldHost)
        {
            NewHost = newHost;
            OldHost = oldHost;
        }
    }
}
