using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Runtime facade for iOS Game Center integration.
    /// In this vertical slice, live Game Center calls are optional and routed via
    /// an injected <see cref="IAppleGameCenterClient"/>.
    /// </summary>
    public static class IOSRuntime
    {
        private static readonly object Gate = new();

        private static bool isInitialized;
        private static string playerId;
        private static string gamertag;
        private static IAppleGameCenterClient gameCenterClient;

        public static bool Initialize(string initialPlayerId = null, string initialGamertag = null)
        {
            lock (Gate)
            {
                isInitialized = true;
                playerId = initialPlayerId ?? playerId;
                gamertag = initialGamertag ?? gamertag;

                if (gameCenterClient == null)
                {
                    gameCenterClient = new IOSGameCenterClient();
                }
            }

            RefreshSignedInGamerIdentity();
            return true;
        }

        public static bool IsInitialized
        {
            get { lock (Gate) { return isInitialized; } }
        }

        internal static bool TryGetAppleGameCenterClient(out IAppleGameCenterClient client)
        {
            lock (Gate)
            {
                if (isInitialized && gameCenterClient != null)
                {
                    client = gameCenterClient;
                    return true;
                }

                client = null;
                return false;
            }
        }

        internal static void SetAppleGameCenterClient(IAppleGameCenterClient testClient)
        {
            lock (Gate)
            {
                gameCenterClient = testClient;
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

            if (TryGetAppleGameCenterClient(out var client))
            {
                try
                {
                    var player = await client.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                    if (player != null && (!string.IsNullOrWhiteSpace(player.Id) || !string.IsNullOrWhiteSpace(player.DisplayName)))
                    {
                        lock (Gate)
                        {
                            playerId = player.Id;
                            gamertag = player.DisplayName;
                        }

                        return RefreshSignedInGamerIdentity();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[IOSRuntime] SignIn failed: {ex.Message}");
                }
            }

            return RefreshSignedInGamerIdentity();
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
            // Reserved for future callback/event pumping.
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
                {
                    return false;
                }

                var resolvedGamertag = !string.IsNullOrWhiteSpace(gamertag)
                    ? gamertag
                    : $"IOSPlayer_{playerId}";

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
                gameCenterClient = null;
            }

            SignedInGamer.Current.SetSignedInToLive(false);
        }
    }
}