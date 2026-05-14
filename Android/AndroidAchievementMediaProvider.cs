using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Achievement media provider for Android backend.
    /// Returns deterministic null when media is unavailable and caches lookups to avoid repeated work.
    /// </summary>
    public sealed class AndroidAchievementMediaProvider : IAchievementMediaProvider
    {
        private const int MaxCacheEntries = 256;
        private static readonly HttpClient IconHttpClient = new();

        private readonly object gate = new();
        private readonly Dictionary<string, AchievementIcon> iconCache = new(StringComparer.Ordinal);
        private readonly Queue<string> cacheAccessOrder = new();

        public async Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                if (iconCache.TryGetValue(achievementKey, out var cached))
                {
                    return cached;
                }
            }

            if (AndroidRuntime.TryGetGooglePlayGamesClient(out var client))
            {
                try
                {
                    var progress = await client.GetAchievementProgressAsync(cancellationToken).ConfigureAwait(false);
                    if (progress.TryGetValue(achievementKey, out var remote) && !string.IsNullOrWhiteSpace(remote.IconUrl))
                    {
                        var bytes = await IconHttpClient.GetByteArrayAsync(remote.IconUrl, cancellationToken).ConfigureAwait(false);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var icon = new AchievementIcon(bytes, "image/png", $"android:{achievementKey}:{remote.IconUrl}");
                            CacheResult(achievementKey, icon);
                            return icon;
                        }
                    }
                }
                catch
                {
                    // Fall back to null icon when live fetch fails.
                }
            }

            CacheResult(achievementKey, null);
            return null;
        }

        private void CacheResult(string achievementKey, AchievementIcon icon)
        {
            lock (gate)
            {
                if (iconCache.Count >= MaxCacheEntries && !iconCache.ContainsKey(achievementKey))
                {
                    var evictKey = cacheAccessOrder.Dequeue();
                    iconCache.Remove(evictKey);
                }

                iconCache[achievementKey] = icon;
                cacheAccessOrder.Enqueue(achievementKey);
            }
        }
    }
}
