namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Contract for achievement backends.
    /// </summary>
    public interface IAchievementProvider
    {
        Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default);
        Task SetProgressAsync(SignedInGamer gamer, string achievementKey, float percentComplete, CancellationToken cancellationToken = default);
        Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Global achievement provider hook used by GamerServices APIs.
    /// </summary>
    public static class AchievementService
    {
        private const string DefaultLocalStorageFolder = "MonoGame.Xna.Framework.Net";

        private static IAchievementProvider liveProvider;
        private static IAchievementProvider localProvider = new PersistentLocalAchievementProvider();

        /// <summary>
        /// Gets or sets the online/live provider used when a gamer is signed in.
        /// </summary>
        public static IAchievementProvider LiveProvider
        {
            get => liveProvider;
            set => liveProvider = value;
        }

        /// <summary>
        /// Gets or sets the local fallback provider used when a gamer is not signed in.
        /// </summary>
        public static IAchievementProvider LocalProvider
        {
            get => localProvider;
            set => localProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Backward-compatible alias for the live provider.
        /// </summary>
        public static IAchievementProvider Provider
        {
            get => LiveProvider ?? LocalProvider;
            set => LiveProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Configures local persistent achievement storage for a specific game folder.
        /// </summary>
        public static void UsePersistentLocalStorage(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                throw new ArgumentException("Game name cannot be empty.", nameof(gameName));

            LocalProvider = PersistentLocalAchievementProvider.CreateForGame(gameName.Trim());
        }

        internal static string LocalStorageFolderName => DefaultLocalStorageFolder;

        internal static IAchievementProvider ResolveProvider(SignedInGamer gamer)
        {
            if (gamer != null && gamer.IsSignedInToLive && LiveProvider != null)
                return LiveProvider;

            return LocalProvider;
        }
    }

    internal sealed class InMemoryAchievementProvider : IAchievementProvider
    {
        private readonly object gate = new();
        private readonly Dictionary<string, Dictionary<string, AchievementState>> gamerAchievements = new(StringComparer.Ordinal);

        public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                if (!gamerAchievements.TryGetValue(gamer.Gamertag, out var states))
                {
                    states = new Dictionary<string, AchievementState>(StringComparer.Ordinal);
                }

                var rows = AchievementProjection.BuildAchievements(states);
                return Task.FromResult(new AchievementCollection(rows));
            }
        }

        public Task SetProgressAsync(
            SignedInGamer gamer,
            string achievementKey,
            float percentComplete,
            CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();
            percentComplete = Math.Clamp(percentComplete, 0f, 100f);

            lock (gate)
            {
                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                AchievementProjection.ApplyProgress(state, percentComplete);
            }

            return Task.CompletedTask;
        }

        public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                AchievementProjection.UnlockState(state);
            }

            return Task.CompletedTask;
        }
    }

    internal sealed class PersistentLocalAchievementProvider : IAchievementProvider
    {
        private sealed class StorageModel
        {
            public Dictionary<string, List<AchievementState>> GamerAchievements { get; set; } = new(StringComparer.Ordinal);
        }

        private readonly object gate = new();
        private readonly string storagePath;
        private bool isLoaded;
        private Dictionary<string, Dictionary<string, AchievementState>> gamerAchievements = new(StringComparer.Ordinal);

        internal string StoragePath => storagePath;

        public PersistentLocalAchievementProvider()
            : this(GetDefaultStoragePath(AchievementService.LocalStorageFolderName))
        {
        }

        internal static PersistentLocalAchievementProvider CreateForGame(string gameName)
        {
            return new PersistentLocalAchievementProvider(GetDefaultStoragePath(gameName));
        }

        internal PersistentLocalAchievementProvider(string storagePath)
        {
            if (string.IsNullOrWhiteSpace(storagePath))
                throw new ArgumentException("Storage path cannot be empty.", nameof(storagePath));

            this.storagePath = storagePath;
        }

        public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                EnsureLoaded();

                if (!gamerAchievements.TryGetValue(gamer.Gamertag, out var states))
                {
                    states = new Dictionary<string, AchievementState>(StringComparer.Ordinal);
                }

                var rows = AchievementProjection.BuildAchievements(states);
                return Task.FromResult(new AchievementCollection(rows));
            }
        }

        public Task SetProgressAsync(
            SignedInGamer gamer,
            string achievementKey,
            float percentComplete,
            CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();
            percentComplete = Math.Clamp(percentComplete, 0f, 100f);

            lock (gate)
            {
                EnsureLoaded();

                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                var wasEarned = state.IsEarned;
                var previousProgress = state.PercentComplete;

                AchievementProjection.ApplyProgress(state, percentComplete);

                if (state.IsEarned != wasEarned || Math.Abs(state.PercentComplete - previousProgress) > 0.0001f)
                {
                    SaveLocked();
                }
            }

            return Task.CompletedTask;
        }

        public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                EnsureLoaded();

                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                var wasEarned = state.IsEarned;

                AchievementProjection.UnlockState(state);

                if (!wasEarned)
                {
                    SaveLocked();
                }
            }

            return Task.CompletedTask;
        }

        private static string GetDefaultStoragePath(string appFolderName)
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = AppContext.BaseDirectory;

            if (OperatingSystem.IsAndroid())
                root = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            else if (OperatingSystem.IsIOS())
                root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(root, appFolderName, "achievements.json");
        }

        private void EnsureLoaded()
        {
            if (isLoaded)
                return;

            isLoaded = true;

            if (!File.Exists(storagePath))
                return;

            try
            {
                var json = File.ReadAllText(storagePath);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var model = System.Text.Json.JsonSerializer.Deserialize<StorageModel>(json);
                if (model?.GamerAchievements == null)
                    return;

                gamerAchievements = model.GamerAchievements.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToDictionary(s => s.Key, s => s, StringComparer.Ordinal),
                    StringComparer.Ordinal);
            }
            catch
            {
                // Corrupt or unreadable local cache should not block gameplay.
                gamerAchievements = new Dictionary<string, Dictionary<string, AchievementState>>(StringComparer.Ordinal);
            }
        }

        private void SaveLocked()
        {
            var dir = Path.GetDirectoryName(storagePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var model = new StorageModel
            {
                GamerAchievements = gamerAchievements.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Values.ToList(),
                    StringComparer.Ordinal)
            };

            var json = System.Text.Json.JsonSerializer.Serialize(model);
            File.WriteAllText(storagePath, json);
        }
    }

    internal sealed class AchievementState
    {
        public string Key { get; set; } = string.Empty;
        public float PercentComplete { get; set; }
        public bool IsEarned { get; set; }
        public DateTime? EarnedDate { get; set; }
    }

    internal static class AchievementProjection
    {
        internal static List<Achievement> BuildAchievements(Dictionary<string, AchievementState> states)
        {
            var byKey = new Dictionary<string, Achievement>(StringComparer.Ordinal);

            foreach (var definition in AchievementCatalog.GetAll())
            {
                states.TryGetValue(definition.Key, out var state);
                byKey[definition.Key] = ToAchievement(definition, state);
            }

            foreach (var state in states.Values)
            {
                if (byKey.ContainsKey(state.Key))
                    continue;

                var fallback = new AchievementDefinition(state.Key, state.Key);
                byKey[state.Key] = ToAchievement(fallback, state);
            }

            return byKey.Values
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .ToList();
        }

        internal static AchievementState GetOrCreateStateLocked(
            Dictionary<string, Dictionary<string, AchievementState>> gamerAchievements,
            string gamerTag,
            string key)
        {
            if (!gamerAchievements.TryGetValue(gamerTag, out var states))
            {
                states = new Dictionary<string, AchievementState>(StringComparer.Ordinal);
                gamerAchievements[gamerTag] = states;
            }

            if (!states.TryGetValue(key, out var state))
            {
                state = new AchievementState
                {
                    Key = key,
                    PercentComplete = 0f,
                    IsEarned = false,
                    EarnedDate = null
                };
                states[key] = state;
            }

            return state;
        }

        internal static void ApplyProgress(AchievementState state, float percentComplete)
        {
            if (state.IsEarned)
                return;

            if (percentComplete > state.PercentComplete)
            {
                state.PercentComplete = percentComplete;
            }

            if (state.PercentComplete >= 100f)
            {
                UnlockState(state);
            }
        }

        internal static void UnlockState(AchievementState state)
        {
            state.PercentComplete = 100f;
            state.IsEarned = true;
            state.EarnedDate ??= DateTime.UtcNow;
        }

        private static Achievement ToAchievement(AchievementDefinition definition, AchievementState state)
        {
            return new Achievement(
                key: definition.Key,
                displayName: definition.DisplayName,
                description: definition.Description,
                howToEarn: definition.HowToEarn,
                gamerScore: definition.GamerScore,
                percentComplete: state?.PercentComplete ?? 0f,
                isEarned: state?.IsEarned ?? false,
                earnedDate: state?.EarnedDate,
                isHidden: definition.IsHidden,
                iconKey: definition.IconKey,
                iconUri: definition.IconUri);
        }
    }
}
