# XNA compatibility

How closely this library follows XNA Game Studio 4.0's `Microsoft.Xna.Framework.Net` and `Microsoft.Xna.Framework.GamerServices`.

**How this was checked (2026-10-29).** The public API of the built `Core` assembly was listed by reflection and compared, member by member, with the archived XNA 4.0 member lists on Microsoft Learn for `NetworkSession`, `NetworkGamer`, `LocalNetworkGamer`, `AvailableNetworkSession`, `QualityOfService`, `NetworkMachine`, `NetworkSessionProperties`, `PacketReader`, `PacketWriter`, `Gamer`, `SignedInGamer`, `Guide`, the enums `NetworkSessionType`, `NetworkSessionState`, `NetworkSessionJoinError`, `NetworkSessionEndReason` and `SendDataOptions`, and the type lists of both namespaces. Achievements, leaderboards, presence, `GamerCollection<T>`, `GamerPrivileges` and `GamerServicesComponent` were **not** compared member by member. If you find a difference that is not listed, it is a bug in this page.

| Word | Meaning |
|------|---------|
| **Yes** | Present, same name and shape |
| **Extra** | Ours only; code written for XNA does not need it |
| **Differs** | Present but not source-compatible with XNA |
| **Missing** | In XNA, not here |
| **n/a** | Needs Xbox LIVE or console hardware; no equivalent is planned |

## Behaviour that differs from XNA

- **Events are raised from `Update()`**, on the thread that calls it, as XNA does. Framework state (the lobby, readiness) still changes at once.
- **`SendData` never delivers to the sender** (no echo).
- **The topology is a star.** Data between two clients goes through the host, which adds one hop.
- **Unreliable and in-order-only datagrams are limited to 1100 bytes** (reliable data of any size is split for you).
- **`AllowHostMigration` is accepted but migration is not built.** If the host leaves, the session ends with `HostEndedSession`.
- **No voice.** `HasVoice`, `IsTalking` and `IsMutedByLocalUser` exist but are always `false` (planned as an optional add-on).
- **`QualityOfService` returns fixed placeholder numbers**, not a measurement.
- **`SimulatedLatency` and `SimulatedPacketLoss` can be set but have no effect.**
- **`BytesPerSecondReceived` and `BytesPerSecondSent` are averages over the whole life of the session**, not a recent rate.
- **`Begin*`/`End*` pairs are replaced by `*Async` methods** (`CreateAsync`, `FindAsync`, `JoinAsync`, `JoinInvitedAsync`). The blocking `Create`, `Find`, `Join` and `JoinInvited` remain.
- **Disposing a session tells the other machines at once** (one datagram each; if it is lost they find out by the 9-second heartbeat timeout). A process that crashes says nothing, so the others notice only after that timeout.
- **Sessions can have a password and every datagram on a link is authenticated** (see the README). XNA had neither.

## `Microsoft.Xna.Framework.Net`

### Types

| XNA type | Here |
|----------|------|
| `NetworkSession`, `LocalNetworkGamer`, `NetworkGamer`, `NetworkMachine`, `AvailableNetworkSession`, `AvailableNetworkSessionCollection`, `NetworkSessionProperties`, `PacketReader`, `PacketWriter`, `QualityOfService` | Yes, with the differences below |
| `GameEndedEventArgs`, `GameStartedEventArgs`, `GamerJoinedEventArgs`, `GamerLeftEventArgs`, `NetworkSessionEndedEventArgs`, `NetworkSessionJoinException` | Yes |
| `HostChangedEventArgs` | Yes (but never raised, see `HostChanged`) |
| `WriteLeaderboardsEventArgs` | n/a |
| `NetworkSessionType`, `NetworkSessionState`, `NetworkSessionEndReason`, `NetworkSessionJoinError`, `SendDataOptions` | Yes, see Enums |

### `NetworkSession`

| XNA member | Here | Notes |
|------------|------|-------|
| `AllGamers`, `LocalGamers`, `RemoteGamers`, `Host`, `IsHost`, `IsEveryoneReady` | Yes | `AllGamers` and `RemoteGamers` are `GamerCollection`, which derives from `GamerCollection<NetworkGamer>` |
| `MaxGamers`, `PrivateGamerSlots`, `SessionState`, `SessionType` | Yes | |
| `AllowJoinInProgress` | Yes | |
| `AllowHostMigration` | Differs | Accepted, but migration is not built |

