using Android.Gms.Common;
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
                    System.Diagnostics.Debug.WriteLine($"[AndroidRuntime] PlayGamesSdk.Initialize failed: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine("[AndroidRuntime] SignInAsync called before Initialize.");
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }

            Activity currentActivity;
            lock (Gate) { currentActivity = activity; }

            if (currentActivity == null)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidRuntime] SignInAsync: activity is null. Call Initialize(activity, ...) first.");
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }

            var playServicesStatus = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(currentActivity);
            if (playServicesStatus != ConnectionResult.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidRuntime] Google Play Services not available (error code: {playServicesStatus}). " +
                    "Ensure device has an up-to-date version of Google Play Services.");
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }

            try
            {
                var signInClient = PlayGames.GetGamesSignInClient(currentActivity);

                var authResult = await signInClient.IsAuthenticated()
                    .AsAsync<AuthenticationResult>()
                    .ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine($"[AndroidRuntime] IsAuthenticated check: {authResult?.IsAuthenticated}");

                if (authResult == null || !authResult.IsAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine("[AndroidRuntime] Not authenticated, calling SignIn()...");
                    authResult = await signInClient.SignIn()
                        .AsAsync<AuthenticationResult>()
                        .ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[AndroidRuntime] SignIn() result: IsAuthenticated={authResult?.IsAuthenticated}");
                }

                if (authResult != null && authResult.IsAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine("[AndroidRuntime] Sign-in succeeded.");
                    SignedInGamer.Current.SetSignedInToLive(true);
                    RefreshSignedInGamerIdentity();
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("[AndroidRuntime] SignIn completed but IsAuthenticated=false. " +
                    "Check Play Games developer console: app not published to testers, " +
                    "SHA-1 fingerprint mismatch, or package name mismatch.");
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidRuntime] SignIn threw: {ex.GetType().Name}: {ex.Message}");
                SignedInGamer.Current.SetSignedInToLive(false);
                return false;
            }
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
