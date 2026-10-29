namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Represents a single leaderboard row.
    /// </summary>
    public sealed class LeaderboardEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LeaderboardEntry"/> class.
        /// </summary>
        public LeaderboardEntry(int rank, string gamertag, long score, bool isCurrentGamer)
        {
            Rank = rank;
            Gamertag = gamertag;
            Score = score;
            IsCurrentGamer = isCurrentGamer;
        }

        /// <summary>
        /// Gets the 1-based rank of this row across the whole leaderboard, not within the page.
        /// </summary>
        public int Rank { get; }
        /// <summary>
        /// Gets the gamertag the score belongs to.
        /// </summary>
        public string Gamertag { get; }
        /// <summary>
        /// Gets the score.
        /// </summary>
        public long Score { get; }
        /// <summary>
        /// Gets whether this row belongs to the pivot gamer the page was read for.
        /// </summary>
        public bool IsCurrentGamer { get; }
    }
}
