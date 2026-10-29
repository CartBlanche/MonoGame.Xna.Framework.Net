namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Canonical achievement metadata shared across providers.
    /// </summary>
    public sealed class AchievementDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AchievementDefinition"/> class.
        /// </summary>
        /// <param name="key">Unique achievement key.</param>
        /// <param name="displayName">Display name; falls back to <paramref name="key"/> when null or whitespace.</param>
        /// <param name="description">Text describing the achievement.</param>
        /// <param name="howToEarn">Text describing how to earn it.</param>
        /// <param name="gamerScore">Gamer score awarded when it is earned.</param>
        /// <param name="isHidden">Whether the achievement is hidden until earned.</param>
        /// <param name="iconKey">Key of the icon in the content or media provider, or null.</param>
        /// <param name="iconUri">URI of the icon, or null.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
        public AchievementDefinition(
            string key,
            string displayName,
            string description = "",
            string howToEarn = "",
            int gamerScore = 0,
            bool isHidden = false,
            string? iconKey = null,
            string? iconUri = null)
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

        /// <summary>
        /// Gets the unique key identifying the achievement.
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
    }
}
