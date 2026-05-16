# MonoGame.Xna.Framework.Net

MonoGame.Xna.Framework.Net is a modern, XNA-style networking layer for MonoGame.

It keeps the familiar Microsoft.Xna.Framework.Net and GamerServices APIs while making the transport layer pluggable, so your game code can stay stable while the back-end changes.

## Project goals

- Keep the classic XNA networking feel for existing game code.
- Support multiple networking back-ends behind one common API.
- Let games switch back-ends at startup.
- Keep message contracts shared across all back-ends.
- Make host/find/join/message flows easy to test end-to-end.

## Repository layout

- Net/: core networking types, messages, abstractions, adapters, factories, and services.
- GamerServices/: XNA-style gamer, guide, and leaderboard services.
- Steam/: Steam back-end package.
- Android/: Android and Play Games back-end package.
- iOS/: iOS and Game Center back-end package.
- Tests/: unit and integration tests.

## Core networking model

The main extension point is INetworkSessionFactory.

At runtime, the game selects one active factory through NetworkServiceProvider.

- NetworkServiceProvider.SetSessionFactory(...) selects the active back-end.
- NetworkServiceProvider.SessionFactory.CreateSession() creates a session for that back-end.
- NetworkServiceProvider.SessionFactory.FindSessionsAsync(...) discovers joinable sessions.

All back-ends expose sessions through INetworkSession and gamers through INetworkGamer and ILocalNetworkGamer.

That means your game loop and message handlers can remain the same while the transport changes.

## Default back-end

The default UDP and SystemLink path stays available as the fallback when no platform back-end is selected.

```csharp
NetworkServiceProvider.ResetToDefault();
```

## Current back-ends

- UDP/SystemLink: implemented
- Steam: implemented and covered by tests
- Android: implemented and covered by tests
- iOS: implemented and covered by tests

## Back-end setup guides

Use the backend README files for platform setup requirements and verification steps for login, leaderboards, achievements, and multiplayer/session discovery:

- [Steam back-end README](Steam/README.md)
- [Android back-end README](Android/README.md)
- [iOS back-end README](iOS/README.md)

## Shared implementation guidance

- Keep game-facing behavior consistent across back-ends.
- Prefer adapter layers first when wrapping existing code.
- Start with a small vertical slice before deep platform integration.
- Keep back-end-specific code isolated so package dependencies stay clean.
