using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// Composition-root helper for wiring the Steam backend into GamerServices + Net.
    /// </summary>
    public static class SteamPlatformBootstrap
    {
        private static readonly object Gate = new();
        private static Func<ILeaderboardProvider> liveProviderFactory = () => new SteamLeaderboardProvider();
        private static Func<IAchievementProvider> achievementLiveProviderFactory = () => new SteamAchievementProvider();
        private static Func<IAchievementMediaProvider> achievementMediaLiveProviderFactory = () => new SteamAchievementMediaProvider();

        /// <summary>
        /// Configures Steam-backed services for networking and guide sign-in.
        /// Call once during app startup.
        /// </summary>
        public static void Configure(
            string gameName,
            IGuideSignInProvider signInProvider = null,
            INetworkSessionFactory sessionFactory = null,
            Func<ILeaderboardProvider> liveProviderFactoryOverride = null,
            Func<IAchievementProvider> achievementLiveProviderFactoryOverride = null,
            Func<IAchievementMediaProvider> achievementMediaLiveProviderFactoryOverride = null,
            IEnumerable<AchievementDefinition> achievementDefinitions = null,
            SteamFallbackMode fallbackMode = SteamFallbackMode.PreferFallback)
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

            Guide.SignInProvider = signInProvider ?? new SteamSignInProvider();
            NetworkServiceProvider.SetSessionFactory(sessionFactory ?? new SteamNetworkSessionFactory(gameName, fallbackMode));

            lock (Gate)
            {
                liveProviderFactory = liveProviderFactoryOverride ?? (() => new SteamLeaderboardProvider());
                achievementLiveProviderFactory = achievementLiveProviderFactoryOverride ?? (() => new SteamAchievementProvider());
                achievementMediaLiveProviderFactory = achievementMediaLiveProviderFactoryOverride ?? (() => new SteamAchievementMediaProvider());
            }
        }

        /// <summary>
        /// Runs Guide sign-in and, on success, enables the Steam live leaderboard provider.
        /// </summary>
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
