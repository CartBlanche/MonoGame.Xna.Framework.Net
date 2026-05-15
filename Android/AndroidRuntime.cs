using Android.Gms.Games;
using Android.Gms.Extensions;

using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Runtime facade for Android Play Games services. On Android this wraps native
    /// Play Games Services v2; on non-Android build targets it operates on locally
    /// supplied identity only (used by desktop test projects).
    /// </summary>
    public static class AndroidRuntime
    {
        private static readonly object Gate = new();

        private static bool isInitialized;
        private static string playerId;
        private static string gamertag;
        private static Activity activity;
        private static IGooglePlayGamesClient testClient;

        /// <summary>
        /// Initializes the Android runtime. Must be called from <c>Activity.OnCreate</c>
        /// before any other method. Automatically calls <see cref="PlayGamesSdk.Initialize"/>.
        /// </summary>
        public static bool Initialize(
            Activity androidActivity,
            string initialPlayerId = null,
            string initialGamertag = null)
        {
            lock (Gate)
            {
                activity = androidActivity;
                isInitialized = true;
                playerId = initialPlayerId ?? playerId;
                gamertag = initialGamertag ?? gamertag;
            }

            if (androidActivity != null)
            {
                try
                {
                    PlayGamesSdk.Initialize(androidActivity);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[AndroidRuntime] PlayGamesSdk.Initialize failed: {ex.Message}");
                }
            }

            RefreshSignedInGamerIdentity();
            return true;
        }

        public static bool IsInitialized
        {
            get { lock (Gate) { return isInitialized; } }
        }

        internal static bool TryGetGooglePlayGamesClient(out IGooglePlayGamesClient client)
        {
            lock (Gate)
            {
                if (testClient != null)
                {
                    client = testClient;
                    return true;
                }

                if (isInitialized && activity != null)
                {
                    client = new AndroidPlayGamesClient(activity);
                    return true;
                }
                client = null;
                return false;
            }
        }

        internal static void SetGooglePlayGamesClient(IGooglePlayGamesClient testClient)
        {
            lock (Gate)
            {
                AndroidRuntime.testClient = testClient;
            }
        }

        public static async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInitialized)
            {
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }

            Activity currentActivity;
            lock (Gate) { currentActivity = activity; }

            if (currentActivity != null)
            {
                try
                {
                    var signInClient = PlayGames.GetGamesSignInClient(currentActivity);

                    var authResult = await signInClient.IsAuthenticated()
                        .AsAsync<AuthenticationResult>()
                        .ConfigureAwait(false);

                    if (!authResult.IsAuthenticated)
                    {
                        authResult = await signInClient.SignIn()
                            .AsAsync<AuthenticationResult>()
                            .ConfigureAwait(false);
                    }

                    if (authResult.IsAuthenticated)
                    {
                        SignedInGamer.Current.SetSignedInToLive(true);
                        return true;
                    }

                    SignedInGamer.Current.SetSignedInToLive(false);
                    return false;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[AndroidRuntime] SignIn failed: {ex.Message}");
                    SignedInGamer.Current.SetSignedInToLive(false);
                    return false;
                }
            }
            return false;
        }

        public static void SetSignedInIdentity(string newPlayerId, string newGamertag)
        {
            lock (Gate)
            {
                playerId = newPlayerId;
                gamertag = newGamertag;
                isInitialized = true;
            }

            RefreshSignedInGamerIdentity();
        }

        public static void RunCallbacks()
        {
            // Reserved for future SDK callback/event pumping.
        }

        internal static bool RefreshSignedInGamerIdentity()
        {
            lock (Gate)
            {
                if (!isInitialized)
                {
                    SignedInGamer.Current.SetSignedInToLive(false);
                    return false;
                }

                var hasIdentity = !string.IsNullOrWhiteSpace(gamertag) || !string.IsNullOrWhiteSpace(playerId);
                SignedInGamer.Current.SetSignedInToLive(hasIdentity);

                if (!hasIdentity)
                    return false;

                var resolvedGamertag = !string.IsNullOrWhiteSpace(gamertag)
                    ? gamertag
                    : $"AndroidPlayer_{playerId}";

                SignedInGamer.Current.SetGamertag(resolvedGamertag);
                return true;
            }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                isInitialized = false;
                playerId = null;
                gamertag = null;
                activity = null;
                testClient = null;
            }

            SignedInGamer.Current.SetSignedInToLive(false);
        }
    }
}
