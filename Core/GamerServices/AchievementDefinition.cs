namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Canonical achievement metadata shared across providers.
    /// </summary>
    public sealed class AchievementDefinition
    {
        public AchievementDefinition(
            string key,
            string displayName,
            string description = "",
            string howToEarn = "",
            int gamerScore = 0,
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
            IsHidden = isHidden;
            IconKey = iconKey;
            IconUri = iconUri;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string HowToEarn { get; }
        public int GamerScore { get; }
        public bool IsHidden { get; }
        public string IconKey { get; }
        public string IconUri { get; }
    }
}
