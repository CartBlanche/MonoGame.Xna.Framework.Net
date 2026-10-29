using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Composition-root helper for wiring iOS backend into GamerServices + Net.
    /// </summary>
    public static class IOSPlatformBootstrap
    {
        private static readonly object Gate = new();

        private static Func<ILeaderboardProvider> liveProviderFactory = () => new IOSLeaderboardProvider();
        private static Func<IAchievementProvider> achievementLiveProviderFactory = () => new IOSAchievementProvider();
        private static Func<IAchievementMediaProvider> achievementMediaLiveProviderFactory = () => new IOSAchievementMediaProvider();

        /// <summary>
        /// Wires Game Center into GamerServices and Net: local persistence, sign-in provider and network back-end.
        /// </summary>
        /// <remarks>
        /// Call once at startup, before <see cref="IOSRuntime.Initialize"/>. This does not initialise the runtime or sign anyone in; call <see cref="IOSRuntime.Initialize"/> and then <see cref="TrySignInAndEnableLiveAsync"/>.
        /// </remarks>
        /// <param name="gameName">Names the local leaderboard and achievement storage. Null or blank leaves the storage unchanged.</param>
        /// <param name="signInProvider">Replaces the default <see cref="IOSSignInProvider"/>.</param>
        /// <param name="liveProviderFactoryOverride">Replaces the factory for the live leaderboard provider.</param>
        /// <param name="achievementLiveProviderFactoryOverride">Replaces the factory for the live achievement provider.</param>
        /// <param name="achievementMediaLiveProviderFactoryOverride">Replaces the factory for the live achievement media provider.</param>
        /// <param name="achievementDefinitions">Achievements to register in the <c>AchievementCatalog</c>.</param>
        /// <param name="fallbackMode">Fallback behaviour of the default <see cref="IOSBackend"/>; ignored when <paramref name="backend"/> is supplied.</param>
        /// <param name="backend">A network back-end to use instead of a new <see cref="IOSBackend"/>.</param>
        public static void Configure(
            string gameName,
            IGuideSignInProvider signInProvider = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback,
            INetworkBackend backend = null)
        {
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                LeaderboardService.UsePersistentLocalStorage(gameName.Trim());
                AchievementService.UsePersistentLocalStorage(gameName.Trim());
            }

            AchievementService.RemoteSyncEnabled = true;

            if (achievementDefinitions != null)
            {
                AchievementCatalog.RegisterRange(achievementDefinitions);
            }

            Guide.SignInProvider = signInProvider ?? new IOSSignInProvider();
            NetworkServiceProvider.SetBackend(backend ?? new IOSBackend(fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new IOSLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new IOSAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new IOSAchievementMediaProvider());
            }
        }

        /// <summary>
        /// Shows sign-in and, if the gamer ends up signed in, installs the live leaderboard, achievement and achievement media providers.
        /// </summary>
        /// <remarks>
        /// Needs <see cref="Configure"/> and <see cref="IOSRuntime.Initialize"/> first. If the gamer is not signed in the live providers are cleared and the result is <see langword="false"/>.
        /// </remarks>
        /// <returns><see langword="true"/> if the gamer is signed in and the live providers are enabled.</returns>
        public static async Task<bool> TrySignInAndEnableLiveAsync(
            int paneCount = 1,
            bool onlineOnly = false,
            CancellationToken cancellationToken = default)
        {
            await Guide.ShowSignInAsync(paneCount, onlineOnly, cancellationToken).ConfigureAwait(false);

            if (!SignedInGamer.Current.IsSignedInToLive)
            {
                LeaderboardService.LiveProvider = null;
                AchievementService.LiveProvider = null;
                AchievementMediaService.LiveProvider = null;
                return false;
            }

            Func<ILeaderboardProvider> providerFactory;
            Func<IAchievementProvider> achievementProviderFactory;
            Func<IAchievementMediaProvider> achievementMediaProviderFactory;

            lock (Gate)
            {
                providerFactory = liveProviderFactory;
                achievementProviderFactory = achievementLiveProviderFactory;
                achievementMediaProviderFactory = achievementMediaLiveProviderFactory;
            }

            LeaderboardService.LiveProvider = providerFactory();
            AchievementService.LiveProvider = achievementProviderFactory();
            AchievementMediaService.LiveProvider = achievementMediaProviderFactory();

            try
            {
                await AchievementService.ReconcilePendingUnlocksAsync(SignedInGamer.Current, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Reconciliation failures should not block sign-in completion.
            }

            return true;
        }
    }
}