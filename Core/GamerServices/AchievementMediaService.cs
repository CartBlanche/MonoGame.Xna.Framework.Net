namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Contract for achievement media/icon backends.
    /// </summary>
    public interface IAchievementMediaProvider
    {
        /// <summary>
        /// Gets the icon for an achievement, or null if the provider has none.
        /// </summary>
        /// <param name="gamer">The gamer the icon is requested for.</param>
        /// <param name="achievementKey">Key of the achievement.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        Task<AchievementIcon?> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Global media provider hook used to load achievement icons.
    /// </summary>
    public static class AchievementMediaService
    {
        private static IAchievementMediaProvider? liveProvider;
        private static IAchievementMediaProvider localProvider = new InMemoryAchievementMediaProvider();

        /// <summary>
        /// Gets or sets the provider used when the gamer is signed in to live. When null, <see cref="LocalProvider"/> is used.
        /// </summary>
        public static IAchievementMediaProvider? LiveProvider
        {
            get => liveProvider;
            set => liveProvider = value;
        }

        /// <summary>
        /// Gets or sets the provider used when the gamer is not signed in to live or no <see cref="LiveProvider"/> is set. The default is an in-memory provider.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public static IAchievementMediaProvider LocalProvider
        {
            get => localProvider;
            set => localProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        internal static IAchievementMediaProvider ResolveProvider(SignedInGamer gamer)
        {
            if (gamer != null && gamer.IsSignedInToLive && LiveProvider != null)
                return LiveProvider;

            return LocalProvider;
        }
    }

    internal sealed class InMemoryAchievementMediaProvider : IAchievementMediaProvider
    {
        private readonly object gate = new();
        private readonly Dictionary<string, AchievementIcon> iconsByAchievementKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AchievementIcon> iconsByIconKey = new(StringComparer.Ordinal);

        public Task<AchievementIcon?> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(gamer);
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                if (iconsByAchievementKey.TryGetValue(achievementKey, out var direct))
                {
                    return Task.FromResult<AchievementIcon?>(direct);
                }

                var definition = AchievementCatalog.Get(achievementKey);
                if (definition != null && !string.IsNullOrWhiteSpace(definition.IconKey))
                {
                    if (iconsByIconKey.TryGetValue(definition.IconKey, out var byIconKey))
                    {
                        return Task.FromResult<AchievementIcon?>(byIconKey);
                    }
                }
            }

            return Task.FromResult<AchievementIcon?>(null);
        }

        internal void RegisterByAchievementKey(string achievementKey, AchievementIcon icon)
        {
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));
            ArgumentNullException.ThrowIfNull(icon);

            lock (gate)
            {
                iconsByAchievementKey[achievementKey] = icon;
            }
        }

        internal void RegisterByIconKey(string iconKey, AchievementIcon icon)
        {
            if (string.IsNullOrWhiteSpace(iconKey))
                throw new ArgumentException("Icon key cannot be empty.", nameof(iconKey));
            ArgumentNullException.ThrowIfNull(icon);

            lock (gate)
            {
                iconsByIconKey[iconKey] = icon;
            }
        }
    }
}
