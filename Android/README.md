# MonoGame.Xna.Framework.Net.Android

Android back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same shared networking API as the core library, while connecting your game to the Android and Play Games path.

## What this package provides

- AndroidNetworkSessionFactory
- AndroidNetworkSession
- AndroidNetworkGamer runtime integration
- AndroidPlatformBootstrap
- AndroidRuntime

## Required setup (login, leaderboards, achievements, multiplayer)

Use this checklist before you debug runtime issues.

- [ ] Your app exists in Google Play Console.
- [ ] Google Play Games Services is enabled for your app.
- [ ] OAuth client setup is correct for your package name and signing certificate.
- [ ] Achievements and leaderboards are created for your app.
- [ ] Tester accounts are added and can access the build you are testing.
- [ ] If you use multiplayer discovery, multiplayer features are set up for your game.

## Official docs

- Play Games Services overview: https://developer.android.com/games/pgs
- Play Console setup: https://developer.android.com/games/pgs/console/setup
- Android sign-in: https://developer.android.com/games/pgs/android/android-signin
- Android achievements: https://developer.android.com/games/pgs/android/achievements
- Android leaderboards: https://developer.android.com/games/pgs/android/leaderboards
- Multiplayer and invitations: https://developer.android.com/games/pgs/multiplayer

## Step-by-step app setup

1. Create your app in Play Console.
2. Enable Play Games Services for the app.
3. Configure OAuth and signing so your build fingerprint matches Play Games setup.
4. Create achievements and leaderboards.
5. Publish the needed configuration updates for testing.
6. Add tester accounts and verify they can access the build.
7. If you use multiplayer discovery, enable and configure multiplayer features.
8. Initialize Android runtime before creating or finding sessions.

## Basic startup

Use this package in your Android build to select the Android back-end during startup.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Android;

AndroidRuntime.Initialize(
	androidActivity: this,
	initialPlayerId: "player-id",
	initialGamertag: "Player");
AndroidPlatformBootstrap.Configure(gameName: "MyGame");
NetworkServiceProvider.SetSessionFactory(new AndroidNetworkSessionFactory());
```

## Typical app integration

Initialize once at startup, then use the shared networking APIs during gameplay.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Android;

protected override void OnCreate(Bundle? savedInstanceState)
{
	base.OnCreate(savedInstanceState);

	AndroidRuntime.Initialize(this, initialGamertag: "Player");
	AndroidPlatformBootstrap.Configure(gameName: "MyGame");
	NetworkServiceProvider.SetSessionFactory(new AndroidNetworkSessionFactory());
}

public static async Task CreateSessionAsync()
{
	var session = NetworkServiceProvider.SessionFactory.CreateSession();
	await session.CreateAsync(sessionProperties: null, maxGamers: 4).ConfigureAwait(false);
}
```

If your game has an update loop, call this periodically:

```csharp
AndroidRuntime.RunCallbacks();
```

## Sending a reliable gameplay message

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Android;

var session = NetworkServiceProvider.SessionFactory.CreateSession();
var message = new PlayerMoveMessage(playerId: 1, x: 10, y: 20);
session.SendMessage(message, MessageDelivery.Reliable);
```

## Verify your setup

Run this quick smoke checklist in order:

1. Login: Play Games sign-in succeeds for a tester account.
2. Leaderboards: write one score, then read it back.
3. Achievements: unlock one test achievement and confirm it appears.
4. Multiplayer/session discovery: host one session and discover/join from a second device.
5. Reliable message flow: send one reliable message and verify receive on the peer.

## Common failures

- Sign-in fails on one build variant: package name or signing fingerprint does not match OAuth setup.
- Leaderboard calls fail: leaderboard ID is wrong or not published for test visibility.
- Achievement unlock is not visible: achievement ID is wrong or config is not published.
- Session discovery fails: multiplayer setup is incomplete or tester/device setup is invalid.

## Notes

- Keep Android-specific code isolated from the core package.
- Use the shared message contracts in Net so payloads can stay backend-agnostic.
- For core architecture details, use the root README.
