namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Identifies a leaderboard by key.
    /// </summary>
    public sealed class LeaderboardIdentity
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LeaderboardIdentity"/> class.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
        public LeaderboardIdentity(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Leaderboard key cannot be empty.", nameof(key));

            Key = key;
        }

        /// <summary>
        /// Gets the leaderboard key.
        /// </summary>
        public string Key { get; }
    }
}
