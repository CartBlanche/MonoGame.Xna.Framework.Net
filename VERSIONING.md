# Versioning and compatibility

This is the policy the first publish follows. Where a choice is still open it says so.

## Packages

| Package | Contains |
|---------|----------|
| `MonoGame.Xna.Framework.Net` | the XNA-shaped API, the session code, the LAN back-end |
| `MonoGame.Xna.Framework.Net.Steam` | Steam lobbies and P2P, leaderboards, achievements |
| `MonoGame.Xna.Framework.Net.EOS` | Epic Online Services lobbies and P2P (see "Epic" below) |
| `MonoGame.Xna.Framework.Net.Android` | Nearby Connections and Play Games |
| `MonoGame.Xna.Framework.Net.iOS` | Game Center |

All five carry **the same version number** and each back-end depends on the core package at that version or newer, so a game
updates them together. A back-end release that changes nothing is still published, to keep the numbers equal.

## Version numbers

[Semantic Versioning 2.0](https://semver.org). The version is in each package's `Directory.Build.props`; CI adds a date to
non-release builds (`0.3.1-2026-10-29`) and uses the tag for releases (`v0.4.0-preview.1`).

- **Before 1.0 (`0.x`)**: a minor version may break the public API; a patch version does not. Previews are `-preview.N`.
- **From 1.0**: a **major** version for any breaking change, a **minor** for additions, a **patch** for fixes.

## What the public API is

Everything listed in `Tests/*/PublicApiBaselines/`: public and protected members of public types in the
`Microsoft.Xna.Framework` namespaces. A test per package compares the built assembly with that list, so a change cannot
slip in. If a change is intended:

1. look at the added and removed lines the test prints;
2. decide what it means for the version (removing or renaming anything, or changing a signature, is breaking; adding is not);
3. run the tests with `UPDATE_API_BASELINE=1` to accept it, and add a line to `CHANGELOG.md`.

Not covered by the baselines: nullable annotations, and behaviour that the signatures do not show. A change in behaviour that
games can see (an exception that used to be thrown, an event raised at a different time) is breaking even if no signature changed,
and goes in the changelog the same way.

After 1.0, something is marked `[Obsolete]` for at least one minor version before it is removed in the next major.

## The wire protocol

The network protocol has its own numbers, separate from the package version:

| What | Constant | Now |
|------|----------|-----|
| Joining and session messages | `JoinRequestMessage.CURRENT_PROTOCOL_VERSION` | 5 |
| LAN discovery packets | `DiscoveryPacket.PROTOCOL_VERSION` | 1 |

Rules:

- **Any change to what goes on the wire bumps the number.** This includes new or reordered fields, a new framing, or a change to
  the encryption.
- **Peers must have the same number.** A host refuses a join from a different one with `NetworkSessionJoinError.ProtocolVersionMismatch`,
  and the message gives both numbers ("Host: v5, Client: v4"), so a game can tell the player which side to update.
- **There is no negotiation between versions**, so a game's players all have to be on a library release with the same
  protocol number. This is the plan for 1.0: games of this kind ship to everyone at once, and supporting two protocols at
  the same time doubles the testing. It can be revisited if a game needs rolling updates.
- A package release that changes the protocol number is listed as such in the changelog. At least a minor version bump, and
  a major one from 1.0 on.

| Package version | Protocol |
|-----------------|----------|
| 0.3.1 | 1 |
| 0.4.0 previews and later | 5 |

## What is supported

- **.NET 10** (`net10.0`, `net10.0-android36.0`, `net10.0-ios26.0`). Android and iOS accept only the current platform version
  of the .NET 10 workloads, so a game targeting them needs those. Older .NET versions are not supported (open: whether to add
  `net8.0` for the core, Steam and EOS packages).
- **MonoGame 3.8.x.** The packages compile against MonoGame but do not depend on it, so the game must reference
  `MonoGame.Framework` for its platform. Older MonoGame versions are untested.
- Platforms and what has actually been run against them are in the README.

## Epic

The EOS package contains Epic's SDK source and native libraries, which are under Epic's own terms, not this repository's MIT
license. Until those terms have been checked against redistributing them in a package, the EOS package is not published with
the others (the workflow publishes it only for an `eos-v*` tag or a manual run), and its metadata must say what it contains.