| `SessionProperties` | Differs | `IDictionary<string, object>`, not `NetworkSessionProperties` (the blocking `Create` and `Find` do take `NetworkSessionProperties`) |
| `BytesPerSecondReceived`, `BytesPerSecondSent` | Differs | Average since the session started |
| `SimulatedLatency`, `SimulatedPacketLoss` | Differs | Can be set; nothing uses them yet |
| `IsDisposed` | Missing | |
| `MaxPreviousGamers`, `MaxSupportedGamers`, `PreviousGamers` | Missing | |
| `Create`, `Find`, `Join`, `JoinInvited` | Yes | Blocking |
| `BeginCreate`/`EndCreate`, `BeginFind`/`EndFind`, `BeginJoin`/`EndJoin`, `BeginJoinInvited`/`EndJoinInvited` | Differs | Replaced by `CreateAsync`, `FindAsync`, `JoinAsync`, `JoinInvitedAsync` |
| `Dispose`, `StartGame`, `EndGame`, `Update` | Yes | |
| `AddLocalGamer` | Missing | |
| `ResetReady` | Yes | Host only, as in XNA: throws `InvalidOperationException` elsewhere |
| `FindGamerById` | Differs | On the collection: `session.AllGamers.FindGamerById(id)` |
| `GameStarted`, `GameEnded`, `GamerJoined`, `GamerLeft`, `SessionEnded`, static `InviteAccepted` | Yes | Raised from `Update()` |
| `HostChanged` | Differs | Can be subscribed to, but is never raised: it is only raised by host migration, which is not built |
| `WriteArbitratedLeaderboard`, `WriteTrueSkill`, `WriteUnarbitratedLeaderboard` | n/a | |
| (none) | Extra | `CreateAsync` with a `password`, `JoinAsync` with a `password`, `Password`, `IsPasswordProtected`, `Leave`, `Cancel`, `DisposeAsync`, `MessageReceived`, `EventDispatchMode`, `Logger`, `Diagnostics`, `NetworkTransport` |

### `NetworkGamer` and `LocalNetworkGamer`

`NetworkGamer` derives from `Gamer`, as in XNA.

| XNA member | Here | Notes |
|------------|------|-------|
| `DisplayName`, `Gamertag`, `Tag`, `Id`, `IsHost`, `IsLocal`, `IsReady`, `Machine`, `RoundtripTime` | Yes | `Id` is a `byte`, as in XNA |
| `HasVoice`, `IsTalking`, `IsMutedByLocalUser` | Differs | Always `false` |
| `IsDisposed`, `LeaderboardWriter`, `GetProfile`/`BeginGetProfile`/`EndGetProfile` | Missing | |
| `HasLeftSession`, `IsGuest`, `IsPrivateSlot`, `Session` | Missing | |
| `IsDataAvailable`, `ReceiveData`, `SendData` | Yes | On every `NetworkGamer`, not only `LocalNetworkGamer` (a superset) |
| `SignedInGamer` | Yes | On every `NetworkGamer`; `null` for remote gamers |
| `LocalNetworkGamer.EnableSendVoice`, `SendPartyInvites` | Missing | |
| (none) | Extra | `GamerId` (string, unique across machines), `UnacknowledgedAge` |

### `AvailableNetworkSession` and `QualityOfService`

| XNA member | Here | Notes |
|------------|------|-------|
| `CurrentGamerCount`, `HostGamertag`, `OpenPrivateGamerSlots`, `OpenPublicGamerSlots` | Yes | |
| `SessionProperties` | Differs | `IDictionary<string, object>` |
| `QualityOfService` | Differs | Placeholder values (see the next rows for its members) |
| (none) | Extra | `SessionId`, `SessionName`, `HostPeer`, `IsPasswordProtected`, and a public constructor |
| `QualityOfService.IsAvailable`, `AverageRoundtripTime`, `MinimumRoundtripTime`, `BytesPerSecondDownstream`, `BytesPerSecondUpstream` | Yes | Fixed placeholder values. `MaximumRoundtripTime` is an extra |

### Other classes

| XNA type | Here | Notes |
|----------|------|-------|
| `NetworkMachine` (`Gamers`, `RemoveFromSession`) | Yes | |
| `NetworkSessionProperties` (`Count`, indexer, `GetEnumerator`) | Yes | Also behaves like a list |
| `PacketWriter` (`Length`, `Position`, `Write(...)` for every XNA type) | Yes | `Write(byte[])` writes the raw bytes, as XNA does; `WriteLengthPrefixed` is an extra |
| `PacketReader` (`Length`, `Position`, `Read*` for every XNA type) | Yes | `ReadLengthPrefixedBytes` is an extra |
| `PacketWriter` and `PacketReader` as `BinaryWriter` and `BinaryReader` | Differs | They are not subclasses: `BaseStream`, `Flush`, `PeekChar` and `Read(...)` are missing |

### Enums and exceptions

| XNA | Here |
|-----|------|
| `NetworkSessionType`: `Local`, `LocalWithLeaderboards`, `SystemLink`, `PlayerMatch`, `Ranked` | Differs: `LocalWithLeaderboards` is missing. `Ranked` and `PlayerMatch` need Steam, EOS or iOS; the LAN back-end carries `SystemLink` only |
| `NetworkSessionState`: `Lobby`, `Playing`, `Ended` | Yes, plus `Creating`, `Joining` |
| `NetworkSessionEndReason`: `ClientSignedOut`, `HostEndedSession`, `RemovedByHost`, `Disconnected` | Yes, plus `NetworkError` |
| `NetworkSessionJoinError`: `SessionNotFound`, `SessionNotJoinable`, `SessionFull` | Yes, plus `Unknown`, `ProtocolVersionMismatch`, `Timeout`, `PasswordRequired`, `WrongPassword` |
| `SendDataOptions`: `None`, `Reliable`, `InOrder`, `ReliableInOrder`, `Chat` | Yes. `Chat` combines with the others and changes nothing (every link is authenticated) |
| `NetworkException`, `NetworkNotAvailableException`, `GamerPrivilegeException`, `InviteAcceptedEventArgs` | Yes (in `GamerServices`, as in XNA) |
| `GamerServicesNotAvailableException`, `GuideAlreadyVisibleException`, `GameUpdateRequiredException` | Missing |

