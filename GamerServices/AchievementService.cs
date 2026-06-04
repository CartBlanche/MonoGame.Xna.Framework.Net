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
        private static readonly IAchievementProvider syncAwareProvider = new SyncAwareAchievementProvider();

        /// <summary>
        /// Indicates whether this build should track pending remote synchronization
        /// for locally earned achievements.
        /// </summary>
        public static bool RemoteSyncEnabled { get; set; }

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
            get => syncAwareProvider;
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

        /// <summary>
        /// Unlocks the achievement using local-first persistence and attempts live sync when available.
        /// </summary>
        public static async Task UnlockWithSyncAsync(
            SignedInGamer gamer,
            string achievementKey,
            CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            cancellationToken.ThrowIfCancellationRequested();

            var local = LocalProvider;
            var localPersistent = local as PersistentLocalAchievementProvider;
            var shouldTrackPendingSync = RemoteSyncEnabled;

            if (localPersistent != null)
            {
                localPersistent.UnlockLocal(gamer, achievementKey, shouldTrackPendingSync);
            }
            else
            {
                await local.UnlockAsync(gamer, achievementKey, cancellationToken).ConfigureAwait(false);
            }

            if (!shouldTrackPendingSync)
                return;

            if (gamer.IsSignedInToLive && LiveProvider != null)
            {
                try
                {
                    await LiveProvider.UnlockAsync(gamer, achievementKey, cancellationToken).ConfigureAwait(false);
                    localPersistent?.MarkSynced(gamer, achievementKey);
                }
                catch (Exception ex)
                {
                    localPersistent?.MarkSyncFailed(gamer, achievementKey, ex.Message);
                }
            }
        }

        /// <summary>
        /// Retries all pending locally unlocked achievements against the active live provider.
        /// Returns the number of achievements synced successfully in this pass.
        /// </summary>
        public static async Task<int> ReconcilePendingUnlocksAsync(
            SignedInGamer gamer,
            CancellationToken cancellationToken = default)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            cancellationToken.ThrowIfCancellationRequested();

            if (!RemoteSyncEnabled || !gamer.IsSignedInToLive || LiveProvider == null)
                return 0;

            if (LocalProvider is not PersistentLocalAchievementProvider localPersistent)
                return 0;

            var pendingKeys = localPersistent.GetPendingSyncKeys(gamer);
            var syncedCount = 0;

            foreach (var key in pendingKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await LiveProvider.UnlockAsync(gamer, key, cancellationToken).ConfigureAwait(false);
                    localPersistent.MarkSynced(gamer, key);
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    localPersistent.MarkSyncFailed(gamer, key, ex.Message);
                }
            }

            return syncedCount;
        }

        private sealed class SyncAwareAchievementProvider : IAchievementProvider
        {
            public Task<AchievementCollection> GetAchievementsAsync(SignedInGamer gamer, CancellationToken cancellationToken = default)
            {
                return ResolveProvider(gamer).GetAchievementsAsync(gamer, cancellationToken);
            }

            public Task SetProgressAsync(
                SignedInGamer gamer,
                string achievementKey,
                float percentComplete,
                CancellationToken cancellationToken = default)
            {
                return ResolveProvider(gamer).SetProgressAsync(gamer, achievementKey, percentComplete, cancellationToken);
            }

            public Task UnlockAsync(SignedInGamer gamer, string achievementKey, CancellationToken cancellationToken = default)
            {
                return UnlockWithSyncAsync(gamer, achievementKey, cancellationToken);
            }
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
                AchievementProjection.UnlockState(state, AchievementSyncState.UnlockedSynced);
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
                var previousSyncState = state.SyncState;

                AchievementProjection.UnlockState(state, AchievementSyncState.UnlockedSynced);

                if (!wasEarned || previousSyncState != state.SyncState)
                {
                    SaveLocked();
                }
            }

            return Task.CompletedTask;
        }

        internal void UnlockLocal(SignedInGamer gamer, string achievementKey, bool markPendingSync)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            lock (gate)
            {
                EnsureLoaded();

                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                var wasEarned = state.IsEarned;
                var previousSyncState = state.SyncState;
                var desiredSyncState = markPendingSync
                    ? AchievementSyncState.UnlockedPendingSync
                    : AchievementSyncState.UnlockedSynced;

                AchievementProjection.UnlockState(state, desiredSyncState);

                if (!wasEarned || previousSyncState != state.SyncState)
                {
                    SaveLocked();
                }
            }
        }

        internal IReadOnlyList<string> GetPendingSyncKeys(SignedInGamer gamer)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            lock (gate)
            {
                EnsureLoaded();

                if (!gamerAchievements.TryGetValue(gamer.Gamertag, out var states))
                {
                    return Array.Empty<string>();
                }

                return states.Values
                    .Where(s => s.IsEarned &&
                        (s.SyncState == AchievementSyncState.UnlockedPendingSync ||
                         s.SyncState == AchievementSyncState.SyncFailedRetry))
                    .Select(s => s.Key)
                    .ToList();
            }
        }

        internal void MarkSynced(SignedInGamer gamer, string achievementKey)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            lock (gate)
            {
                EnsureLoaded();

                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                var changed = state.SyncState != AchievementSyncState.UnlockedSynced || state.LastSyncError != null;

                state.SyncState = AchievementSyncState.UnlockedSynced;
                state.LastSyncAttemptUtc = DateTime.UtcNow;
                state.LastSyncError = null;

                if (changed)
                {
                    SaveLocked();
                }
            }
        }

        internal void MarkSyncFailed(SignedInGamer gamer, string achievementKey, string errorMessage)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (string.IsNullOrWhiteSpace(achievementKey))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(achievementKey));

            lock (gate)
            {
                EnsureLoaded();

                var state = AchievementProjection.GetOrCreateStateLocked(gamerAchievements, gamer.Gamertag, achievementKey);
                var changed = state.SyncState != AchievementSyncState.SyncFailedRetry || !string.Equals(state.LastSyncError, errorMessage, StringComparison.Ordinal);

                state.SyncState = AchievementSyncState.SyncFailedRetry;
                state.LastSyncAttemptUtc = DateTime.UtcNow;
                state.LastSyncError = string.IsNullOrWhiteSpace(errorMessage) ? "sync_failed" : errorMessage;

                if (changed)
                {
                    SaveLocked();
                }
            }
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

                // Upgrade legacy rows that predate sync-state metadata.
                foreach (var gamerRow in gamerAchievements.Values)
                {
                    foreach (var state in gamerRow.Values)
                    {
                        if (state.IsEarned && state.SyncState == AchievementSyncState.Locked)
                        {
                            state.SyncState = AchievementSyncState.UnlockedSynced;
                        }
                    }
                }
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
        public AchievementSyncState SyncState { get; set; }
        public DateTime? LastSyncAttemptUtc { get; set; }
        public string LastSyncError { get; set; }
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
                    EarnedDate = null,
                    SyncState = AchievementSyncState.Locked,
                    LastSyncAttemptUtc = null,
                    LastSyncError = null,
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
                UnlockState(state, AchievementSyncState.UnlockedSynced);
            }
        }

        internal static void UnlockState(AchievementState state, AchievementSyncState syncState)
        {
            state.PercentComplete = 100f;
            state.IsEarned = true;
            state.EarnedDate ??= DateTime.UtcNow;
            state.SyncState = syncState;

            if (syncState == AchievementSyncState.UnlockedSynced)
            {
                state.LastSyncAttemptUtc = DateTime.UtcNow;
                state.LastSyncError = null;
            }
            else
            {
                state.LastSyncAttemptUtc = null;
            }
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
                iconUri: definition.IconUri,
                syncState: state?.SyncState);
        }
    }
}
