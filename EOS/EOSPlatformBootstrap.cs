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

        public static void Configure(
            string gameName,
            IGuideSignInProvider signInProvider = null,
            INetworkSessionFactory sessionFactory = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            EOSFallbackMode fallbackMode = EOSFallbackMode.PreferFallback)
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

            Guide.SignInProvider = signInProvider ?? new EOSSignInProvider();
            NetworkServiceProvider.SetSessionFactory(sessionFactory ?? new EOSNetworkSessionFactory(fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new EOSLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new EOSAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new EOSAchievementMediaProvider());
            }
        }

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
