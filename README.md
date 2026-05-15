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

## Add a new back-end

Use Steam as the reference pattern.

### 1) Implement the factory

# MonoGame.Xna.Framework.Net

MonoGame.Xna.Framework.Net is a modern, XNA-style networking layer for MonoGame.

It keeps the familiar `Microsoft.Xna.Framework.Net` and `GamerServices` APIs while making the transport layer pluggable, so game code can stay stable while the back-end changes.

## What lives here

- `Net/`: core networking types, messages, abstractions, adapters, factories, and services.
- `GamerServices/`: XNA-style gamer, guide, and leaderboard services.
- `Steam/`: Steam-specific back-end package.
- `Android/`: Android / Google Play Games back-end package.
- `iOS/`: iOS / Game Center back-end package.
- `Tests/`: unit and integration tests.

## Core networking model

The main extension point is `INetworkSessionFactory`.

At runtime, the game selects one active factory through `NetworkServiceProvider`.

- `NetworkServiceProvider.SetSessionFactory(...)` selects the active back-end.
- `NetworkServiceProvider.SessionFactory.CreateSession()` creates a session instance for that back-end.
- `NetworkServiceProvider.SessionFactory.FindSessionsAsync(...)` discovers joinable sessions.

Back-ends expose sessions through `INetworkSession` and gamers through `INetworkGamer` / `ILocalNetworkGamer`.

That lets your game loop and message handlers stay the same while the transport changes.

## Default back-end

The default UDP/SystemLink path is still available and remains the fallback when no platform back-end is selected.

```csharp
NetworkServiceProvider.ResetToDefault();
```

## Shared back-end guidance

- Keep game-facing behavior the same across back-ends, even if the transport internals differ.
- Prefer adapter layers first when wrapping existing code.
- Start with a small vertical slice before a larger platform API integration.
- Keep back-end-specific code isolated so package dependencies stay clean.

## Back-end docs

If you are implementing or consuming a specific back-end, use the per-back-end README instead of this root file:

- [Steam back-end README](Steam/README.md)
- [Android back-end README](Android/README.md)
- [iOS back-end README](iOS/README.md)

## Status

- Core networking abstraction layer: implemented
- UDP/SystemLink factory + adapter: implemented
- Steam back-end: implemented and covered by tests
- Android back-end: implemented and covered by tests
- iOS back-end: implemented and covered by tests
### Default UDP/SystemLink
