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

- Core/: core package — `Net/` (networking types, messages, abstractions, adapters, factories, and services) and `GamerServices/` (XNA-style gamer, guide, and leaderboard services).
- Steam/: Steam back-end package.
- Android/: Android and Play Games back-end package.
- iOS/: iOS and Game Center back-end package.
- EOS/: Epic Online Services back-end package.
- Tests/: unit and integration tests.

## Core networking model

Games use `NetworkSession` (the XNA-shaped API) and never talk to a platform directly. What changes between
platforms is the back-end, `INetworkBackend`, which only supplies what is platform-specific:

- a transport (`IPeerTransport`) that moves datagrams to peers named by an opaque `PeerId`
- a way to advertise a hosted session and to find sessions to join

Everything else (the lobby, readiness, reliable and ordered delivery, fragmentation, relaying between
clients, gamer ids) is `NetworkSession`, the same on every back-end.

- `NetworkServiceProvider.SetBackend(...)` selects the active back-end; each platform package's
  `...PlatformBootstrap.Configure(...)` does it for you.
- `NetworkSession.CreateAsync`, `FindAsync` and `JoinAsync` then run over that back-end.

That means your game loop and message handlers stay the same while the transport changes.

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
- EOS: implemented and covered by tests

## Back-end setup guides

Use the back-end README files for platform setup requirements and verification steps for login, leaderboards, achievements, and multiplayer/session discovery:

- [Steam back-end README](Steam/README.md)
- [Android back-end README](Android/README.md)
- [iOS back-end README](iOS/README.md)
- [EOS back-end README](EOS/README.md)

## Shared implementation guidance

- Keep game-facing behavior consistent across back-ends.
- Prefer adapter layers first when wrapping existing code.
- Start with a small vertical slice before deep platform integration.
- Keep back-end-specific code isolated so package dependencies stay clean.
