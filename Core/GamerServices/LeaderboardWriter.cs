namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Holds data used to write a leaderboard score.
    /// </summary>
    public sealed class LeaderboardWriter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LeaderboardWriter"/> class for the current gamer, <see cref="SignedInGamer.Current"/>.
        /// </summary>
        public LeaderboardWriter(LeaderboardIdentity leaderboardIdentity)
            : this(leaderboardIdentity, SignedInGamer.Current)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LeaderboardWriter"/> class.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="leaderboardIdentity"/> or <paramref name="gamer"/> is null.</exception>
        public LeaderboardWriter(LeaderboardIdentity leaderboardIdentity, SignedInGamer gamer)
        {
            LeaderboardIdentity = leaderboardIdentity ?? throw new ArgumentNullException(nameof(leaderboardIdentity));
            Gamer = gamer ?? throw new ArgumentNullException(nameof(gamer));
        }

        /// <summary>
        /// Gets the leaderboard being written to.
        /// </summary>
        public LeaderboardIdentity LeaderboardIdentity { get; }
        /// <summary>
        /// Gets the gamer the score is written for.
        /// </summary>
        public SignedInGamer Gamer { get; }
        /// <summary>
        /// Gets or sets the score to submit.
        /// </summary>
        public long Score { get; set; }
    }
}
