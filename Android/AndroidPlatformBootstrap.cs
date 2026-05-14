using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Composition-root helper for wiring Android backend into GamerServices + Net.
    /// </summary>
    public static class AndroidPlatformBootstrap
    {
        private static readonly object Gate = new();

        private static Func<ILeaderboardProvider> liveProviderFactory = () => new AndroidLeaderboardProvider();
        private static Func<IAchievementProvider> achievementLiveProviderFactory = () => new AndroidAchievementProvider();
        private static Func<IAchievementMediaProvider> achievementMediaLiveProviderFactory = () => new AndroidAchievementMediaProvider();

        public static void Configure(
            string gameName,
            IGuideSignInProvider signInProvider = null,
            INetworkSessionFactory sessionFactory = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            AndroidFallbackMode fallbackMode = AndroidFallbackMode.PreferFallback)
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

            Guide.SignInProvider = signInProvider ?? new AndroidSignInProvider();
            NetworkServiceProvider.SetSessionFactory(sessionFactory ?? new AndroidNetworkSessionFactory(fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new AndroidLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new AndroidAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new AndroidAchievementMediaProvider());
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
