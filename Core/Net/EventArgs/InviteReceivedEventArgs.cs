namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Raised by a back-end when the local user accepts an invitation (or clicks "join game") in the
    /// platform's own UI: the session they were invited to.
    /// </summary>
    public class InviteReceivedEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of <see cref="InviteReceivedEventArgs"/> for the session the user was invited to.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        public InviteReceivedEventArgs(AvailableNetworkSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>The session the invitation is for, ready to join.</summary>
        public AvailableNetworkSession Session { get; }
    }
}
