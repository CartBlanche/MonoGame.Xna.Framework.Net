# MonoGame.Xna.Framework.Net

A modern, XNA-style networking layer for MonoGame.

This project keeps the familiar `Microsoft.Xna.Framework.Net` and `GamerServices` APIs, while making the transport layer pluggable so different networking back-ends can be used without rewriting game logic.

## Project goals

- Keep the classic XNA networking feel for existing game code.
- Support multiple networking back-ends behind one common API.
- Let games switch back-ends at startup (UDP/SystemLink, Steam, and others in the future).
- Keep message contracts shared across all back-ends.
- Make it easy to test host/find/join/message flows end-to-end.

### AI-assisted development

Full transparency: AI tools are used in this project to speed up development, research and refactoring.

All architecture, implementation direction and final review decisions are made by an experienced human engineer with 35+ years in the software industry working across banking, insurance, multinational enterprises, indie game studios and other startups.

The goal is simple: ship reliable, maintainable, human-quality code faster.

If you prefer projects built entirely without AI assistance, that is completely fair, then realistically using this libaray is probably not for you.

## Repository layout

- `Net/`: Core networking types, messages, abstractions, adapters, factories, services.
- `GamerServices/`: XNA-style gamer/guide/leaderboard services.
- `Android/`: Android-specific integration and composition helpers.
- `Steam/`: Steam-specific integration and composition helpers.
- `Tests/`: Unit and integration tests.

## How the back-end model works

The key extension point is `INetworkSessionFactory`.

At runtime, the game sets one active factory through `NetworkServiceProvider`.

- `NetworkServiceProvider.SetSessionFactory(...)` selects the active back-end.
- `NetworkServiceProvider.SessionFactory.CreateSession()` creates a session instance for that back-end.
- `NetworkServiceProvider.SessionFactory.FindSessionsAsync(...)` discovers joinable sessions.

All back-ends expose sessions through `INetworkSession` and gamers through `INetworkGamer`/`ILocalNetworkGamer`.

That means your game loop and message handlers can stay the same while the transport changes.

## Current back-ends in this repo

### UDP/SystemLink (default)

- Factory: `UdpNetworkSessionFactory`
- Session implementation: `UdpNetworkSession`
- Strategy: adapts the existing `NetworkSession` implementation through the abstraction interfaces.

### Steam 

- Factory: `SteamNetworkSessionFactory`
- Session implementation: `SteamNetworkSession`
- Gamer model: `SteamNetworkGamer`
- Bootstrap helper: `SteamPlatformBootstrap`
- Runtime wrapper: `SteamRuntime`

The current Steam session is an in-memory vertical slice that validates host/find/join/reliable-message behavior using the same abstraction seams used by a full Steamworks transport.

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

SteamPlatformBootstrap.Configure(gameName: "MyGame");
// Optional in your update loop when Steam is enabled:
// SteamRuntime.RunCallbacks();
```
