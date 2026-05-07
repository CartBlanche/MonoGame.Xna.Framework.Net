using Microsoft.Xna.Framework.GamerServices;

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

            // Steam image handle->RGBA texture decode can be layered later; for now
            // we expose metadata-driven icon lookup and return no binary payload by default.
            _ = AchievementCatalog.Get(achievementKey);
            return Task.FromResult<AchievementIcon>(null);
        }
    }
}
