namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Event arguments for when a gamer leaves the session.
    /// </summary>
    public class GamerLeftEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the gamer who left.
        /// </summary>
        public NetworkGamer Gamer { get; internal set; } = null!;   // the parameterless constructor leaves it for the caller to set

        /// <summary>
        /// Initializes a new instance of GamerLeftEventArgs.
        /// </summary>
        internal GamerLeftEventArgs(NetworkGamer gamer)
        {
            Gamer = gamer;
        }

        /// <summary>
        /// Initializes a new instance of GamerLeftEventArgs with no arguments.
        /// </summary>
        internal GamerLeftEventArgs() { }
    }
}
