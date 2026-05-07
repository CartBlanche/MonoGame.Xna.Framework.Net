using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// Steam media provider for achievement icons.
    /// Current implementation is metadata-driven and returns null when no icon URI is registered.
    /// </summary>
    public sealed class SteamAchievementMediaProvider : IAchievementMediaProvider
    {
        public Task<AchievementIcon> GetIconAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            if (!SteamRuntime.IsInitialized)
            {
                return Task.FromResult<AchievementIcon>(null);
            }

            try
            {
                var iconHandle = SteamUserStats.GetAchievementIcon(achievementKey);
                if (iconHandle == 0)
                {
                    return Task.FromResult<AchievementIcon>(null);
                }

                if (!SteamUtils.GetImageSize(iconHandle, out var width, out var height) || width == 0 || height == 0)
                {
                    return Task.FromResult<AchievementIcon>(null);
                }

                var rgbaByteCount = checked((int)(width * height * 4));
                var rgba = new byte[rgbaByteCount];
                if (!SteamUtils.GetImageRGBA(iconHandle, rgba, rgbaByteCount))
                {
                    return Task.FromResult<AchievementIcon>(null);
                }

                var icon = new AchievementIcon(
                    data: rgba,
                    contentType: "application/x-steam-rgba32",
                    cacheKey: $"steam:{achievementKey}:{iconHandle}",
                    width: (int)width,
                    height: (int)height);

                return Task.FromResult(icon);
            }
            catch
            {
                // Keep behavior resilient when Steam image APIs are unavailable.
                return Task.FromResult<AchievementIcon>(null);
            }
        }
    }
}
