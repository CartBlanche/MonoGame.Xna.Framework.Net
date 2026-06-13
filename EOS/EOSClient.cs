using Auth = Epic.OnlineServices.Auth;
using Connect = Epic.OnlineServices.Connect;
using Epic.OnlineServices;
using Logging = Epic.OnlineServices.Logging;
using Platform = Epic.OnlineServices.Platform;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// EOS client implementation using EOS C# bindings Auth + Connect login flow.
    /// </summary>
    internal sealed class EOSClient : IEpicOnlineServicesClient, IDisposable
    {
        private static readonly object SdkGate = new();
        private const int DefaultAuthTimeoutSeconds = 180;

        private static bool resolverInstalled;
        private static bool sdkInitialized;
        private static Platform.PlatformInterface platformInterface;
        private static EOSCredentials runtimeCredentials;

        internal EOSClient(EOSCredentials credentials = null)
        {
            lock (SdkGate)
            {
                runtimeCredentials = credentials;
            }
        }

        public async Task<EpicAccountPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryEnsurePlatform(out var settings, out var platform))
            {
                return new EpicAccountPlayer();
            }

            Auth.LoginCallbackInfo authLogin;
            try
            {
                authLogin = await LoginEpicAccountWithRecoveryAsync(platform, settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EOSClient] Auth login failed: {ex.Message}");
                return new EpicAccountPlayer();
            }

            if (authLogin.ResultCode != Result.Success || authLogin.LocalUserId == null)
            {
                System.Diagnostics.Debug.WriteLine($"[EOSClient] Auth login did not succeed: {authLogin.ResultCode}");
                return new EpicAccountPlayer();
            }

            var accountId = authLogin.LocalUserId.ToString();

            var authInterface = platform.GetAuthInterface();
            var copyIdTokenOptions = new Auth.CopyIdTokenOptions
            {
                AccountId = authLogin.LocalUserId
            };

            var copyResult = authInterface.CopyIdToken(ref copyIdTokenOptions, out var idToken);
            if (copyResult != Result.Success || idToken == null || string.IsNullOrWhiteSpace(idToken.Value.JsonWebToken))
            {
                System.Diagnostics.Debug.WriteLine($"[EOSClient] CopyIdToken failed: {copyResult}");
                return new EpicAccountPlayer { Id = accountId };
            }

            var productUserId = await LoginProductUserAsync(platform, settings, idToken.Value.JsonWebToken, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(productUserId))
            {
                return new EpicAccountPlayer { Id = productUserId };
            }

            return new EpicAccountPlayer { Id = accountId };
        }

        internal void PumpCallbacks()
        {
            lock (SdkGate)
            {
                platformInterface?.Tick();
            }
        }

        public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EpicLeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            maxResults = Math.Clamp(maxResults, 1, 100);
            return Task.FromResult<IReadOnlyList<EpicLeaderboardEntry>>(Array.Empty<EpicLeaderboardEntry>());
        }

        public Task<IReadOnlyDictionary<string, EpicAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, EpicAchievementProgress>>(new Dictionary<string, EpicAchievementProgress>());
        }

        public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
        {
            return ReportProgressAsync(achievementId, 100f, cancellationToken);
        }

        public Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement id cannot be empty.", nameof(achievementId));

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            lock (SdkGate)
            {
                platformInterface?.Release();
                platformInterface = null;

                if (sdkInitialized)
                {
                    Platform.PlatformInterface.Shutdown();
                    sdkInitialized = false;
                }
            }
        }

        private static bool TryEnsurePlatform(out EosAuthSettings settings, out Platform.PlatformInterface platform)
        {
            settings = EosAuthSettings.TryBuild(runtimeCredentials);
            if (settings == null)
            {
                platform = null;
                return false;
            }

            lock (SdkGate)
            {
                InstallNativeResolverIfNeeded();

                if (!sdkInitialized)
                {
                    var initOptions = new Platform.InitializeOptions
                    {
                        ProductName = settings.ProductName,
                        ProductVersion = settings.ProductVersion
                    };

                    var initResult = Platform.PlatformInterface.Initialize(ref initOptions);
                    if (initResult != Result.Success && initResult != Result.AlreadyConfigured)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EOSClient] EOS initialize failed: {initResult}");
                        platform = null;
                        return false;
                    }

                    sdkInitialized = true;

                    Logging.LoggingInterface.SetLogLevel(Logging.LogCategory.AllCategories, Logging.LogLevel.Info);
                    Logging.LoggingInterface.SetCallback((ref Logging.LogMessage message) =>
                        Debug.WriteLine($"[EOS|{message.Category}|{message.Level}] {message.Message}"));
                }

                if (platformInterface == null)
                {
                    var createOptions = new Platform.Options
                    {
                        ProductId = settings.ProductId,
                        SandboxId = settings.SandboxId,
                        DeploymentId = settings.DeploymentId,
                        ClientCredentials = new Platform.ClientCredentials
                        {
                            ClientId = settings.ClientId,
                            ClientSecret = settings.ClientSecret
                        },
                        Flags = Platform.PlatformFlags.DisableOverlay,
                        IsServer = false
                    };

                    platformInterface = Platform.PlatformInterface.Create(ref createOptions);
                    if (platformInterface == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[EOSClient] EOS platform create failed.");
                        platform = null;
                        return false;
                    }
                }

                platform = platformInterface;
                return true;
            }
        }

        private static async Task<Auth.LoginCallbackInfo> LoginEpicAccountAsync(
            Platform.PlatformInterface platform,
            Auth.LoginOptions authLoginOptions,
            TimeSpan timeout,
            string stageName,
            CancellationToken cancellationToken)
        {
            var authInterface = platform.GetAuthInterface();

            var completion = new TaskCompletionSource<Auth.LoginCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            authInterface.Login(ref authLoginOptions, null, (ref Auth.LoginCallbackInfo callbackInfo) =>
            {
                completion.TrySetResult(callbackInfo);
            });

            return await WaitForCallbackAsync(completion, timeout, cancellationToken, stageName).ConfigureAwait(false);
        }

        private static async Task<Auth.LoginCallbackInfo> LoginEpicAccountWithRecoveryAsync(
            Platform.PlatformInterface platform,
            EosAuthSettings settings,
            CancellationToken cancellationToken)
        {
            var attempts = BuildAuthAttemptSequence(settings);
            Auth.LoginCallbackInfo lastResult = default;

            for (var i = 0; i < attempts.Count; i++)
            {
                var attempt = attempts[i];
                var options = BuildAuthLoginOptions(
                    attempt.LoginCredentialType,
                    attempt.LoginId,
                    attempt.LoginToken,
                    settings.ScopeFlags);

                var stageName = $"Auth.Login({attempt.Label})";
                var result = await LoginEpicAccountAsync(
                    platform,
                    options,
                    settings.CallbackTimeout,
                    stageName,
                    cancellationToken).ConfigureAwait(false);

                if (result.ResultCode == Result.Success)
                {
                    if (i > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EOSClient] Auth succeeded via fallback attempt '{attempt.Label}'.");
                    }

                    return result;
                }

                lastResult = result;
                System.Diagnostics.Debug.WriteLine($"[EOSClient] {stageName} failed: {result.ResultCode}");

                if (attempt.LoginCredentialType == Auth.LoginCredentialType.PersistentAuth
                    && Common.IsOperationComplete(result.ResultCode))
                {
                    try
                    {
                        var deleteResult = await DeletePersistentAuthAsync(platform, settings.CallbackTimeout, cancellationToken).ConfigureAwait(false);
                        System.Diagnostics.Debug.WriteLine($"[EOSClient] DeletePersistentAuth result: {deleteResult}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EOSClient] DeletePersistentAuth failed: {ex.Message}");
                    }
                }
            }

            return lastResult;
        }

        private static List<AuthAttempt> BuildAuthAttemptSequence(EosAuthSettings settings)
        {
            var attempts = new List<AuthAttempt>();

            AddAttempt(
                attempts,
                settings.LoginCredentialType,
                settings.LoginId,
                settings.LoginToken,
                label: "primary");

            if (settings.EnableLoginModeLadder)
            {
                if (settings.RecoveryLoginCredentialType.HasValue)
                {
                    AddAttempt(
                        attempts,
                        settings.RecoveryLoginCredentialType.Value,
                        settings.RecoveryLoginId,
                        settings.RecoveryLoginToken,
                        label: "recovery");
                }

                if (settings.LoginCredentialType == Auth.LoginCredentialType.PersistentAuth)
                {
                    AddAttempt(
                        attempts,
                        Auth.LoginCredentialType.AccountPortal,
                        null,
                        null,
                        label: "account-portal");
                }

                if (!string.IsNullOrWhiteSpace(settings.DeveloperLoginId)
                    && !string.IsNullOrWhiteSpace(settings.DeveloperLoginToken))
                {
                    AddAttempt(
                        attempts,
                        Auth.LoginCredentialType.Developer,
                        settings.DeveloperLoginId,
                        settings.DeveloperLoginToken,
                        label: "developer");
                }
            }
            else if (settings.RecoveryLoginCredentialType.HasValue)
            {
                AddAttempt(
                    attempts,
                    settings.RecoveryLoginCredentialType.Value,
                    settings.RecoveryLoginId,
                    settings.RecoveryLoginToken,
                    label: "recovery");
            }

            return attempts;
        }

        private static void AddAttempt(
            List<AuthAttempt> attempts,
            Auth.LoginCredentialType loginCredentialType,
            string loginId,
            string loginToken,
            string label)
        {
            var normalizedId = string.IsNullOrWhiteSpace(loginId) ? string.Empty : loginId.Trim();
            var normalizedToken = string.IsNullOrWhiteSpace(loginToken) ? string.Empty : loginToken.Trim();

            if (attempts.Any(x =>
                x.LoginCredentialType == loginCredentialType
                && string.Equals(x.LoginId, normalizedId, StringComparison.Ordinal)
                && string.Equals(x.LoginToken, normalizedToken, StringComparison.Ordinal)))
            {
                return;
            }

            attempts.Add(new AuthAttempt(loginCredentialType, normalizedId, normalizedToken, label));
        }

        private static Auth.LoginOptions BuildAuthLoginOptions(
            Auth.LoginCredentialType loginCredentialType,
            string loginId,
            string loginToken,
            Auth.AuthScopeFlags scopeFlags)
        {
            var shouldPassIdToken = loginCredentialType != Auth.LoginCredentialType.PersistentAuth
                && loginCredentialType != Auth.LoginCredentialType.AccountPortal;

            return new Auth.LoginOptions
            {
                Credentials = new Auth.Credentials
                {
                    Type = loginCredentialType,
                    Id = shouldPassIdToken ? loginId : null,
                    Token = shouldPassIdToken ? loginToken : null
                },
                ScopeFlags = scopeFlags,
                LoginFlags = Auth.LoginFlags.None
            };
        }

        private static async Task<Result> DeletePersistentAuthAsync(
            Platform.PlatformInterface platform,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var authInterface = platform.GetAuthInterface();
            var options = new Auth.DeletePersistentAuthOptions();

            var completion = new TaskCompletionSource<Auth.DeletePersistentAuthCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            authInterface.DeletePersistentAuth(ref options, null, (ref Auth.DeletePersistentAuthCallbackInfo callbackInfo) =>
            {
                completion.TrySetResult(callbackInfo);
            });

            var result = await WaitForCallbackAsync(completion, timeout, cancellationToken, "Auth.DeletePersistentAuth").ConfigureAwait(false);
            return result.ResultCode;
        }

        private static async Task<string> LoginProductUserAsync(
            Platform.PlatformInterface platform,
            EosAuthSettings settings,
            string idTokenJwt,
            CancellationToken cancellationToken)
        {
            var connectInterface = platform.GetConnectInterface();

            var loginResult = await ConnectLoginAsync(connectInterface, settings.CallbackTimeout, idTokenJwt, cancellationToken).ConfigureAwait(false);
            if (loginResult.ResultCode == Result.Success)
            {
                return loginResult.LocalUserId?.ToString() ?? string.Empty;
            }

            if (loginResult.ResultCode == Result.InvalidUser && loginResult.ContinuanceToken != null)
            {
                var createOptions = new Connect.CreateUserOptions
                {
                    ContinuanceToken = loginResult.ContinuanceToken
                };

                var createCompletion = new TaskCompletionSource<Connect.CreateUserCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                connectInterface.CreateUser(ref createOptions, null, (ref Connect.CreateUserCallbackInfo createInfo) =>
                {
                    createCompletion.TrySetResult(createInfo);
                });

                var createResult = await WaitForCallbackAsync(createCompletion, settings.CallbackTimeout, cancellationToken, "Connect.CreateUser").ConfigureAwait(false);
                if (createResult.ResultCode == Result.Success)
                {
                    var retryLogin = await ConnectLoginAsync(connectInterface, settings.CallbackTimeout, idTokenJwt, cancellationToken).ConfigureAwait(false);
                    if (retryLogin.ResultCode == Result.Success)
                    {
                        return retryLogin.LocalUserId?.ToString() ?? string.Empty;
                    }

                    System.Diagnostics.Debug.WriteLine($"[EOSClient] Connect login retry failed: {retryLogin.ResultCode}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[EOSClient] Connect.CreateUser failed: {createResult.ResultCode}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[EOSClient] Connect login failed: {loginResult.ResultCode}");
            }

            return string.Empty;
        }

        private static async Task<Connect.LoginCallbackInfo> ConnectLoginAsync(
            Connect.ConnectInterface connectInterface,
            TimeSpan timeout,
            string idTokenJwt,
            CancellationToken cancellationToken)
        {
            var connectLoginOptions = new Connect.LoginOptions
            {
                Credentials = new Connect.Credentials
                {
                    Type = ExternalCredentialType.EpicIdToken,
                    Token = idTokenJwt
                }
            };

            var completion = new TaskCompletionSource<Connect.LoginCallbackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            connectInterface.Login(ref connectLoginOptions, null, (ref Connect.LoginCallbackInfo callbackInfo) =>
            {
                completion.TrySetResult(callbackInfo);
            });

            return await WaitForCallbackAsync(completion, timeout, cancellationToken, "Connect.Login").ConfigureAwait(false);
        }

        private static async Task<TCallback> WaitForCallbackAsync<TCallback>(
            TaskCompletionSource<TCallback> completion,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string stageName)
        {
            var stopwatch = Stopwatch.StartNew();

            while (!completion.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (stopwatch.Elapsed >= timeout)
                {
                    throw new TimeoutException($"Timed out waiting for EOS callback at stage '{stageName}' after {(int)timeout.TotalSeconds}s.");
                }

                lock (SdkGate)
                {
                    platformInterface?.Tick();
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            return await completion.Task.ConfigureAwait(false);
        }

        private static void InstallNativeResolverIfNeeded()
        {
            if (resolverInstalled)
            {
                return;
            }

            var eosAssembly = typeof(Common).Assembly;
            NativeLibrary.SetDllImportResolver(eosAssembly, ResolveNativeLibrary);
            resolverInstalled = true;
        }

        private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, Common.LIBRARY_NAME, StringComparison.Ordinal))
            {
                return IntPtr.Zero;
            }

            var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "EOSSDK-Win64-Shipping.dll" }
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new[] { "libEOSSDK-Mac-Shipping.dylib" }
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        ? new[] { "libEOSSDK-Linux-Shipping.so" }
                        : Array.Empty<string>();

            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        private sealed class EosAuthSettings
        {
            public string ProductName { get; init; }
            public string ProductVersion { get; init; }
            public string ProductId { get; init; }
            public string SandboxId { get; init; }
            public string DeploymentId { get; init; }
            public string ClientId { get; init; }
            public string ClientSecret { get; init; }
            public Auth.LoginCredentialType LoginCredentialType { get; init; }
            public string LoginId { get; init; }
            public string LoginToken { get; init; }
            public Auth.LoginCredentialType? RecoveryLoginCredentialType { get; init; }
            public string RecoveryLoginId { get; init; }
            public string RecoveryLoginToken { get; init; }
            public string DeveloperLoginId { get; init; }
            public string DeveloperLoginToken { get; init; }
            public Auth.AuthScopeFlags ScopeFlags { get; init; }
            public TimeSpan CallbackTimeout { get; init; }
            public bool EnableLoginModeLadder { get; init; }

            public static EosAuthSettings TryBuild(EOSCredentials runtime)
            {
                var productId = runtime?.ProductId?.Trim() ?? ReadRequired("MGNET_EOS_PRODUCT_ID");
                var sandboxId = runtime?.SandboxId?.Trim() ?? ReadRequired("MGNET_EOS_SANDBOX_ID");
                var deploymentId = runtime?.DeploymentId?.Trim() ?? ReadRequired("MGNET_EOS_DEPLOYMENT_ID");
                var clientId = runtime?.ClientId?.Trim() ?? ReadRequired("MGNET_EOS_CLIENT_ID");
                var clientSecret = runtime?.ClientSecret?.Trim() ?? ReadRequired("MGNET_EOS_CLIENT_SECRET");

                if (productId == null || sandboxId == null || deploymentId == null || clientId == null || clientSecret == null)
                {
                    return null;
                }

                var loginType = ParseCredentialType(Environment.GetEnvironmentVariable("MGNET_EOS_LOGIN_TYPE"));
                var loginId = Environment.GetEnvironmentVariable("MGNET_EOS_LOGIN_ID") ?? string.Empty;
                var loginToken = Environment.GetEnvironmentVariable("MGNET_EOS_LOGIN_TOKEN") ?? string.Empty;
                var recoveryType = ParseOptionalCredentialType(Environment.GetEnvironmentVariable("MGNET_EOS_RECOVERY_LOGIN_TYPE"));
                var recoveryId = Environment.GetEnvironmentVariable("MGNET_EOS_RECOVERY_LOGIN_ID") ?? string.Empty;
                var recoveryToken = Environment.GetEnvironmentVariable("MGNET_EOS_RECOVERY_LOGIN_TOKEN") ?? string.Empty;
                var developerLoginId = Environment.GetEnvironmentVariable("MGNET_EOS_DEVELOPER_LOGIN_ID") ?? string.Empty;
                var developerLoginToken = Environment.GetEnvironmentVariable("MGNET_EOS_DEVELOPER_LOGIN_TOKEN") ?? string.Empty;
                var enableLoginModeLadder = ParseBool(Environment.GetEnvironmentVariable("MGNET_EOS_ENABLE_LOGIN_MODE_LADDER"), defaultValue: true);

                if (loginType == Auth.LoginCredentialType.PersistentAuth && !recoveryType.HasValue)
                {
                    recoveryType = Auth.LoginCredentialType.AccountPortal;
                }

                var callbackTimeout = ParseTimeout(Environment.GetEnvironmentVariable("MGNET_EOS_AUTH_TIMEOUT_SECONDS"));
                var scopeFlags = ParseScopeFlags(Environment.GetEnvironmentVariable("MGNET_EOS_SCOPE_FLAGS"));

                var exchangeCode = Environment.GetEnvironmentVariable("MGNET_EOS_EXCHANGE_CODE");
                if (!string.IsNullOrWhiteSpace(exchangeCode))
                {
                    loginType = Auth.LoginCredentialType.ExchangeCode;
                    loginToken = exchangeCode.Trim();
                    loginId = string.Empty;
                }

                return new EosAuthSettings
                {
                    ProductName = runtime?.ProductName?.Trim()
                        ?? Environment.GetEnvironmentVariable("MGNET_EOS_PRODUCT_NAME")?.Trim()
                        ?? "MonoGame.Xna.Framework.Net",
                    ProductVersion = runtime?.ProductVersion?.Trim()
                        ?? Environment.GetEnvironmentVariable("MGNET_EOS_PRODUCT_VERSION")?.Trim()
                        ?? "1.0.0",
                    ProductId = productId,
                    SandboxId = sandboxId,
                    DeploymentId = deploymentId,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    LoginCredentialType = loginType,
                    LoginId = loginId,
                    LoginToken = loginToken,
                    RecoveryLoginCredentialType = recoveryType,
                    RecoveryLoginId = recoveryId,
                    RecoveryLoginToken = recoveryToken,
                    DeveloperLoginId = developerLoginId,
                    DeveloperLoginToken = developerLoginToken,
                    ScopeFlags = scopeFlags,
                    CallbackTimeout = callbackTimeout,
                    EnableLoginModeLadder = enableLoginModeLadder
                };
            }

            private static string ReadRequired(string variableName)
            {
                var value = Environment.GetEnvironmentVariable(variableName);
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            private static Auth.LoginCredentialType ParseCredentialType(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return Auth.LoginCredentialType.PersistentAuth;
                }

                if (Enum.TryParse<Auth.LoginCredentialType>(value.Trim(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                return Auth.LoginCredentialType.PersistentAuth;
            }

            private static Auth.LoginCredentialType? ParseOptionalCredentialType(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                if (Enum.TryParse<Auth.LoginCredentialType>(value.Trim(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                return null;
            }

            private static TimeSpan ParseTimeout(string value)
            {
                if (int.TryParse(value, out var seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                return TimeSpan.FromSeconds(DefaultAuthTimeoutSeconds);
            }

            private static Auth.AuthScopeFlags ParseScopeFlags(string value)
            {
                var defaults = Auth.AuthScopeFlags.BasicProfile | Auth.AuthScopeFlags.FriendsList | Auth.AuthScopeFlags.Presence;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return defaults;
                }

                if (Enum.TryParse<Auth.AuthScopeFlags>(value.Trim(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                return defaults;
            }

            private static bool ParseBool(string value, bool defaultValue)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                if (bool.TryParse(value.Trim(), out var parsed))
                {
                    return parsed;
                }

                return defaultValue;
            }
        }

        private readonly struct AuthAttempt
        {
            public AuthAttempt(Auth.LoginCredentialType loginCredentialType, string loginId, string loginToken, string label)
            {
                LoginCredentialType = loginCredentialType;
                LoginId = loginId ?? string.Empty;
                LoginToken = loginToken ?? string.Empty;
                Label = string.IsNullOrWhiteSpace(label) ? loginCredentialType.ToString() : label;
            }

            public Auth.LoginCredentialType LoginCredentialType { get; }
            public string LoginId { get; }
            public string LoginToken { get; }
            public string Label { get; }
        }
    }
}
