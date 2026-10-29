
namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Event arguments for when an invite is accepted.
	/// </summary>
	public class InviteAcceptedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the gamer who accepted the invite.
        /// </summary>
        public SignedInGamer Gamer { get; internal set; }

        /// <summary>
        /// Gets whether the invite is to the session the gamer is already in, in which case there is
        /// nothing to join.
        /// </summary>
        public bool IsCurrentSession { get; internal set; }

        public InviteAcceptedEventArgs(SignedInGamer gamer, bool isCurrentSession)
        {
            Gamer = gamer;
            IsCurrentSession = isCurrentSession;
        }
    }
}
