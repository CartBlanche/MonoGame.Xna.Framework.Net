namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Represents a single achievement and the current player's progress toward it.
    /// </summary>
    public sealed class Achievement
    {
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
            string iconKey = null,
            string iconUri = null)
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
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string HowToEarn { get; }
        public int GamerScore { get; }
        public float PercentComplete { get; }
        public bool IsEarned { get; }
        public DateTime? EarnedDate { get; }
        public bool IsHidden { get; }
        public string IconKey { get; }
        public string IconUri { get; }
    }
}
