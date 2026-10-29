# Versioning and compatibility

This is the policy the first publish follows. Where a choice is still open it says so.

## Packages

| Package | Contains |
|---------|----------|
| `MonoGame.Xna.Framework.Net` | the XNA-shaped API, the session code, the LAN back-end |
| `MonoGame.Xna.Framework.Net.Steam` | Steam lobbies and P2P, leaderboards, achievements |
| `MonoGame.Xna.Framework.Net.EOS` | Epic Online Services lobbies and P2P (**not published yet**, see "Epic" below) |
| `MonoGame.Xna.Framework.Net.Android` | Nearby Connections and Play Games |
| `MonoGame.Xna.Framework.Net.iOS` | Game Center |

All five carry **the same version number** and each back-end depends on the core package at that version or newer, so a game
updates them together. A back-end release that changes nothing is still published, to keep the numbers equal. (The EOS
package keeps the number but is not published yet.)

## Version numbers

[Semantic Versioning 2.0](https://semver.org). The version is in each package's `Directory.Build.props`; CI adds a date to
non-release builds (`1.0.0-preview.1-2026-10-29`) and uses the tag for releases (`v1.0.0-preview.1`).

- **The first version is `1.0.0-preview.1`.** Nothing was published before it, so there is no 0.x history to continue from.
- **Previews (`1.0.0-preview.N`)**: anything may change from one preview to the next, the API and the wire protocol included.
- **From `1.0.0`**: a **major** version for any breaking change, a **minor** for additions, a **patch** for fixes.

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
| Joining and session messages | `JoinRequestMessage.CURRENT_PROTOCOL_VERSION` | 1 |
| LAN discovery packets | `DiscoveryPacket.PROTOCOL_VERSION` | 1 |

Rules:

- **Any change to what goes on the wire bumps the number.** This includes new or reordered fields, a new framing, or a change to
  the encryption.
- **Peers must have the same number.** A host refuses a join from a different one with `NetworkSessionJoinError.ProtocolVersionMismatch`,
  and the message gives both numbers ("Host: v2, Client: v1"), so a game can tell the player which side to update.
- **There is no negotiation between versions**, so a game's players all have to be on a library release with the same
  protocol number. This is the plan for 1.0: games of this kind ship to everyone at once, and supporting two protocols at
  the same time doubles the testing. It can be revisited if a game needs rolling updates.
- A package release that changes the protocol number is listed as such in the changelog. At least a minor version bump, and
  a major one from 1.0 on.

The first published release is protocol **1**. The builds that were used during development had other numbers (1 to 5, for
formats that never shipped); they are not safe to mix with a release, even the one that also says 1.

| Package version | Protocol |
|-----------------|----------|
| the first published release | 1 |

## What is supported

- **.NET 10** (`net10.0`, `net10.0-android36.0`, `net10.0-ios26.0`). Android and iOS accept only the current platform version
  of the .NET 10 workloads, so a game targeting them needs those. Older .NET versions are not supported (open: whether to add
  `net8.0` for the core, Steam and EOS packages).
- **MonoGame 3.8.x.** The packages compile against MonoGame but do not depend on it, so the game must reference
  `MonoGame.Framework` for its platform. Older MonoGame versions are untested.
- Platforms and what has actually been run against them are in the README.

## Epic

The EOS package contains Epic's SDK source and native libraries, which are under Epic's own terms, not this repository's MIT
license. **It is not published yet**: the decision is to wait until those terms have been checked against redistributing them
in a package. The workflow's EOS publish job is switched off (`if: ${{ false }}`), and the package still packs in CI so it stays
buildable. Before it is published, its license metadata must say what it contains. Until then the EOS back-end is used by
referencing the project or building it from source.
