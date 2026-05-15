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

        public static void Configure(
            string gameName,
            IGuideSignInProvider signInProvider = null,
            INetworkSessionFactory sessionFactory = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            IOSFallbackMode fallbackMode = IOSFallbackMode.PreferFallback)
        {
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                LeaderboardService.UsePersistentLocalStorage(gameName.Trim());
                AchievementService.UsePersistentLocalStorage(gameName.Trim());
            }

            if (achievementDefinitions != null)
            {
                AchievementCatalog.RegisterRange(achievementDefinitions);
            }

            Guide.SignInProvider = signInProvider ?? new IOSSignInProvider();
            NetworkServiceProvider.SetSessionFactory(sessionFactory ?? new IOSNetworkSessionFactory(fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new IOSLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new IOSAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new IOSAchievementMediaProvider());
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
            return true;
        }
    }
}