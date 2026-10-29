namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Contract for achievement media/icon backends.
    /// </summary>
    public interface IAchievementMediaProvider
    {
        Task<AchievementIcon?> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Global media provider hook used to load achievement icons.
    /// </summary>
    public static class AchievementMediaService
    {
        private static IAchievementMediaProvider? liveProvider;
        private static IAchievementMediaProvider localProvider = new InMemoryAchievementMediaProvider();

        public static IAchievementMediaProvider? LiveProvider
        {
            get => liveProvider;
            set => liveProvider = value;
        }

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
