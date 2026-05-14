# MonoGame.Xna.Framework.Net.Android

Android / Google Play Games back-end package for MonoGame.Xna.Framework.Net.

This package wires the shared networking abstractions to the Android back-end path and keeps the Android-specific startup and sign-in behavior isolated from the core package.

## What this package provides

- `AndroidNetworkSessionFactory`
- `AndroidNetworkSession`
- `AndroidNetworkGamer` types through the Android runtime integration
- `AndroidPlatformBootstrap`
- `AndroidRuntime`

## Basic startup

Use this package from your Android app to initialize Play Games and select the Android session factory.

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

The Android host app should initialize the runtime once during startup, then sign in and wire the session factory before gameplay begins.

```csharp
using Microsoft.Xna.Framework.Net.Android;

protected override void OnCreate(Bundle? savedInstanceState)
{
	base.OnCreate(savedInstanceState);

	AndroidRuntime.Initialize(this, initialGamertag: "Player");
	AndroidPlatformBootstrap.Configure(gameName: "MyGame");
}
```

If your game uses a periodic update loop, you can keep the Android runtime alive with:

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

## Notes

- Keep Android-specific code isolated from the core package.
- Use the shared message contracts in `Net/` so the same gameplay payloads work across back-ends.
- If you need the broader architecture overview, start from the root README instead.
