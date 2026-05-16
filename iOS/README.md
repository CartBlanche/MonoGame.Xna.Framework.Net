# MonoGame.Xna.Framework.Net.iOS

iOS back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same shared networking API as the core library, while connecting your game to the iOS and Game Center path.

## What this package provides

- IOSNetworkSessionFactory
- IOSNetworkSession
- IOSPlatformBootstrap
- IOSRuntime

## Required setup (login, leaderboards, achievements, multiplayer)

Use this checklist before you debug runtime issues.

- [ ] Your app has a valid App ID and bundle identifier in Apple Developer.
- [ ] Game Center capability is enabled for your app target.
- [ ] Game Center is enabled for the app in App Store Connect.
- [ ] Achievements and leaderboards are created for your app.
- [ ] Sandbox tester accounts are added for development testing.
- [ ] If you use multiplayer discovery, GameKit matchmaking is set up for your game.

## Official docs

- Game Center overview: https://developer.apple.com/game-center/
- GameKit framework docs: https://developer.apple.com/documentation/gamekit
- Xcode capabilities setup: https://developer.apple.com/documentation/xcode/configuring-app-capabilities
- GameKit leaderboards docs: https://developer.apple.com/documentation/gamekit/gkleaderboard
- GameKit matchmaking docs: https://developer.apple.com/documentation/gamekit/gkmatchmaker
- Testing and debugging Game Center: https://developer.apple.com/documentation/gamekit/testing-and-debugging-game-center

## Step-by-step app setup

1. Create or confirm your app identifier and bundle ID in Apple Developer.
2. Enable the Game Center capability in your app target.
3. Enable Game Center for the app in App Store Connect.
4. Create achievements and leaderboards.
5. Add sandbox testers and sign in on your test device.
6. If you use multiplayer discovery, configure matchmaking for your game flow.
7. Start the app with a sandbox tester account and complete Game Center authentication.
8. Initialize iOS runtime before creating or finding sessions.

## Basic startup

Use this package in your iOS build to select the iOS back-end during startup.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.iOS;

IOSRuntime.Initialize(initialGamertag: "Player");
IOSPlatformBootstrap.Configure(gameName: "MyGame");
NetworkServiceProvider.SetSessionFactory(new IOSNetworkSessionFactory());
```

## Typical app integration

Initialize once at startup, then use the shared networking APIs during gameplay.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.iOS;

public override void FinishedLaunching(UIApplication app)
{
    IOSRuntime.Initialize(initialGamertag: "Player");
    IOSPlatformBootstrap.Configure(gameName: "MyGame");
    NetworkServiceProvider.SetSessionFactory(new IOSNetworkSessionFactory());
}

public static async Task CreateSessionAsync()
{
    var session = NetworkServiceProvider.SessionFactory.CreateSession();
    await session.CreateAsync(sessionProperties: null, maxGamers: 4).ConfigureAwait(false);
}
```

If your game has an update loop, call this periodically:

```csharp
IOSRuntime.RunCallbacks();
```

## Sending a reliable gameplay message

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.iOS;

var session = NetworkServiceProvider.SessionFactory.CreateSession();
var message = new PlayerMoveMessage(playerId: 1, x: 10, y: 20);
session.SendMessage(message, MessageDelivery.Reliable);
```

## Verify your setup

Run this quick smoke checklist in order:

1. Login: Game Center authentication succeeds for a sandbox tester.
2. Leaderboards: write one score, then read it back.
3. Achievements: unlock one test achievement and confirm it appears.
4. Multiplayer/session discovery: host one session and discover/join from a second device.
5. Reliable message flow: send one reliable message and verify receive on the peer.

## Common failures

- Login UI fails repeatedly: Game Center capability is missing or tester account is not set up correctly.
- Leaderboard calls fail: leaderboard ID does not match App Store Connect.
- Achievement unlock is not visible: achievement ID does not match App Store Connect.
- Session discovery fails: matchmaking setup does not match your test scenario.

## Notes

- Keep iOS-specific code isolated from the core package.
- Use the shared message contracts in Net so payloads can stay backend-agnostic.
- For core architecture details, use the root README.