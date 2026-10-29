# MonoGame.Xna.Framework.Net.EOS

Epic Online Services back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same shared networking API as the core library while wiring EOS sign-in, leaderboard, achievement, media, and session seams.

## What this package provides

- EOSBackend: EOS lobbies for discovery, EOS P2P for gameplay (`EOSP2PTransport`)
- EOSPlatformBootstrap
- EOSRuntime

## Required setup (login, leaderboards, achievements, multiplayer)

Use this checklist before debugging runtime issues.

- [ ] Create your EOS product, sandbox, and deployment in Epic Developer Portal.
- [ ] Configure client credentials and grant policy for your game.
- [ ] Create EOS leaderboard definitions for keys used by your game.
- [ ] Create EOS achievement definitions and icon assets.
- [ ] Ensure EOS runtime native libraries are deployed with your app.
- [ ] Configure user login path for your target environment (dev/auth/external).

### SDK Auth + Connect environment settings

`EOSClient` now follows the EOS C# SDK Auth + Connect flow (Auth login, CopyIdToken, Connect login, continuance token CreateUser retry).

Required environment variables:

- `MGNET_EOS_PRODUCT_ID`
- `MGNET_EOS_SANDBOX_ID`
- `MGNET_EOS_DEPLOYMENT_ID`
- `MGNET_EOS_CLIENT_ID`
- `MGNET_EOS_CLIENT_SECRET`

Optional environment variables:

- `MGNET_EOS_PRODUCT_NAME` (default: `MonoGame.Xna.Framework.Net`)
- `MGNET_EOS_PRODUCT_VERSION` (default: `1.0.0`)
- `MGNET_EOS_LOGIN_TYPE` (default: `PersistentAuth`)
- `MGNET_EOS_LOGIN_ID`
- `MGNET_EOS_LOGIN_TOKEN`
- `MGNET_EOS_ENABLE_LOGIN_MODE_LADDER` (default: `true`)
- `MGNET_EOS_RECOVERY_LOGIN_TYPE` (used after failed `PersistentAuth`; default: `AccountPortal`)
- `MGNET_EOS_RECOVERY_LOGIN_ID`
- `MGNET_EOS_RECOVERY_LOGIN_TOKEN`
- `MGNET_EOS_DEVELOPER_LOGIN_ID` (used for optional `Developer` fallback in the ladder)
- `MGNET_EOS_DEVELOPER_LOGIN_TOKEN` (used for optional `Developer` fallback in the ladder)
- `MGNET_EOS_SCOPE_FLAGS` (default: `BasicProfile, FriendsList, Presence`)
- `MGNET_EOS_AUTH_TIMEOUT_SECONDS` (default: `180`)
- `MGNET_EOS_EXCHANGE_CODE` (overrides login type/token to `ExchangeCode`)
- `MGNET_EOS_PLATFORM_FLAGS` (comma-separated `Platform.PlatformFlags`; default: `DisableOverlay`)

### Native library

NuGet consumers get `libEOSSDK-*`/`EOSSDK-*` from the package's `runtimes/<rid>/native`. Running from source (a project reference), the build copies the library for your machine's own runtime id to the output's `runtimes/<rid>/native`, and `EOSClient` looks there. `dotnet test Tests/EOS --filter EOSNativeLibraryTests` checks that it loads, with no account needed.

### Callbacks

EOS delivers connection requests, lobby changes and invitations only when it is ticked. While a session is running the EOS transport ticks it for you; call `EOSRuntime.RunCallbacks()` yourself only if you want invitations before any session exists.

Default ladder when enabled and starting from `PersistentAuth`:

1. `PersistentAuth`
2. `AccountPortal`
3. `Developer` (only when both developer ID/token are provided)

## Official docs

- EOS main docs: https://dev.epicgames.com/docs/epic-online-services
- EOS C SDK and integration: https://dev.epicgames.com/docs/epic-online-services/eos-get-started
- EOS auth/connect: https://dev.epicgames.com/docs/epic-account-services/auth-interface
- EOS sessions/lobbies/p2p: https://dev.epicgames.com/docs/epic-online-services/game-services

## Step-by-step app setup

1. Create or confirm EOS product/sandbox/deployment.
2. Configure client credentials and required permissions.
3. Create leaderboard and achievement definitions.
4. Add EOS runtime libraries to your app output.
5. Initialize EOS runtime on startup.
6. Configure EOS platform bootstrap and sign-in flow.
7. Verify sign-in, leaderboard, achievements, and session discovery in a test build.

## Basic startup

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.EOS;

EOSRuntime.Initialize(initialGamertag: "Player");
EOSPlatformBootstrap.Configure(gameName: "MyGame");
```

## Typical app integration

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.EOS;

public static async Task<bool> SignInAndEnableLiveAsync()
{
    EOSRuntime.Initialize(initialGamertag: "Player");
    EOSPlatformBootstrap.Configure(gameName: "MyGame");
    return await EOSPlatformBootstrap.TrySignInAndEnableLiveAsync().ConfigureAwait(false);
}

public static async Task<NetworkSession> CreateSessionAsync()
{
    return await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null);
}
```

Call this periodically if your game has an update loop:

```csharp
EOSRuntime.RunCallbacks();
```

## Sending a reliable gameplay message

```csharp
var writer = new PacketWriter();
writer.Write(10);
writer.Write(20);
session.LocalGamers[0].SendData(writer, SendDataOptions.ReliableInOrder);
```

## Verify your setup

1. SignIn: EOS authentication returns a player identity.
2. Leaderboards: submit a score and read a ranked page.
3. Achievements: set progress/unlock and confirm projection.
4. Multiplayer/session discovery: host a session and join from another instance.
5. Reliable message flow: send one reliable message and verify receive on peer.

## Common failures

- Sign-in returns false: EOS runtime initialized without valid login context.
- Leaderboard calls no-op: leaderboard key not configured in EOS portal.
- Achievement icon missing: icon asset or mapping key not configured.
- Session discovery empty: no active host or blocked local networking path.

## Notes

- This vertical slice keeps EOS calls behind an injectable client seam for deterministic tests.
- Network transport uses EOS Lobby (session creation and discovery) and EOS P2P (packet delivery with NAT traversal via Epic's relay infrastructure).
- Bundled native runtime version for this release: EOS SDK v1.19.1.2.
- Packaged runtime binaries are included under runtimes for desktop targets, including `linux-x64`, `linux-arm64`, `win-x64`, and `win-arm64`; macOS ships the same universal dylib in both `osx-x64` and `osx-arm64` RID folders for predictable .NET native asset resolution.

## Backend parity matrix

| Capability | EOS status |
| --- | --- |
| SignIn | implemented |
| Leaderboards | implemented |
| Achievements | implemented |
| Achievement media | implemented |
| Networking | implemented |
| Runtime smoke | compile-validated only |
