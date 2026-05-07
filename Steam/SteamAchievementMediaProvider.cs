using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// Steam media provider for achievement icons with bounded in-memory caching.
    /// Resilient to Steam API failures; returns null when icons are unavailable.
    /// </summary>
    public sealed class SteamAchievementMediaProvider : IAchievementMediaProvider
    {
        private const int MaxCacheEntries = 256;

        private readonly object gate = new();
        private readonly Dictionary<string, AchievementIcon> iconCache = new(StringComparer.Ordinal);
        private readonly Queue<string> cacheAccessOrder = new();

        public Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
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
                    return Task.FromResult(cached);
                }
            }

            if (!SteamRuntime.IsInitialized)
            {
                return Task.FromResult<AchievementIcon>(null);
            }

            try
            {
                var iconHandle = SteamUserStats.GetAchievementIcon(achievementKey);
                if (iconHandle == 0)
                {
                    CacheResult(achievementKey, null);
                    return Task.FromResult<AchievementIcon>(null);
                }

                if (!SteamUtils.GetImageSize(iconHandle, out var width, out var height) || width == 0 || height == 0)
                {
                    CacheResult(achievementKey, null);
                    return Task.FromResult<AchievementIcon>(null);
                }

                var rgbaByteCount = checked((int)(width * height * 4));
                var rgba = new byte[rgbaByteCount];
                if (!SteamUtils.GetImageRGBA(iconHandle, rgba, rgbaByteCount))
                {
                    CacheResult(achievementKey, null);
                    return Task.FromResult<AchievementIcon>(null);
                }

                var icon = new AchievementIcon(
                    data: rgba,
                    contentType: "application/x-steam-rgba32",
                    cacheKey: $"steam:{achievementKey}:{iconHandle}",
                    width: (int)width,
                    height: (int)height);

                CacheResult(achievementKey, icon);
                return Task.FromResult(icon);
            }
            catch
            {
                // Resilient: cache null result to avoid repeated Steam API calls on failure.
                CacheResult(achievementKey, null);
                return Task.FromResult<AchievementIcon>(null);
            }
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

        /// <summary>
        /// Clears the icon cache. Useful for testing or cache invalidation.
        /// </summary>
        internal void ClearCache()
        {
            lock (gate)
            {
                iconCache.Clear();
                cacheAccessOrder.Clear();
            }
        }
    }
}
