# MonoGame.Xna.Framework.Net.iOS

iOS / Game Center back-end package for MonoGame.Xna.Framework.Net.

This package wires the shared networking abstractions to an iOS-focused back-end path and keeps iOS startup and sign-in behavior isolated from the core package.

## What this package provides

- `IOSNetworkSessionFactory`
- `IOSNetworkSession`
- `IOSPlatformBootstrap`
- `IOSRuntime`

## Basic startup

Use this package from your iOS app to initialize the iOS runtime and select the iOS session factory.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.iOS;

IOSRuntime.Initialize(initialGamertag: "Player");
IOSPlatformBootstrap.Configure(gameName: "MyGame");
NetworkServiceProvider.SetSessionFactory(new IOSNetworkSessionFactory());
```

## Typical app integration

The iOS host app should initialize the runtime once during startup, then sign in and wire the session factory before gameplay begins.

```csharp
using Microsoft.Xna.Framework.Net.iOS;

public override void FinishedLaunching(UIApplication app)
{
    IOSRuntime.Initialize(initialGamertag: "Player");
    IOSPlatformBootstrap.Configure(gameName: "MyGame");
}
```

If your game uses a periodic update loop, you can keep runtime identity in sync with:

```csharp
IOSRuntime.RunCallbacks();
```

## Notes

- Keep iOS-specific code isolated from the core package.
- Use shared message contracts in `Net/` so payloads remain backend-agnostic.
- Game Center SDK calls are intentionally behind `IAppleGameCenterClient` to keep this package testable and composable.