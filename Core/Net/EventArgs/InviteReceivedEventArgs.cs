namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Raised by a back-end when the local user accepts an invitation (or clicks "join game") in the
    /// platform's own UI: the session they were invited to.
    /// </summary>
    public class InviteReceivedEventArgs : EventArgs
    {
        public InviteReceivedEventArgs(AvailableNetworkSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>The session the invitation is for, ready to join.</summary>
        public AvailableNetworkSession Session { get; }
    }
}
