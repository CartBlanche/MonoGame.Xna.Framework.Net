# Changelog

Newest first. See `VERSIONING.md` for what a version number means.

## 1.0.0-preview.1 (not yet published)

Wire protocol **1**. Nothing has been published before this, so the breaking changes below are relative to the development builds the
playground games were written against (labelled 0.3.1 in the project files), and matter to those games, not to anyone else.
Development builds used protocol numbers 1 to 5 for formats that never shipped; do not mix them with this release.

### Breaking changes

- `NetworkGamer.Id` is XNA's `byte`. The string that used to be `Id` is `GamerId`. Code that passed `gamer.Id` to something taking
  any object (`Array.IndexOf`, `Contains`, `Equals`, string concatenation) still compiles but no longer matches.
- `NetworkGamer` derives from `Gamer`; its `Gamertag` is read-only.
- `AvailableNetworkSession` takes a `PeerId` for the host (`UdpTransport.ToPeer(endpoint)`), not an `IPEndPoint`.
- `PacketWriter.Write(byte[])` writes the raw bytes, as XNA does. Use `WriteLengthPrefixed` and `ReadLengthPrefixedBytes` for the old behaviour.
- `INetworkSession`, the `Begin*`/`End*` session methods and the per-platform session classes are gone. Games use `NetworkSession`;
  a platform supplies an `INetworkBackend` (`NetworkServiceProvider.SetBackend`, or the platform's `...PlatformBootstrap.Configure`).
- `QualityOfService` uses XNA's names (`AverageRoundtripTime`, `MinimumRoundtripTime`, `BytesPerSecondDownstream`, `BytesPerSecondUpstream`).
- `NetworkException`, `GamerPrivilegeException` and `InviteAcceptedEventArgs` moved from `Microsoft.Xna.Framework.Net` to `Microsoft.Xna.Framework.GamerServices`.
- `SteamBackend` and `SteamP2PTransport` moved into `Microsoft.Xna.Framework.Net.Steam`, as the other back-ends' types are.
- `SendDataOptions` is a `[Flags]` enum and has `Chat`.
- A session's default logger writes to the debugger output, not the console (`NetworkLogging.Logger`).
- A session refuses datagrams that are not authenticated for their link.
- Events are raised from `Update()`.

### Added

- Session passwords (`CreateAsync(..., password)`, `JoinAsync(session, password)`) and authenticated links: every datagram between the host
  and a client is protected with AES-GCM, and unreadable to someone watching if a password is set.
- A hardened receive path: malformed datagrams are dropped, senders are checked, join requests are rate-limited.
- Online session types (`PlayerMatch`, `Ranked`) on Steam, EOS and iOS; session properties in lobbies; real invitations; session end reasons.
- `NetworkLogging.Logger`, `NetworkSession.ResetReady`, `FindGamerById`, `IsDisposed`, `HostChanged` (never raised: no host migration),
  `GamerServicesDispatcher`, `Gamer.IsDisposed`.
- `SimulatedLatency` and `SimulatedPacketLoss` take effect on what a machine sends (a fixed latency keeps datagrams in order).
- Nullable reference types and analyzers on the core package.
- `NetworkSession.GameId` (and `SessionAdvertisement.GameId`): names the game, so that on a LAN a game finds only its own
  sessions. Without it every game that uses the library listed, and could join, every other game's sessions. Empty by default;
  set it to the same text in every build of the game.
- `COMPATIBILITY.md` and `Samples/DataPath`, a data-path acceptance test.

### Fixed

- `SendData` to everyone did not deliver to the sender, but XNA's does ("This includes the gamer sending the packet"). A game that
  sets itself up from the packet it sends to everyone (NetRumble's host does) never started. Games that discard packets from local
  gamers, as XNA's samples do, are unaffected; a game written against the old behaviour must now skip `sender.IsLocal`.
- A LAN host announced the usual game port even when its transport had to bind another because a second session on the machine had
  the first, so the second host's joiners were sent to the first host (and rejected, or joined the wrong game).
- Disposing a session now tells the other machines at once (it used to be `Leave()` only).
- `SignedInGamer.Privileges` was never assigned.
- The local achievement and leaderboard files are written with source-generated JSON (reflection is unsafe once an iOS app is trimmed).
- `Guide.ShowMessageBox` could deadlock on a thread with a synchronization context.
- The EOS native library could not be found when running from source.
