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

### Steam 

- Factory: `SteamNetworkSessionFactory`
- Session implementation: `SteamNetworkSession`
- Gamer model: `SteamNetworkGamer`
- Bootstrap helper: `SteamPlatformBootstrap`
- Runtime wrapper: `SteamRuntime`

The current Steam session is an in-memory vertical slice that validates host/find/join/reliable-message behavior using the same abstraction seams used by a full Steamworks transport.

## Add a new back-end

Use Steam as the reference pattern.

### 1) Implement the factory

Create a class that implements `INetworkSessionFactory`.

Responsibilities:

- Return a back-end name in `BackendName`.
- Create session objects in `CreateSession()`.
- Return discoverable sessions in `FindSessionsAsync(...)`.

### 2) Implement the session

Create a class that implements `INetworkSession`.

Minimum responsibilities:

- Session lifecycle: `CreateAsync`, `JoinAsync`, `CloseAsync`, `Dispose`.
- Messaging: `SendMessage`, `BroadcastMessage`, `Update`.
- Gamer model exposure through `AllGamers` and `LocalGamer`.
- Raise events (`MessageReceived`, `GamerJoined`, `GamerLeft`, `GameStarted`, `GameEnded`, `SessionEnded`) at the same points game code expects.

### 3) Implement gamer types

Implement `INetworkGamer` and `ILocalNetworkGamer` for your back-end.

Keep behavior consistent with other back-ends:

- Stable gamer identity (`Id`)
- Display name (`Gamertag`)
- Host/local flags
- Readiness state

### 4) Wire it at startup

In your game initialization, choose the factory:

```csharp
NetworkServiceProvider.SetSessionFactory(new YourBackendSessionFactory());
```

After this, your game code should use `NetworkServiceProvider.SessionFactory` to create/find/join sessions.

### 5) Reuse shared message contracts

Keep your message types implementing `INetworkMessage` and register them through `NetworkMessageRegistry`.

This keeps payload formats shared across back-ends and reduces duplicate code.

### 6) Add tests like Steam tests

Use the existing test style as a template:

- End-to-end host/find/join flow
- Reliable message send/receive flow
- Composition/root wiring test for startup configuration

## Typical startup examples

### Default UDP/SystemLink

```csharp
NetworkServiceProvider.ResetToDefault();
```

### Steam

```csharp
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;

SteamPlatformBootstrap.Configure(gameName: "MyGame");
// Optional in your update loop when Steam is enabled:
// SteamRuntime.RunCallbacks();
```

## Practical guidance when adding back-ends

- Keep game-facing behavior the same across back-ends, even if transport internals differ.
- Prefer adapter layers first (as done by UDP) when wrapping existing code.
- Start with a small vertical slice (as done by Steam) before full platform API integration.
- Keep backend-specific code isolated so package dependencies stay clean.

## Status

- Core networking abstraction layer: implemented
- UDP/SystemLink factory + adapter: implemented
- Steam backend vertical slice: implemented and covered by tests
- Additional back-ends: ready to be added through the same factory/session/gamer pattern
