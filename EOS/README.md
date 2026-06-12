# MonoGame.Xna.Framework.Net.EOS

Epic Online Services back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same shared networking API as the core library while wiring EOS sign-in, leaderboard, achievement, media, and session seams.

## What this package provides

- EOSNetworkSessionFactory
- EOSNetworkSession
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

public static async Task CreateSessionAsync()
{
    var session = NetworkServiceProvider.SessionFactory.CreateSession();
    await session.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0)
        .ConfigureAwait(false);
}
```

Call this periodically if your game has an update loop:

```csharp
EOSRuntime.RunCallbacks();
```

## Sending a reliable gameplay message

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.EOS;

var session = NetworkServiceProvider.SessionFactory.CreateSession();
var message = new PlayerMoveMessage(playerId: 1, x: 10, y: 20);
session.BroadcastMessage(message);
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
- Network transport currently uses shared SystemLink/UDP path while preserving EOS backend routing.
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
