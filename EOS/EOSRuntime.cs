using Microsoft.Xna.Framework.GamerServices;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Runtime facade for EOS integration.
    /// </summary>
    public static class EOSRuntime
    {
        private static readonly object Gate = new();

        private static bool isInitialized;
        private static string playerId;
        private static string gamertag;
        private static IEpicOnlineServicesClient eosClient;

        public static bool Initialize(string initialPlayerId = null, string initialGamertag = null)
        {
            lock (Gate)
            {
                isInitialized = true;
                playerId = initialPlayerId ?? playerId;
                gamertag = initialGamertag ?? gamertag;

                if (eosClient == null)
                {
                    eosClient = new EOSClient();
                }
            }

            RefreshSignedInGamerIdentity();
            return true;
        }

        public static bool IsInitialized
        {
            get { lock (Gate) { return isInitialized; } }
        }

        internal static bool TryGetEpicOnlineServicesClient(out IEpicOnlineServicesClient client)
        {
            lock (Gate)
            {
                if (isInitialized && eosClient != null)
                {
                    client = eosClient;
                    return true;
                }

                client = null;
                return false;
            }
        }

        internal static void SetEpicOnlineServicesClient(IEpicOnlineServicesClient testClient)
        {
            lock (Gate)
            {
                eosClient = testClient;
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

            if (TryGetEpicOnlineServicesClient(out var client))
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
                    Debug.WriteLine($"[EOSRuntime] SignIn failed: {ex.Message}");
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
            // Reserved for future EOS callback/event pumping.
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
                    : $"EOSPlayer_{playerId}";

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
                eosClient = null;
            }

            SignedInGamer.Current.SetSignedInToLive(false);
        }
    }
}
