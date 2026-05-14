using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Achievement provider for Android backend.
    /// Uses process-local shared state in the initial vertical slice.
    /// </summary>
    public sealed class AndroidAchievementProvider : IAchievementProvider
    {
        private sealed class AchievementState
        {
            public float PercentComplete;
            public bool IsEarned;
            public DateTime? EarnedDate;
        }

        private static readonly object Gate = new();
        private static readonly Dictionary<string, Dictionary<string, AchievementState>> GamerAchievements = new(StringComparer.Ordinal);

        public async Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            cancellationToken.ThrowIfCancellationRequested();

            var definitions = BuildDefinitionSet();
            var gamerKey = ResolveGamerKey(gamer);
            var rows = new List<Achievement>(definitions.Count);
            IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress> liveProgress = null;

            if (AndroidRuntime.TryGetGooglePlayGamesClient(out var client))
            {
                try
                {
                    liveProgress = await client.GetAchievementProgressAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Fall back to local projection if live call fails.
                }
            }

            lock (Gate)
            {
                GamerAchievements.TryGetValue(gamerKey, out var states);

                foreach (var definition in definitions)
                {
                    if (liveProgress != null && liveProgress.TryGetValue(definition.Key, out var remote))
                    {
                        rows.Add(new Achievement(
                            key: definition.Key,
                            displayName: definition.DisplayName,
                            description: definition.Description,
                            howToEarn: definition.HowToEarn,
                            gamerScore: definition.GamerScore,
                            percentComplete: remote.PercentComplete,
                            isEarned: remote.IsUnlocked,
                            earnedDate: remote.LastUpdatedUtc,
                            isHidden: definition.IsHidden,
                            iconKey: definition.IconKey,
                            iconUri: string.IsNullOrWhiteSpace(definition.IconUri) ? remote.IconUrl : definition.IconUri));
                        continue;
                    }

                    AchievementState state = null;
                    if (states != null)
                    {
                        states.TryGetValue(definition.Key, out state);
                    }

                    var percentComplete = state?.PercentComplete ?? 0f;
                    var isEarned = state?.IsEarned ?? false;

                    rows.Add(new Achievement(
                        key: definition.Key,
                        displayName: definition.DisplayName,
                        description: definition.Description,
                        howToEarn: definition.HowToEarn,
                        gamerScore: definition.GamerScore,
                        percentComplete: percentComplete,
                        isEarned: isEarned,
                        earnedDate: state?.EarnedDate,
                        isHidden: definition.IsHidden,
                        iconKey: definition.IconKey,
                        iconUri: definition.IconUri));
                }
            }

            return new AchievementCollection(rows);
        }

        public async Task SetProgressAsync(SignedInGamer gamer, string achievementKey, float percentComplete, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();
            percentComplete = Math.Clamp(percentComplete, 0f, 100f);

            if (AndroidRuntime.TryGetGooglePlayGamesClient(out var client))
            {
                try
                {
                    if (percentComplete >= 100f)
                    {
                        await client.UnlockAchievementAsync(achievementKey, cancellationToken).ConfigureAwait(false);
                    }
                    else if (percentComplete > 0f)
                    {
                        await client.IncrementAchievementAsync(achievementKey, 1, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Fall back to local tracking if live call fails.
                }
            }

            lock (Gate)
            {
                var state = GetOrCreateStateLocked(ResolveGamerKey(gamer), achievementKey);
                if (state.IsEarned)
                {
                    return;
                }

                state.PercentComplete = Math.Max(state.PercentComplete, percentComplete);
                if (state.PercentComplete >= 100f)
                {
                    state.PercentComplete = 100f;
                    state.IsEarned = true;
                    state.EarnedDate = DateTime.UtcNow;
                }
            }
        }

        public async Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            if (AndroidRuntime.TryGetGooglePlayGamesClient(out var client))
            {
                try
                {
                    await client.UnlockAchievementAsync(achievementKey, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Fall back to local tracking if live call fails.
                }
            }

            lock (Gate)
            {
                var state = GetOrCreateStateLocked(ResolveGamerKey(gamer), achievementKey);
                if (!state.IsEarned)
                {
                    state.IsEarned = true;
                    state.PercentComplete = 100f;
                    state.EarnedDate = DateTime.UtcNow;
                }
            }
        }

        private static AchievementState GetOrCreateStateLocked(string gamerKey, string achievementKey)
        {
            if (!GamerAchievements.TryGetValue(gamerKey, out var states))
            {
                states = new Dictionary<string, AchievementState>(StringComparer.Ordinal);
                GamerAchievements[gamerKey] = states;
            }

            if (!states.TryGetValue(achievementKey, out var state))
            {
                state = new AchievementState();
                states[achievementKey] = state;
            }

            return state;
        }

        private static List<AchievementDefinition> BuildDefinitionSet()
        {
            var catalog = AchievementCatalog.GetAll();
            if (catalog.Count > 0)
            {
                return catalog.ToList();
            }

            return new List<AchievementDefinition>();
        }

        private static string ResolveGamerKey(SignedInGamer gamer)
        {
            return string.IsNullOrWhiteSpace(gamer?.Gamertag) ? "Player" : gamer.Gamertag;
        }
    }
}
