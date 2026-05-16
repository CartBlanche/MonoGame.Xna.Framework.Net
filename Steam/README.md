# MonoGame.Xna.Framework.Net.Steam

Steam back-end package for MonoGame.Xna.Framework.Net.

This package keeps the same shared networking API as the core library, while connecting your game to the Steam path.

## What this package provides

- SteamNetworkSessionFactory
- SteamNetworkSession
- SteamNetworkGamer
- SteamPlatformBootstrap
- SteamRuntime

## Required setup (login, leaderboards, achievements, multiplayer)

Use this checklist before you debug runtime issues.

- [ ] Your game has a valid Steam App ID in Steamworks.
- [ ] You launch from Steam, or you include steam_appid.txt for local development.
- [ ] Steamworks features are set up for your app (stats/achievements, leaderboards, multiplayer as needed).
- [ ] The signed-in Steam account has access to the app build you are testing.

## Official docs

- Steamworks SDK API overview: https://partner.steamgames.com/doc/sdk/api
- Steam local testing and steam_appid.txt: https://partner.steamgames.com/doc/sdk/api#SteamAPI_RestartAppIfNecessary
- Steam achievements and stats: https://partner.steamgames.com/doc/features/achievements
- Steam leaderboards: https://partner.steamgames.com/doc/features/leaderboards
- Steam matchmaking and lobbies: https://partner.steamgames.com/doc/features/multiplayer/matchmaking

## Step-by-step app setup

1. Create or confirm your Steamworks app and get the App ID.
2. Set up achievements and stats in Steamworks and publish them.
3. Set up leaderboards in Steamworks and publish them.
4. If you use multiplayer discovery, set up lobbies or matchmaking for your app.
5. For local runs outside Steam, place steam_appid.txt next to your game executable.
6. Put only your numeric App ID in the file, for example:

```text
480
```

7. Start Steam and sign in with a test account that can access your app.
8. Initialize Steam runtime before creating or finding sessions.

## Basic startup

Use this package in your Steam build to select the Steam back-end during startup.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

SteamPlatformBootstrap.Configure(gameName: "MyGame");
NetworkServiceProvider.SetSessionFactory(new SteamNetworkSessionFactory());
```

## Typical app integration

Initialize once at startup, then use the shared networking APIs during gameplay.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

public static void ConfigureNetworking()
{
	SteamPlatformBootstrap.Configure(gameName: "MyGame");
	NetworkServiceProvider.SetSessionFactory(new SteamNetworkSessionFactory());
}

public static async Task CreateSessionAsync()
{
	var session = NetworkServiceProvider.SessionFactory.CreateSession();
	await session.CreateAsync(sessionProperties: null, maxGamers: 4).ConfigureAwait(false);
}
```

If your game has an update loop, call this periodically:

```csharp
SteamRuntime.RunCallbacks();
```

## Sending a reliable gameplay message

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

var session = NetworkServiceProvider.SessionFactory.CreateSession();
var message = new PlayerMoveMessage(playerId: 1, x: 10, y: 20);
session.SendMessage(message, MessageDelivery.Reliable);
```

## Verify your setup

Run this quick smoke checklist in order:

1. Login: runtime initializes and reports an authenticated Steam user.
2. Leaderboards: write one score, then read it back.
3. Achievements: unlock one test achievement and confirm it appears in Steam.
4. Multiplayer/session discovery: host one session and discover/join from a second instance.
5. Reliable message flow: send one reliable message and verify receive on the peer.

## Common failures

- Steam API init fails locally: missing or wrong steam_appid.txt, or Steam client is not running.
- Leaderboard calls fail: leaderboard is missing or not published in Steamworks.
- Achievement unlock is not visible: stats/achievement setup is incomplete or unpublished.
- Session discovery fails: multiplayer configuration does not match your runtime flow.

## Notes

- Keep Steam-specific code isolated from the core package.
- Use the shared message contracts in Net so payloads can stay backend-agnostic.
- For core architecture details, use the root README.
