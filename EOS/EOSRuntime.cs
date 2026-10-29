using Epic.OnlineServices;
using Microsoft.Xna.Framework.GamerServices;
using Platform = Epic.OnlineServices.Platform;
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
        private static EOSCredentials credentials;

        internal static void SetCredentials(EOSCredentials runtimeCredentials)
        {
            lock (Gate)
            {
                credentials = runtimeCredentials;
            }
        }

        /// <summary>
        /// Marks the runtime initialized and creates the EOS client from the credentials given to <see cref="EOSPlatformBootstrap.Configure"/> (or the <c>MGNET_EOS_*</c> environment variables).
        /// </summary>
        /// <remarks>
        /// The EOS SDK itself is brought up lazily by the client, not here, so this always returns <see langword="true"/> and does not prove the credentials are valid. Call <see cref="SignInAsync"/> afterwards to authenticate.
        /// </remarks>
        /// <param name="initialPlayerId">Optional player id to start with.</param>
        /// <param name="initialGamertag">Optional gamertag to start with.</param>
        /// <returns>Always <see langword="true"/>.</returns>
        public static bool Initialize(string initialPlayerId = null, string initialGamertag = null)
        {
            lock (Gate)
            {
                isInitialized = true;
                playerId = initialPlayerId ?? playerId;
                gamertag = initialGamertag ?? gamertag;

                if (eosClient == null)
                {
                    eosClient = new EOSClient(credentials);
                }
            }

            RefreshSignedInGamerIdentity();
            return true;
        }

        /// <summary>Whether <see cref="Initialize"/> (or <see cref="SetSignedInIdentity"/>) has been called and <see cref="Shutdown"/> has not since.</summary>
        public static bool IsInitialized
        {
            get { lock (Gate) { return isInitialized; } }
        }

        internal static Platform.PlatformInterface TryGetPlatform()
            => (TryGetEpicOnlineServicesClient(out var c) && c is EOSClient) ? EOSClient.TryGetPlatform() : null;

        internal static ProductUserId TryGetCurrentProductUserId()
            => (TryGetEpicOnlineServicesClient(out var c) && c is EOSClient) ? EOSClient.TryGetCurrentProductUserId() : null;

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
                if (!ReferenceEquals(eosClient, testClient) && eosClient is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                eosClient = testClient;
            }
        }

        /// <summary>
        /// Authenticates with EOS and updates <c>SignedInGamer.Current</c>.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="false"/> when <see cref="Initialize"/> has not been called. Authentication failures are logged, not thrown; the result then reflects whatever identity is already known.
        /// </remarks>
        /// <returns><see langword="true"/> if the gamer is now signed in.</returns>
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
                    NetworkLogging.Logger.LogWarning($"[EOSRuntime] SignIn failed: {ex.Message}");
                }
            }

            return RefreshSignedInGamerIdentity();
        }

        /// <summary>
        /// Sets the player id and gamertag directly and marks the runtime initialized, without contacting EOS.
        /// </summary>
        /// <remarks>
        /// Intended for supplying an identity from outside (for example tests); it creates no EOS client, so the EOS back-end does not become ready.
        /// </remarks>
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

        /// <summary>
        /// Pumps EOS platform callbacks. Call it regularly (every frame) when a session is not receiving; does nothing before <see cref="Initialize"/>.
        /// </summary>
        public static void RunCallbacks()
        {
            if (TryGetEpicOnlineServicesClient(out var client) && client is EOSClient eosSdkClient)
            {
                eosSdkClient.PumpCallbacks();
            }
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

        /// <summary>
        /// Disposes the EOS client, clears the identity and marks the gamer signed out.
        /// </summary>
        public static void Shutdown()
        {
            IEpicOnlineServicesClient clientToDispose = null;

            lock (Gate)
            {
                isInitialized = false;
                playerId = null;
                gamertag = null;
                clientToDispose = eosClient;
                eosClient = null;
            }

            if (clientToDispose is IDisposable disposable)
            {
                disposable.Dispose();
            }

            SignedInGamer.Current.SetSignedInToLive(false);
        }
    }
}