## `Microsoft.Xna.Framework.GamerServices`

| XNA type | Here | Notes |
|----------|------|-------|
| `Gamer` | Differs | Has `DisplayName`, `Gamertag`, `Tag` and static `SignedInGamers`. Missing: `IsDisposed`, `LeaderboardWriter`, `GetProfile`, `GetFromGamertag` and their `Begin`/`End` pairs |
| `SignedInGamer` | Differs | Has `Gamertag`, `DisplayName`, `IsGuest`, `IsSignedInToLive`, `PlayerIndex`, `Presence`, `Privileges`, `GetAchievements`. Missing: `GameDefaults`, `PartySize`, `GetFriends`, `IsFriend`, `IsHeadset`, `AwardAchievement` and the `Begin`/`End` achievement methods, and the static `SignedIn` and `SignedOut` events. Extras: `Current`, `*Async` methods, `WriteLeaderboard`, `GetLeaderboard` |
| `GamerCollection<T>` | Present | Not compared member by member |
| `GamerPresence` | Present | Has `PresenceMode` and `PresenceValue`; not compared member by member |
| `GamerPrivileges` | Differs | Only `AllowOnlineSessions` (true while signed in to the online service) |
| `GamerServicesComponent` | Present | Not compared member by member |
| `GamerServicesDispatcher` | Missing | |
| `Guide` | Differs | Has `IsVisible`, `IsTrialMode`, `IsScreenSaverEnabled`, `ShowSignIn`, `ShowMarketplace`, `ShowMessageBox`. Missing: `NotificationPosition`, `SimulateTrialMode`, `BeginShowKeyboardInput`/`EndShowKeyboardInput`, `BeginShowMessageBox`/`EndShowMessageBox` (use `ShowMessageBoxAsync`), `DelayNotifications` and the `ShowFriends`, `ShowGameInvite`, `ShowGamerCard`, `ShowMessages`, `ShowParty`, `ShowPlayers`... family |
| `Achievement`, `AchievementCollection` | Differs | Not compared member by member; backed by Steam, EOS or Play Games achievements, or a local file |
| `LeaderboardIdentity`, `LeaderboardReader`, `LeaderboardWriter`, `LeaderboardEntry` | Differs | Not the XNA shape; backed by the platform's leaderboards or a local file. Not compared member by member |
| `FriendCollection`, `FriendGamer`, `GamerProfile`, `GameDefaults`, `PropertyDictionary`, `SignedInEventArgs`, `SignedOutEventArgs` | Missing | |
| Avatar types and enums | n/a | |

## Migrating from earlier versions of this library

- `NetworkGamer.Id` is now XNA's `byte`. The string that used to be `Id` is `GamerId`.
  **Watch for silent breaks:** code that passed `gamer.Id` to something that takes any object (`Array.IndexOf(stringArray, gamer.Id)`, `list.Contains(gamer.Id)`, `Equals`, `object ==`, string concatenation) still compiles, but a byte never equals a string, so it quietly stops matching. Search for every `.Id` that is not a compile error. ShipGame lost its "has left" notices this way.
- `NetworkGamer.Gamertag` is read-only, and `NetworkGamer` derives from `Gamer`.
- `QualityOfService` uses XNA's names: `AverageRoundtripTime`, `MinimumRoundtripTime`, `BytesPerSecondDownstream` (was `...Received`) and `BytesPerSecondUpstream` (was `...Sent`).
- `NetworkException`, `GamerPrivilegeException` and `InviteAcceptedEventArgs` moved from `Microsoft.Xna.Framework.Net` to `Microsoft.Xna.Framework.GamerServices`, where XNA has them.
- `SendDataOptions` is a `[Flags]` enum with `Chat`.
- `AvailableNetworkSession` takes a `PeerId` for the host, not an `IPEndPoint`: `UdpTransport.ToPeer(endpoint)`.
- `PacketWriter.Write(byte[])` writes the raw bytes. Use `WriteLengthPrefixed` and `ReadLengthPrefixedBytes` for the old behaviour; `ReadBytes()` without a count is gone.
- `INetworkSession` is gone. Games use `NetworkSession`; a platform supplies an `INetworkBackend`.
- `NetworkSession`'s default logger writes to the debugger output, not the console. Set `NetworkLogging.Logger` to change it.
- A session now refuses datagrams that are not authenticated for their link. Code that injects raw datagrams must use the transport directly.
