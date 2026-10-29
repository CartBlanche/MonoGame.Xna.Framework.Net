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

What each back-end has actually been run against, as of 2026-10-29. "Written" means it builds and its unit tests pass, but it has not run against the real platform.

| Back-end | Carries | Status |
|----------|---------|--------|
| UDP / SystemLink (default) | `SystemLink` | **Works.** Core, compatibility (53) and conformance suites pass over real sockets, including password joins and forged-datagram rejection. a two-machine play-through of ShipGame is pending |
| Steam | all session types | A hosted session becoming a lobby that carries its type and properties is **verified against the live Steam client** (one account, app 480). The password flag was added afterwards and has only unit tests. Finding and joining a lobby and receiving a real invitation are **not verified** (they need a second account). Gameplay traffic uses Steam P2P |
| EOS | all session types | **Written, never run against an Epic account.** The native library loads on macOS arm64 (tested). Lobbies, P2P, invitations, sign-in, leaderboards and achievements need your Epic sandbox and two accounts to verify |
| Android | `SystemLink` (over Nearby Connections, or the LAN) | The LAN path **passes 14 of 14 tests on an emulator**. Nearby Connections and Play Games sign-in are **not verified** (they need two devices with Google Play services) |
| iOS | all session types (Game Center) | **Builds.** Game Center (`GKMatch`) is **not verified**, and the iOS test app currently finds 0 tests, so nothing runs on the platform yet |

All five share the same `NetworkSession`: the lobby, readiness, reliable and ordered delivery, fragmentation, relaying and security are the same code whichever back-end carries the datagrams. Only discovery and the transport are platform code.

## Compatibility with XNA

The API follows XNA Game Studio 4.0's `Microsoft.Xna.Framework.Net` and `GamerServices`. [COMPATIBILITY.md](COMPATIBILITY.md) lists every member that is the same, different or missing, the behaviour that differs, and what to change when moving from an earlier version of this library.

## Security

- **Session password.** `NetworkSession.CreateAsync(..., password)` makes a session that joiners need a password for; `JoinAsync(session, password)` supplies it. The host challenges the joiner, who proves they know the password; the password never crosses the network. A wrong or missing password is refused with `NetworkSessionJoinError.WrongPassword` or `PasswordRequired`, and the lobby entry says a password is needed (`AvailableNetworkSession.IsPasswordProtected`).
- **Authenticated links.** Every datagram between the host and a client is protected with AES-GCM under a key the host issues at join, so a forged or tampered datagram is dropped. With a password, the key is sealed under it, so the traffic is also unreadable to someone watching the network. **Without a password the key travels in the clear:** that stops forgery by someone who cannot read the traffic, but not by someone who can (on the same LAN, for instance). Use a password where that matters.
- **Hardened receive path.** Malformed datagrams are dropped without throwing, senders are checked against the gamer they claim to be, and join requests are rate-limited per address and overall.

## Logging

The framework logs through `NetworkLogging.Logger`. The default writes to the debugger output, which a release build discards. Assign your own `INetworkLogger` (or `NullNetworkLogger`) once at start-up; a session may also be given its own with `session.Logger`.

```csharp
NetworkLogging.Logger = new MyFileLogger();
```

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
