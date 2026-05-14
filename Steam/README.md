# MonoGame.Xna.Framework.Net.Steam

Steam-specific back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same networking abstractions as the core library, but wires them to the Steam back-end path.

## What this package provides

- `SteamNetworkSessionFactory`
- `SteamNetworkSession`
- `SteamNetworkGamer`
- `SteamPlatformBootstrap`
- `SteamRuntime`

The current Steam session is an in-memory vertical slice that validates host/find/join and reliable message behavior using the same abstraction seams a full Steamworks transport would use.

## Basic startup

Use this package from your Steam build to select the Steam back-end during startup.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

SteamPlatformBootstrap.Configure(gameName: "MyGame");
NetworkServiceProvider.SetSessionFactory(new SteamNetworkSessionFactory());
```

## Typical app integration

In a Steam game, initialize the back-end before creating or joining sessions, then let the shared networking APIs handle the rest.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

public static void ConfigureNetworking()
{
	SteamPlatformBootstrap.Configure(gameName: "MyGame");
	NetworkServiceProvider.SetSessionFactory(new SteamNetworkSessionFactory());
}

public static async Task CreateSteamSessionAsync()
{
	var session = NetworkServiceProvider.SessionFactory.CreateSession();
	await session.CreateAsync(sessionProperties: null, maxGamers: 4).ConfigureAwait(false);
}
```

## Sending a reliable gameplay message

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

var session = NetworkServiceProvider.SessionFactory.CreateSession();
var message = new PlayerMoveMessage(playerId: 1, x: 10, y: 20);
session.SendMessage(message, MessageDelivery.Reliable);
```

## Notes

- Keep Steam-specific code isolated from the core package.
- Use the shared message contracts in `Net/` so the same gameplay payloads work across back-ends.
- If you need the broader architecture overview, start from the root README instead.
