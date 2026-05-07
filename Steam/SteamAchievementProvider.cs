using Microsoft.Xna.Framework.GamerServices;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// IAchievementProvider backed by Steam user stats/achievement APIs.
    /// Uses registered AchievementCatalog metadata for canonical display fields.
    /// </summary>
    public sealed class SteamAchievementProvider : IAchievementProvider
    {
        public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            cancellationToken.ThrowIfCancellationRequested();

            var definitions = BuildDefinitionSet();
            var rows = new List<Achievement>(definitions.Count);

            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var earned = TryGetAchievement(definition.Key, out var unlockTime);

                rows.Add(new Achievement(
                    key: definition.Key,
                    displayName: definition.DisplayName,
                    description: definition.Description,
                    howToEarn: definition.HowToEarn,
                    gamerScore: definition.GamerScore,
                    percentComplete: earned ? 100f : 0f,
                    isEarned: earned,
                    earnedDate: unlockTime,
                    isHidden: definition.IsHidden,
                    iconKey: definition.IconKey,
                    iconUri: definition.IconUri));
            }

            return Task.FromResult(new AchievementCollection(rows));
        }

        public Task SetProgressAsync(SignedInGamer gamer, string achievementKey, float percentComplete, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();
            percentComplete = Math.Clamp(percentComplete, 0f, 100f);

            if (percentComplete >= 100f)
            {
                return UnlockAsync(gamer, achievementKey, cancellationToken);
            }

            // Optional progress hint in Steam API; ignored if unavailable.
            TryIndicateProgress(achievementKey, (uint)Math.Max(1, Math.Round(percentComplete)), 100u);
            return Task.CompletedTask;
        }

        public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            TrySetAchievement(achievementKey);
            TryStoreStats();
            return Task.CompletedTask;
        }

        private static List<AchievementDefinition> BuildDefinitionSet()
        {
            var catalog = AchievementCatalog.GetAll();
            if (catalog.Count > 0)
                return catalog.ToList();

            return ReadDefinitionsFromSteam();
        }

        private static List<AchievementDefinition> ReadDefinitionsFromSteam()
        {
            var output = new List<AchievementDefinition>();

            var count = TryGetAchievementCount();
            for (uint i = 0; i < count; i++)
            {
                var key = TryGetAchievementName(i);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var displayName = TryGetDisplayAttribute(key, "name");
                var description = TryGetDisplayAttribute(key, "desc");
                var hidden = string.Equals(TryGetDisplayAttribute(key, "hidden"), "1", StringComparison.Ordinal);

                output.Add(new AchievementDefinition(
                    key: key,
                    displayName: string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                    description: description ?? string.Empty,
                    howToEarn: string.Empty,
                    gamerScore: 0,
                    isHidden: hidden));
            }

            return output;
        }

        private static uint TryGetAchievementCount()
        {
            try
            {
                return SteamUserStats.GetNumAchievements();
            }
            catch
            {
                return 0u;
            }
        }

        private static string TryGetAchievementName(uint index)
        {
            try
            {
                return SteamUserStats.GetAchievementName(index);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetDisplayAttribute(string key, string attribute)
        {
            try
            {
                return SteamUserStats.GetAchievementDisplayAttribute(key, attribute);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetAchievement(string key, out DateTime? unlockTime)
        {
            unlockTime = null;

            try
            {
                if (SteamUserStats.GetAchievementAndUnlockTime(key, out var earned, out var unixUnlockTime))
                {
                    if (earned && unixUnlockTime > 0)
                    {
                        unlockTime = DateTimeOffset.FromUnixTimeSeconds(unixUnlockTime).UtcDateTime;
                    }

                    return earned;
                }

                if (SteamUserStats.GetAchievement(key, out earned))
                {
                    return earned;
                }
            }
            catch
            {
                // Best-effort read only.
            }

            return false;
        }

        private static void TryIndicateProgress(string key, uint current, uint max)
        {
            try
            {
                SteamUserStats.IndicateAchievementProgress(key, current, max);
            }
            catch
            {
                // Optional capability.
            }
        }

        private static void TrySetAchievement(string key)
        {
            try
            {
                SteamUserStats.SetAchievement(key);
            }
            catch
            {
                // Best-effort unlock.
            }
        }

        private static void TryStoreStats()
        {
            try
            {
                SteamUserStats.StoreStats();
            }
            catch
            {
                // Best-effort persist.
            }
        }
    }
}
