using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Composition-root helper for wiring EOS backend into GamerServices + Net.
    /// </summary>
    public static class EOSPlatformBootstrap
    {
        private static readonly object Gate = new();

        private static Func<ILeaderboardProvider> liveProviderFactory = () => new EOSLeaderboardProvider();
        private static Func<IAchievementProvider> achievementLiveProviderFactory = () => new EOSAchievementProvider();
        private static Func<IAchievementMediaProvider> achievementMediaLiveProviderFactory = () => new EOSAchievementMediaProvider();

        /// <summary>
        /// Wires EOS into GamerServices and Net: credentials, local persistence, sign-in provider and network back-end.
        /// </summary>
        /// <remarks>
        /// Call once at startup, before <see cref="EOSRuntime.Initialize"/>. This does not initialise EOS or sign anyone in; call <see cref="EOSRuntime.Initialize"/> and then <see cref="TrySignInAndEnableLiveAsync"/>.
        /// </remarks>
        /// <param name="gameName">Names the local leaderboard and achievement storage. Null or blank leaves the storage unchanged.</param>
        /// <param name="credentials">EOS credentials. Null leaves them to the <c>MGNET_EOS_*</c> environment variables.</param>
        /// <param name="signInProvider">Replaces the default <see cref="EOSSignInProvider"/>.</param>
        /// <param name="liveProviderFactoryOverride">Replaces the factory for the live leaderboard provider.</param>
        /// <param name="achievementLiveProviderFactoryOverride">Replaces the factory for the live achievement provider.</param>
        /// <param name="achievementMediaLiveProviderFactoryOverride">Replaces the factory for the live achievement media provider.</param>
        /// <param name="achievementDefinitions">Achievements to register in the <c>AchievementCatalog</c>.</param>
        /// <param name="fallbackMode">Fallback behaviour of the default <see cref="EOSBackend"/>; ignored when <paramref name="backend"/> is supplied.</param>
        /// <param name="backend">A network back-end to use instead of a new <see cref="EOSBackend"/>.</param>
        public static void Configure(
            string gameName,
            EOSCredentials credentials = null,
            IGuideSignInProvider signInProvider = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            EOSFallbackMode fallbackMode = EOSFallbackMode.PreferFallback,
            INetworkBackend backend = null)
        {
            if (credentials != null)
            {
                EOSRuntime.SetCredentials(credentials);
            }

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

            Guide.SignInProvider = signInProvider ?? new EOSSignInProvider();
            NetworkServiceProvider.SetBackend(backend ?? new EOSBackend(fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new EOSLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new EOSAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new EOSAchievementMediaProvider());
            }
        }

        /// <summary>
        /// Shows sign-in and, if the gamer ends up signed in, installs the live leaderboard, achievement and achievement media providers.
        /// </summary>
        /// <remarks>
        /// Needs <see cref="Configure"/> and <see cref="EOSRuntime.Initialize"/> first. If the gamer is not signed in the live providers are cleared and the result is <see langword="false"/>. Failures while reconciling pending achievement unlocks are ignored.
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
