namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Sync lifecycle state for an earned achievement.
    /// </summary>
    public enum AchievementSyncState
    {
        /// <summary>
        /// Not earned and no progress has been recorded as earned.
        /// </summary>
        Locked = 0,
        /// <summary>
        /// Earned locally; the unlock has not yet been delivered to the live provider.
        /// </summary>
        UnlockedPendingSync = 1,
        /// <summary>
        /// Earned and, where remote sync applies, delivered to the live provider.
        /// </summary>
        UnlockedSynced = 2,
        /// <summary>
        /// Earned locally, but the last attempt to deliver the unlock to the live provider failed; it is retried by <see cref="AchievementService.ReconcilePendingUnlocksAsync"/>.
        /// </summary>
        SyncFailedRetry = 3,
    }

    /// <summary>
    /// Represents a single achievement and the current player's progress toward it.
    /// </summary>
    public sealed class Achievement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Achievement"/> class.
        /// </summary>
        /// <param name="key">Unique achievement key.</param>
        /// <param name="displayName">Display name; falls back to <paramref name="key"/> when null or whitespace.</param>
        /// <param name="description">Text describing the achievement.</param>
        /// <param name="howToEarn">Text describing how to earn it.</param>
        /// <param name="gamerScore">Gamer score awarded when it is earned.</param>
        /// <param name="percentComplete">Progress, clamped to 0 through 100.</param>
        /// <param name="isEarned">Whether the achievement has been earned.</param>
        /// <param name="earnedDate">When it was earned, or null if not earned or not known.</param>
        /// <param name="isHidden">Whether the achievement is hidden until earned.</param>
        /// <param name="iconKey">Key of the icon in the content or media provider, or null.</param>
        /// <param name="iconUri">URI of the icon, or null.</param>
        /// <param name="syncState">Sync state; when null it is <see cref="AchievementSyncState.UnlockedSynced"/> if earned, otherwise <see cref="AchievementSyncState.Locked"/>.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
        public Achievement(
            string key,
            string displayName,
            string description,
            string howToEarn,
            int gamerScore,
            float percentComplete,
            bool isEarned,
            DateTime? earnedDate,
            bool isHidden = false,
            string? iconKey = null,
            string? iconUri = null,
            AchievementSyncState? syncState = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(key));

            Key = key;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            Description = description ?? string.Empty;
            HowToEarn = howToEarn ?? string.Empty;
            GamerScore = gamerScore;
            PercentComplete = Math.Clamp(percentComplete, 0f, 100f);
            IsEarned = isEarned;
            EarnedDate = earnedDate;
            IsHidden = isHidden;
            IconKey = iconKey;
            IconUri = iconUri;
            SyncState = syncState ?? (isEarned ? AchievementSyncState.UnlockedSynced : AchievementSyncState.Locked);
        }

        /// <summary>
        /// Gets the unique key identifying this achievement.
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// Gets the display name, which is the key when no name was supplied.
        /// </summary>
        public string DisplayName { get; }
        /// <summary>
        /// Gets the description, or an empty string.
        /// </summary>
        public string Description { get; }
        /// <summary>
        /// Gets the text describing how to earn the achievement, or an empty string.
        /// </summary>
        public string HowToEarn { get; }
        /// <summary>
        /// Gets the gamer score awarded for earning the achievement.
        /// </summary>
        public int GamerScore { get; }
        /// <summary>
        /// Gets the progress toward the achievement as a percentage from 0 to 100.
        /// </summary>
        public float PercentComplete { get; }
        /// <summary>
        /// Gets whether the achievement has been earned.
        /// </summary>
        public bool IsEarned { get; }
        /// <summary>
        /// Gets when the achievement was earned, or null if it has not been.
        /// </summary>
        public DateTime? EarnedDate { get; }
        /// <summary>
        /// Gets whether the achievement is hidden until earned.
        /// </summary>
        public bool IsHidden { get; }
        /// <summary>
        /// Gets the key used to look up the icon through <see cref="AchievementMediaService"/>, or null.
        /// </summary>
        public string? IconKey { get; }
        /// <summary>
        /// Gets a URI for the icon, or null.
        /// </summary>
        public string? IconUri { get; }
        /// <summary>
        /// Gets the sync lifecycle state of this achievement.
        /// </summary>
        public AchievementSyncState SyncState { get; }
    }
}
