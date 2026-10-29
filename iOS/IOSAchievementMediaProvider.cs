using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Achievement media provider for iOS backend with bounded in-memory caching.
    /// </summary>
    public sealed class IOSAchievementMediaProvider : IAchievementMediaProvider
    {
        private const int MaxCacheEntries = 256;

        private readonly object gate = new();
        private readonly Dictionary<string, AchievementIcon> iconCache = new(StringComparer.Ordinal);
        private readonly Queue<string> cacheAccessOrder = new();

        /// <summary>
        /// Returns the achievement's icon from Game Center, or <see langword="null"/> when none is available.
        /// </summary>
        /// <remarks>
        /// Needs <see cref="IOSRuntime.Initialize"/> to have been called. If the live lookup is unavailable, fails, or has no icon data for the key, the result is <see langword="null"/>. Content type defaults to <c>image/png</c> when none is given. Results, misses included, are cached per key.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="gamer"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="achievementKey"/> is null, empty or whitespace.</exception>
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

            if (IOSRuntime.TryGetAppleGameCenterClient(out var client))
            {
                try
                {
                    var progress = await client.GetAchievementProgressAsync(cancellationToken).ConfigureAwait(false);
                    if (progress.TryGetValue(achievementKey, out var remote) && remote.IconData != null && remote.IconData.Length > 0)
                    {
                        var icon = new AchievementIcon(
                            remote.IconData,
                            string.IsNullOrWhiteSpace(remote.IconContentType) ? "image/png" : remote.IconContentType,
                            $"ios:{achievementKey}");

                        CacheResult(achievementKey, icon);
                        return icon;
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