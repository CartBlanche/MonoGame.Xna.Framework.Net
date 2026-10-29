# Implementation Plan

Goal: **API-compatible with `Microsoft.Xna.Framework.Net` / `GamerServices` on the surface, modern and
pluggable underneath.** The public XNA-shaped API is a frozen contract; everything below it may be
rewritten freely as long as the compat suite (Phase 0) stays green.

This file is the single source of truth for progress. Update it in the same commit as the work it describes.

Legend: `[-]` dropped · `[ ]` todo · `[~]` in progress · `[x]` done · `[?]` needs verification before work starts · `(!)` decision needed

_Last updated: 2026-10-29 (Phases 0-6 and 8 complete, 7 deferred, 9 in progress; Steam finding/joining and invites, and the EOS/Android/iOS platform paths, are unverified on real runtimes; the iOS test app needs attention)_

---

## Status at a glance

| Phase | Theme | Status | Tasks done |
|------:|-------|--------|-----------|
| 0 | Safety net: CI runs tests, compat suite scaffold | **Done** | 8/8 |
| 1 | Wire protocol correctness (envelope, InOrder, fragmentation) | **Done** | 7/7 |
| 2 | Threading and event model (`Update()`-drained events) | Done | 6/6 |
| 3 | XNA API parity (gamers, packets, sessions) | Done (2 deferred, 1 dropped) | 16/19 |
| 4 | Backend unification (one public API, peer-id transport) | Done (4.4 unverified on devices) | 6/6 |
| 5 | Online session types, matchmaking, invites | Done (Steam/EOS paths unverified live) | 5/5 |
| 6 | Security and robustness | **Done** | 5/5 |
| 7 | Voice (pluggable) | Deferred until after 9 | 0/7 |
| 8 | Modernisation and cleanup | **Done** (8.2 skipped) | 7/8 |
| 9 | Samples, docs, release | In progress (9.3, 9.4 done) | 2/7 |
| 10 | Dedicated/headless server (optional) | Not started | 0/6 |

### Baseline (already done, commit `20aa893` "Lan fixes for more reliable UDP")
- [x] Broadcast on every network adapter (`LanBroadcast`), `SystemLinkSessionManager` fixes
- [x] `AllowJoinInProgress` enforced, join-rejection reported
- [x] Reliable delivery over UDP: per-gamer `ReliableChannel` (seq numbers, cumulative acks, resend, early-packet buffer); protocol v2
- [x] Any datagram from a peer counts as a heartbeat

### Known pre-existing working-tree change

---

## Principles

1. **Contract vs mechanism.** XNA names, signatures, enum values and *observable behaviour* are the contract.
   Transport, threading, serialisation internals and DI are mechanism.
2. **Pre-release: no backward-compat burden.** Nothing is published to NuGet and no shipped game uses the library, so wire-format and non-XNA API changes need no migration shims, obsolete wrappers or version negotiation. Keep `JoinRequestMessage.CURRENT_PROTOCOL_VERSION` bumped when the format changes, only so stale dev builds fail loudly. Compat concerns (protocol stability, API deprecation policy, semver) start at the first NuGet release (Phase 9).
3. **Every phase ends green:** build + tests on Core, Steam, EOS; Android/iOS build.
4. **One logical commit group per bullet list under "Commit groups".** Suggested messages included.
5. **Deviations from XNA are documented** in the Decision log at the bottom, with a reason.

---

## Phase 0: Safety net  (DONE 2026-10-28)

**Correction:** the original plan said CI never ran tests. That was wrong (my grep was truncated): `nuget-preview.yml` already has
a `test` job on ubuntu/windows/macos, and every publish job depends on it. Task 0.2 was therefore unnecessary.

Tasks
- [x] 0.1 Baseline recorded (below)
- [x] 0.2 ~~Add `dotnet test` to CI~~ not needed, CI already ran Core, Steam and EOS tests
- [x] 0.3 `Tests/Compat/` project: public XNA-shaped API only, real UDP loopback, not in Core's `InternalsVisibleTo`
- [x] 0.4 Compat tests: lifecycle with 2 and 4 instances (create, find, join, ready, start/end, leave, host leave, full session, join-in-progress), data path (reliable, unreliable, ordering over 300 packets, `PacketWriter`/`PacketReader` round trip, targeted and multi-recipient sends)
- [x] 0.5 Real-runtime smoke tests already skip unless `MGNET_STEAM_SMOKE` / `MGNET_EOS_SMOKE` is set
- [x] 0.6 `bin/`, `obj/`, `*.lscache` are ignored; none are tracked
- [x] 0.7 Compat project added to the solution and to the CI `build` and `test` jobs
- [x] 0.8 **Bug fixed along the way:** `SystemLinkSessionManager.DiscoverSessionsAsync` started a new `ReceiveAsync` every 100 ms and abandoned the old one when the timeout won. Datagrams went to the orphaned receives and were lost, so discovery found nothing after the first join in a process. Now one receive stays outstanding

Baseline before this work (Release, macOS, net10.0)

| Suite | Passed | Skipped | Failed |
|-------|-------:|--------:|-------:|
| Core | 70 | 0 | 0 |
| Steam | 12 | 0 (1 smoke test is env-gated and reports skipped) | 0 |
| EOS | 5 | 3 | 0 |
| Android / iOS | build only | | |
| **Compat (new)** | 13 | 5 known gaps | 0 |

Known gaps found by the compat suite (each is an `[Ignore("...plan task...")]` test; un-ignore it when the task is done)
- 1.1 / 1.7: game packets starting with a framework message id are swallowed; clients get `GameStarted`/`GameEnded` twice
- 3.13 / 3.15: discovery reports `CurrentGamerCount` as 1; clients don't see each other

Commit groups
- A. `net: fix LAN discovery losing datagrams to orphaned receives` (0.8, `SystemLinkSessionManager.cs` only)
- B. `tests: add XNA compatibility suite` (0.3, 0.4, solution and CI wiring 0.7)
- C. `docs: add implementation plan` (`IMPLEMENTATION_PLAN.md`)

Exit criteria: met. Compat suite passes on current code, with the gaps tracked.

---

## Phase 1: Wire protocol correctness

Problems found by reading `NetworkSession.ProcessPayload` and `ReliableChannel`:
- Framework messages (ids 2–10) and application payloads share one byte space. A game packet starting with a byte in
  2–10 that happens to parse and consume the whole buffer is swallowed as a framework message.
- `SendDataOptions.InOrder` alone (unreliable-but-ordered) is not implemented: non-reliable sends are unsequenced.
- Reliable sequence is `ushort`; wrap-around handling needs proof.
- No fragmentation: a reliable payload over the MTU is dropped or fragmented by IP unpredictably.

Tasks
- [x] 1.1 Failing repro exists: `DataTests.GamePackets_ThatStartWithAFrameworkMessageId_AreStillDelivered` (un-ignored and passing since 1.2). Also seen in the wild: 3 of 300 four-byte ints were lost when their low byte was 2–10
- [x] 1.2 Framework/app envelope: every channel payload starts with a kind byte (`PayloadKind.Application` / `PayloadKind.Message`); game data is never inspected by the registry; unregistered or malformed messages are dropped (no fallback to game data). Protocol version is now 3. Raw (non-channel) datagrams are registered messages only (handshake, leave)
- [x] 1.3 `InOrder` without `Reliable`: new sequenced channel (`0x24`); the receiver drops anything older than what it already delivered, wrap-safe. `Reliable` alone still delivers in order (a stronger guarantee than XNA's)
- [x] 1.4 Sequence wrap: a lockstep 70 000-packet test already existed; added the same across the wrap with 20% loss, duplication and reordering, and a sequenced-channel wrap test
- [x] 1.5 Fragmentation and reassembly on the reliable channel (`0x25`, fragments are consecutive reliable packets, last-fragment flag). Reliable messages up to 1 MiB; unreliable and sequenced are limited to one datagram (1100 bytes, plus the kind byte) and throw `ArgumentOutOfRangeException` beyond that
- [x] 1.7 Clients raised `GameStarted`/`GameEnded` twice (`SessionStateMessage` and the legacy `GameStateChangeMessage` both fire it). Remove the legacy message (pulls 8.5 forward). Done: `GameStateChangeMessage` removed; `AClientReady_IsSeenByTheHost_AndStartGameReachesEveryone` un-ignored and passing
- [x] 1.6 Backpressure: 256-packet send window measured from the oldest unacked packet, bounded queue (8192 packets), resends capped per tick. A peer that overflows the queue, or breaks the fragment protocol, raises `ReliableChannel.Faulted` and the session evicts that gamer. Selective acks (bitmap of what arrived early) added so resends cover only real holes; without them a capped resend collapsed throughput under loss

Commit groups
- A. `tests: reproduce framework/app byte collision` (1.1)
- B. `net: separate framework and application channels (bump protocol version)` (1.2)
- C. `net: implement InOrder delivery` (1.3, 1.4)
- D. `net: fragmentation and bounded send queues` (1.5, 1.6)

Exit criteria: met. Collision, in-order, wrap and large-payload tests pass at channel level (Tests/Core) and over real UDP (Tests/Compat). Protocol version is 3.

---

## Phase 2: Threading and event model

Problem: `GamerJoined`, `GamerLeft`, `SessionEnded`, `MessageReceived` fire on the receive-loop or timer threads.
XNA raised them from `NetworkSession.Update()` on the game thread, and games (including the NetworkStateManagement
`NetworkSessionComponent`, which calls `Update()` every frame) rely on it. `Update()` is currently a no-op for UDP sessions.

Tasks
- [x] 2.1 (ShipGame already queues events itself in `NetworkManager` and re-raises them from its own update; that code keeps working, and becomes redundant) Internal thread-safe event queue (`System.Threading.Channels`); network threads enqueue, `Update()` drains and raises. Implemented with a `ConcurrentQueue<Action>` (no need for Channels); handler exceptions are logged and do not stop the queue; `SessionEnded` is raised at most once, and directly (not queued) when the caller disposes
- [x] 2.2 Keep `NetworkGamer.IsDataAvailable`/`ReceiveData` consistent with the same drain point
- [x] 2.3 Replace static `NetworkGamer.LocalGamer` with per-session state (remove the static outright; no shim needed pre-release)
- [x] 2.4 Make `NetworkMessageRegistry` thread-safe and reject framework-id collisions (the same type may re-register; a different type on a taken id throws)
- [x] 2.6 Graceful host `Leave()`: clients remove the host gamer (`RemoveGamer`) but never raise `SessionEnded(HostEndedSession)` (only the `EvictGamer` path does). Un-ignore `TheHostLeaving_EndsTheSessionOnClients`
- [x] 2.5 Option `NetworkSession.EventDispatchMode` ({`OnUpdate` default, `Immediate`}) for headless servers/tools that never call `Update()`

Commit groups
- A. `net: raise session events from Update()` (2.1, 2.2, 2.5)
- B. `net: per-session local gamer, thread-safe registry` (2.3, 2.4)

Exit criteria: compat test asserts events fire only inside `Update()` and on the caller's thread.

---

## Phase 3: XNA API parity

Fill every mock/stub so ported code behaves identically.

Gamers
- [x] 3.1 `NetworkGamer.Id` is an XNA `byte`, allocated lazily by the session and unique among the gamers it sees; the string id is `GamerId` (and `INetworkGamer.Id`). `FindGamerById` takes a byte. Not yet identical across machines: the host should assign ids and send them in `JoinAccepted` (do with 3.15/3.16) (XNA-visible, so keep XNA's shape; no legacy string-id shim needed)
- [x] 3.2 `NetworkGamer.Machine` real: one machine for all local gamers, one per remote gamer, `Gamers` lists the machine's live gamers. `RemoveFromSession()` follows with kick in 3.12
- [x] 3.3 `NetworkGamer.SignedInGamer` is null for remote gamers (was `SignedInGamer.Current` for everyone). Multiple local gamers sharing one `Current` waits for per-gamer sign-in (Phase 5/identity)
- [x] 3.4 `LocalNetworkGamer.EnableSendData/IsSendingData` removed: stubs that are not XNA members (XNA has `EnableSendVoice`), unused by ShipGame, and `EnableSendData(SendDataOptions)` has no meaningful semantics. See decision log
- [x] 3.5 Incoming queue bounded at 8192 packets, oldest dropped, counted in `Diagnostics.PacketsDropped`. DEFERRED: per-local-gamer addressing, since the wire carries no recipient and only one local gamer per session exists in practice; revisit with multiple local gamers over the network
- [x] 3.6 `RoundtripTime` measured by the reliable channel from acknowledgements of packets that were never resent (smoothed), so it works for any transport that uses the channel; the UDP heartbeat still feeds it too
- [ ] 3.6b Keep `NetworkGamer.UnacknowledgedAge` (non-XNA, used by ShipGame's stall detection) as a supported extension, available on every back-end. Kept on `NetworkGamer` for the UDP path; "every back-end" comes with Phase 4 (the channel moves to Core for all transports)
- [x] 3.6c `ReceiveData` with a buffer smaller than the packet now throws `ArgumentException` naming the size needed, and leaves the packet queued (it used to truncate silently)

Packets
- [x] 3.7 `PacketWriter.Write(byte[])` length-prefixes; XNA writes raw bytes. Fixed to XNA behaviour + explicit `WriteLengthPrefixed`/`ReadLengthPrefixedBytes` (the parameterless `ReadBytes()` is gone; `ReadBytes(int)` stays). Internal users moved to the prefixed pair, so their wire formats are unchanged. ShipGame checked: it never uses `Write(byte[])`/`ReadBytes`, so it is safe for ShipGame
- [x] 3.8 Added the `BinaryWriter`/`BinaryReader` members XNA's packets inherit that were missing: `char`, `char[]`, `decimal`, `Seek`, `ReadChar/ReadChars/ReadDecimal`. NOT added: `Point`/`Rectangle`/`BoundingBox`/`BoundingSphere`/`Plane`/`Ray`, which I do not believe XNA's `PacketWriter` has (it has `Vector2/3/4`, `Quaternion`, `Matrix`, `Color`, already present). Add them if ShipGame or another consumer needs them
- [ ] 3.9 Allocation reduction: pooled buffers behind `PacketWriter`/`PacketReader` (API unchanged). DEFERRED to Phase 8: no measured problem yet; `RentData` already pools the send path

Sessions
- [-] 3.10 DROPPED 2026-10-28: `Begin*/End*` (APM) removed, at the owner's request. `IAsyncResult` is not a modern pattern, and porting XNA code that calls them to `await` is a mechanical rewrite. The blocking `Create`/`Find`/`Join`/`JoinInvited` stay (they wrap the Task APIs); `Create`/`Find` take `NetworkSessionProperties`, so a literal `null` works
- [x] 3.11 `NetworkSessionProperties` is XNA's 8 nullable-int slots (fixed size, `IList<int?>`). Slots travel as dictionary entries keyed "0"-"7". Found while doing it: `CreateAsync` ignored the properties it was given and `FindAsync` never filtered; both now work (a search matches sessions that have every property it sets, equal). Not done: `AvailableNetworkSession.SessionProperties` is still the dictionary, so an XNA game reading `available.SessionProperties[0]` needs the key "0"
- [x] 3.13 (advertisement now carries the real gamer count; open public slots = max - private - count) Discovery hard-codes `AvailableNetworkSession.CurrentGamerCount = 1` and derives open slots from max; the advertisement must carry the real count. Un-ignore `FindingASession_ReportsTheCurrentGamerCount`
- [x] 3.14 (!) `GamerJoined` `+=` in the Lobby state re-raises the event for every gamer already in the session (including the local one), and invokes the whole delegate, so earlier subscribers get duplicates too. ShipGame does not rely on it (`LanLobby` adds existing gamers itself and de-duplicates by id). DECIDED 2026-10-28: no replay; only gamers joining after subscription. Done: `GamerJoined` is a plain event; gamers who joined before the first `Update()` (local gamers included) are raised by it, which I believe matches XNA's first-`Update()` behaviour for local gamers (unverified)
- [x] 3.15 Done: the host sends the roster in `JoinAcceptedMessage` and a `GamerJoinedMessage` (id 11) to existing gamers, and tells clients when a gamer leaves. Game data between two clients goes through the host (`Relay`/`Forwarded` payload kinds), so seeing a gamer means you can send to it. Protocol is now v4. A client's `AllGamers` holds only itself and the host, not the other clients. Un-ignore `EveryGamer_CanSeeEveryOtherGamer`
- [x] 3.16 Done: no synthetic host; the client adds the host gamer, with its real id, when the host accepts. Gamer byte ids are assigned by the host and sent in the roster, so they are the same on every machine (completes 3.1). The host gamer on a client is a synthetic object with a random `Id`, not the host's real id, so the same gamer has different ids on different machines
- [x] 3.17 Remove the sender self-loopback in `NetworkGamer.SendDataInternal` (SystemLink case) and make the Local case consistent; add a compat test that a sender never receives its own packet
- [x] 3.12 Host migration NOT implemented: `AllowHostMigration` defaults to false and is documented as having no effect. `NetworkMachine.RemoveFromSession()` (host only) ends the removed machine's session with `RemovedByHost`. Timeouts and a channel that gives up now end a client's session with `Disconnected` instead of `HostEndedSession`. Already fine: `Joining` state, full sessions refused and not advertised. Not done: `LocalGamers` cap, because a session only ever has one local gamer (see 3.5)

Commit groups
- A. `gamers: real Machine, SignedInGamer, EnableSendData` (3.2–3.4)
- B. `gamers: bounded queues, RTT everywhere, byte Id` (3.1, 3.5, 3.6)
- C. `packets: XNA parity for byte[] and missing types` (3.7, 3.8)
- D. `packets: pooled buffers` (3.9)
- E. `sessions: integer properties, filtering` (3.11; 3.10 dropped)
- F. `sessions: host migration / kick / join-state` (3.12)

Exit criteria: no `// Mock implementation` left in `Core/Net`; compat suite covers each member. Met except `HasVoice`/`IsTalking`/`IsMutedByLocalUser`, which stay stubs until Phase 7 (voice), and the Mock stubs in `Core/GamerServices` (Guide, GamerServices), which are outside Core/Net.

---

## Phase 4: Backend unification

Problem: two parallel session APIs. `NetworkSession` (XNA shape) is what games use; `INetworkSession`/`INetworkMessage`
is a second one. Backends are split: `INetworkSessionProvider` ends up wrapping `NetworkSession` over a transport,
while `Steam/EOS/Android/iOS*NetworkSession : INetworkSession` are in-memory vertical slices that bypass it.
`INetworkTransport` is `IPEndPoint`-based, which Steam/EOS/Game Center peers don't have.

Tasks
- [x] 4.1 DONE 2026-10-29: option 1. `INetworkSession`, `INetworkGamer`, `ILocalNetworkGamer`, `INetworkSessionFactory`, `SessionInfo`, `UdpNetworkSession` and every plugin's `*NetworkSession`/`*NetworkGamer`/`*NetworkSessionFactory` are gone. Event args now carry `NetworkGamer` (XNA's own type)
      (1) remove or internalise `INetworkSession` (recommended); (2) keep it public as a game-facing read-only interface that `NetworkSession` implements, for mocking/DI in game tests; (3) keep it as a full plugin seam, only worthwhile if a back-end must own session semantics itself (service-owned lobby/authority such as Nakama or Photon)
- [x] 4.2 `PeerId` and `IPeerTransport` (`Bind`, `Send(peer, data, length)`, `ReceiveAsync`) replace `INetworkTransport`; `UdpTransport` maps peers to endpoints and takes a preferred port with fallback; `NetworkSession`, `MessageReceivedEventArgs.RemotePeer` and `AvailableNetworkSession.HostPeer` use peers. Peer-id based transport abstraction (`PeerId` instead of `IPEndPoint`); UDP maps endpoint↔PeerId internally. Replace `INetworkTransport` outright (no adapter needed pre-release); all four plugins move in the same phase
- [x] 4.3 Steam ported: `SteamP2PTransport` uses real SteamIDs as peers (the fake IPv6 endpoints are gone) and sends unreliably, since the channel supplies reliability. `SteamBackend` does lobby discovery and the LAN fallback. The Steam in-memory slice (`SteamNetworkSession`, `SteamNetworkGamer`, `SteamNetworkSessionFactory`, `SteamSessionDirectory`) is deleted and its tests replaced by `SteamBackendTests`
- [~] 4.4 Transports written, NOT verified on real runtimes (Android test app: 14/14 pass on an emulator, but that exercises the LAN fallback, not Nearby, since no Activity is initialised; iOS test app runs in the simulator but NUnitLite finds 0 tests there, cause unknown): `EOSP2PTransport` + `EOSBackend` (EOS lobbies and P2P; accepts P2P connections only from members of a lobby it is in), `AndroidNearbyTransport` + `AndroidBackend` (Nearby Connections, connects on the first datagram), `GameKitTransport` + `IOSBackend` (GKMatch; no session browsing, so a joiner needs a session id and uses `GameKitTransport.HostPeer`). They build and their LAN fallback is covered by the conformance suite; the platform paths need an Epic sandbox with two accounts, two Android devices or emulators with Google Play services, and a Game Center sandbox account respectively
- [x] 4.5 Public seam: `INetworkBackend` (transport factory, advertise, find, prepare-join) + `ISessionAdvertiser`/`SessionAdvertisement`, selected with `NetworkServiceProvider.SetBackend`. `UdpBackend` is the default. `NetworkSession.CreateAsync/FindAsync/JoinAsync` compose it, and no back-end needs `internal` access. `INetworkSessionProvider` and the public `CreateSystemLinkSessionAsync`/`JoinSystemLinkSessionAsync` are gone
- [x] 4.6 `Tests/Conformance/BackendConformanceTests.cs`: one abstract NUnit fixture (find, join, both-way reliable data, 60 KB fragmented message, client leave, host leave, full session not advertised, search properties), linked into the compat, Steam, EOS, Android and iOS test projects, each deriving a fixture. Back-ends that need two accounts skip the two-session scenarios when their real runtime is up

Commit groups
- A. `net: peer-id transport abstraction` (4.1, 4.2, 4.5)
- B. `steam: move to peer-id transport` (4.3)
- C. `eos/android/ios: move to peer-id transport` (4.4)
- D. `tests: backend conformance suite` (4.6)

Exit criteria: all back-ends pass the same conformance suite; no back-end needs `internal` access.

---

## Phase 5: Online session types, matchmaking, invites

- [x] 5.1 `PlayerMatch` and `Ranked` run through the same back-end path as SystemLink: `INetworkBackend.SupportsSessionType` says what a back-end carries and `NetworkSession` refuses the rest with a `NotSupportedException` naming the back-end. The UDP/Android back-ends carry SystemLink only; Steam, EOS and iOS (with their runtime up) carry all three, labelling lobbies with the type and filtering searches by it. Found on the way: the receive loop, `SendData` and the join path were all hard-wired to SystemLink, so a PlayerMatch session could not have exchanged a byte
- [x] 5.2 Session properties are carried by the online back-ends as lobby data (Steam) and lobby attributes (EOS), encoded with the public `SessionPropertyEncoding` so types survive, kept up to date while the session lives, and read back into `AvailableNetworkSession.SessionProperties`, where Core's search filter (3.11) narrows the results. Skill and mode matching are just properties. VERIFIED against the live Steam client (2026-10-29, one account, app 480): a hosted session becomes a lobby carrying its type and properties and follows later property changes (`SteamLobbySmokeTests`, `MGNET_STEAM_SMOKE=1`). Not verified: EOS, and finding/joining a lobby (needs a second Steam account)
- [x] 5.3 `JoinInvitedAsync` joins the invitation the user last accepted in the platform UI (it used to join a made-up session). Back-ends raise `INetworkBackend.InviteReceived`: Steam (`GameLobbyJoinRequested_t` and `+connect_lobby`) and EOS (`AddNotifyLobbyInviteAccepted`; lobbies now allow invites) are written. Steam: the step after Steam names a lobby (describing it as a joinable session) is verified live; the callback that Steam fires on a real invite is not (needs a second account). EOS: unverified; Android has no invitations; iOS is NOT wired (needs `GKLocalPlayer`'s listener and a device)
- [x] 5.4 `NetworkSession.InviteAccepted` is raised on the game's thread from `NetworkSession.Update()` and `GamerServicesComponent.Update()`; its args are XNA's (`Gamer`, `IsCurrentSession`, which is true when the invite is to a session this machine is already in) instead of the old non-XNA `IsSignedInGamer`
- [x] 5.5 End reasons audited: dispose = `ClientSignedOut` (once); host leaves = `HostEndedSession`; host removes you = `RemovedByHost`; heartbeat timeout or a channel that gives up = `Disconnected`; a transport that fails 10 receives in a row = `NetworkError` (new: the loop used to log and spin forever). Back-ends add no reasons of their own now that the sessions are all `NetworkSession`

Exit criteria: `NetworkStateManagement` create/find/join/invite flows work on Steam.

---

## Phase 6: Security and robustness

No auth exists in `Core/Net`: `SessionInfo.IsPasswordProtected` is never enforced; senders are trusted by endpoint.

- [x] 6.1 Optional session password: the host challenges, the joiner proves it knows it; the password never crosses the network. `IsPasswordProtected` is enforced and advertised (LAN, Steam and EOS lobbies carry the flag)
- [x] 6.2 Per-link key issued by the host at join, so a datagram that is not authenticated for its link is dropped (spoofed `GamerLeaving`/`SessionState` included)
- [x] 6.3 Parser hardening: size limits and exception safety on all message `Deserialize` paths; fuzz tests
- [x] 6.4 Rate-limit join requests: a burst and a trickle per address, and an overall ceiling
- [x] 6.5 Every datagram on a link is AES-GCM protected (authenticated always; unreadable to an eavesdropper when there is a password)

---

## Phase 7: Voice (optional, DEFERRED until after Phase 9)

Deferred 2026-10-29 at the owner's call: nothing else depends on it, so Phases 8 and 9 (and the first NuGet publish) come first. The numbering stays so references still work.

XNA's voice API is only status: `NetworkGamer.HasVoice/IsTalking/IsMutedByLocalUser`, `LocalNetworkGamer.IsSendingData`,
and `GamerPrivileges.AllowCommunication`. Capture, encode, transport and playback were the platform's job, not the game's.
That maps well onto plugins: Core owns the XNA-shaped status API, and a voice *back-end* supplies the mechanism.

Design (pluggable, optional)
- Core defines `INetworkVoiceBackend` (start/stop, per-peer mute, `TalkingChanged`, `PeerVoiceAvailable`) and an optional capability interface `INetworkVoiceProvider` that a back-end factory may also implement. No provider means `HasVoice == false` and nothing else changes
- Core raises talking/mute events through the Phase 2 `Update()` queue and wires mute to `GamerPrivileges`
- Three kinds of provider, chosen by capability:
  1. Platform-native: Steam Voice, EOS RTC (Epic runs capture/playback itself in an RTC room tied to the lobby)
  2. Software fallback in Core or a separate package: `Microphone` capture, Opus encode, sent on a dedicated voice channel byte (Phase 1 envelope), decode and play with `DynamicSoundEffectInstance`. Works on LAN/UDP and any transport
  3. Apple/Google: verify what platform voice APIs still exist before committing; the fallback covers them otherwise

Tasks
- [ ] 7.0 Depends on 1.2 (voice channel byte) and 2.1 (events on `Update()`)
- [ ] 7.1 Core: `INetworkVoiceBackend`/`INetworkVoiceProvider`, real `HasVoice/IsTalking/IsMutedByLocalUser/IsSendingData`
- [ ] 7.2 Software fallback provider (Opus + voice channel, jitter buffer, push-to-talk/voice-activity)
- [ ] 7.3 Steam Voice provider
- [ ] 7.4 EOS RTC provider (room lifecycle bound to session lifecycle)
- [ ] 7.5 Android/iOS: research native options, otherwise use 7.2
- [ ] 7.6 Mute/privilege plumbing via `GamerPrivileges`

---

## Phase 8: Modernisation and cleanup

- [x] 8.1 One framework-wide `NetworkLogging.Logger` (default: debugger output); sessions use it unless given their own, and the LAN code and every back-end log through it. 55 `Debug.WriteLine` calls converted, with a severity each. `EOSClient`'s 34 calls converted with the EOS review
- [ ] 8.2 (SKIPPED for now) Instance-based service container behind the static `NetworkServiceProvider`/registries (statics remain as the facade)
- [x] 8.3 Removed the empty `NetworkMessages.cs` and the empty duplicate `MessageReceivedEventArgs.cs`; `Net.GamerCollection` now derives from `GamerCollection<NetworkGamer>` (the XNA type), so `GamerCollection<NetworkGamer> all = session.AllGamers` compiles
- [x] 8.4 `PlayerMoveMessage` deleted (nothing used it; wire id 1 is now unassigned)
- [x] 8.5 "Phase N" comments removed; the legacy `GameStateChangeMessage` broadcast was already gone
- [x] 8.6 Every await in Core and the back-ends is `ConfigureAwait(false)`; all 14 blocking wrappers say they block and why they cannot deadlock. `Guide.ShowMessageBoxAsync` used `Task.Yield()`, which resumes on the caller's context and would have deadlocked `ShowMessageBox`; it now hops to the thread pool
- [x] 8.7 Nullable reference types and `latest-recommended` analyzers on Core: 90 nullable warnings and 73 analyzer warnings fixed, 0 left. Downgraded to none in `Core/.editorconfig`: CA1707/CA1708 (public names follow the protocol and XNA), CA1822 (XNA-shaped instance members), CA1805 (explicit defaults document intent). Public changes a nullable-aware caller sees: `?` on `FindGamerById`, `Host`, `Tag`, `Sender`, `Password`, `SignedInGamer.Privileges` (never assigned: always null), the achievement icon providers, `PrepareJoinAsync`; the optional string and exception arguments
- [x] 8.8 EOS binding stays vendored: Epic ships its C# SDK as source to copy in and publishes no NuGet package of its own (only an unofficial one). Before the first publish (9.5), check that the Epic Online Services Developer Agreement allows redistributing the SDK source and native libraries in our package. EOS code reviewed 2026-10-29 (see the decisions log); still NOT run against a real Epic account

---

## Phase 9: Samples, docs, release

- [ ] 9.1 `NetworkStateManagement` (`/Users/dominique/Development/CartBlanche/MonoGame-Samples/NetworkStateManagement`) runs unmodified-as-possible on UDP, Steam, then EOS/Android/iOS. Note: it exercises the session lifecycle (create/find/join/invite/lobby/end reasons) but **not** `SendData`/`ReceiveData`
- [x] 9.2 `Samples/DataPath` (console app, public API only): every player sends every other positions (unreliable), an in-order counter, numbered reliable events and a 60 KB message that gets split, then says what it sent; each machine checks what arrived (exact values, no reordering of the in-order stream, reliable events once and in order with none missing, the large message intact, enough positions through, the simulated latency showing on the other side). Runs as `selftest` (a host and clients in one process over real UDP, joined directly by address, with the session password on so the data crosses the protected links), or as `host` and `join` on real machines, with `--loss` and `--latency` injected per machine. Exit code 0 PASS, 1 FAIL. `Tests/Compat/DataPathAcceptanceTests.cs` runs the self-test (2 clients, 10% loss, 40 ms). It found a bug in my `SimulatedLatency` (separate timers let back-to-back datagrams overtake each other, so the in-order stream dropped most of what a relay sent; fixed with one ordered queue, test `SimulatedLatency_KeepsDatagramsInTheOrderTheyWereSent`). Not done: a graphical version (the console app is the acceptance test), and no other back-end has been run through it yet (set one with `NetworkServiceProvider.SetBackend` first)
- [x] 9.3 `README.md` has an accurate per-back-end status table (what was run against the real platform and what was not), and short Security and Logging sections. `COMPATIBILITY.md` is the XNA compatibility matrix, checked against the archived XNA 4.0 member lists on Microsoft Learn (NetworkSession, NetworkGamer, LocalNetworkGamer, AvailableNetworkSession, QualityOfService, NetworkMachine, NetworkSessionProperties, PacketReader/Writer, Gamer, SignedInGamer, Guide, five enums, and both namespaces' type lists), with the behaviour that differs and a migration list. Achievements, leaderboards, presence, `GamerCollection<T>`, `GamerPrivileges` and `GamerServicesComponent` were not compared member by member
- [~] 9.3b Close the gaps the matrix found that are worth closing before the API freeze (9.5). DONE 2026-10-29, with tests: `QualityOfService` names, `SendDataOptions.Chat` (and `[Flags]`), `NetworkSession.ResetReady` (host only), `HostChanged` and `HostChangedEventArgs` (never raised: no host migration), `NetworkException`/`GamerPrivilegeException`/`InviteAcceptedEventArgs` moved to `GamerServices`, `NetworkGamer : Gamer` (`Gamertag` read-only), `SimulatedLatency`/`SimulatedPacketLoss` now act on every datagram a machine sends (the goodbye on dispose is exempt), `NetworkSession.FindGamerById`, `NetworkSession.IsDisposed` and `Gamer.IsDisposed`, `GamerServicesDispatcher` (and `GamerServicesComponent` now uses it). DECIDED to leave as they are: `SessionProperties` stays an `IDictionary<string, object>` (NetworkPrediction uses string keys). DEFERRED until a game needs them: `NetworkSessionType.LocalWithLeaderboards`, `AddLocalGamer`, `PreviousGamers`/`MaxPreviousGamers`/`MaxSupportedGamers`, `SignedInGamer.SignedIn/SignedOut`, `Guide.BeginShowKeyboardInput/EndShowKeyboardInput`, `PacketReader/Writer` as `BinaryReader/Writer`, `NetworkGamer.HasLeftSession/IsGuest/IsPrivateSlot/Session`. Skip: Xbox LIVE UI, avatars, friends, party, TrueSkill and arbitrated leaderboards
- [x] 9.4 ShipGame (`~/Projects/MonoGame/MonoGame.Samples/ShipGame`, referencing this repo's Core by project reference) updated for the intentional API changes and builds with 0 errors (Core and DesktopGL), its 44 tests pass, as do this repo's 53 compat tests. It needed two changes, neither `Write(byte[])`: `NetworkGamer.Id` is now XNA's `byte`, so its string identity use moved to `GamerId` (12 sites in 5 files), and `AvailableNetworkSession` takes a `PeerId` for the host (`UdpTransport.ToPeer(endpoint)`). The edits are NOT committed in ShipGame (its tree has the owner's other uncommitted work). A two-machine play-through has not been done
- [x] 9.4c ShipGame had one silent break from `Id` becoming a `byte`: `CombatSync.SlotOf` did `Array.IndexOf(slotIds, gamer.Id)`, which compiles against any object but never matches a string, so the host ignored every leave and stall (no "X left the game", no grey-out, no "No answer from"). Found from the owner's two-machine test (the library's log showed the timeout and the removal, ShipGame's own `ShipGame-net.log` showed `Gamer left`, only the on-screen notice was missing). Fixed to `GamerId`, plus a consistency change in `NetworkManager`; every other `.Id` in ShipGame checked. For 9.4b, search each game for `.Id` uses that still compile
- [ ] 9.4b Other games that use this library (the owner will give the folders once ShipGame is confirmed working on two machines): build each against the current library and make the same kind of tweaks (`Id` to `GamerId`, `PeerId`, nullable annotations, anything else the compiler or a play-through shows). FOUND 2026-10-29, build-only, nothing edited: six projects in `~/Development/CartBlanche/MonoGame-Samples` already reference this library. NetworkPrediction, NetworkStateManagement, CatapultWars and NetRumble (Core projects) build with 0 errors. CardsStarterKit/Blackjack fails on `INetworkSessionFactory` (`Networking/MultiplayerSessionService.cs:69`) and Peer2Peer on `INetworkGamer` (`PeerToPeerGame.cs:375`): both are interfaces removed in Phase 4, so those two need porting to `NetworkSession`/`NetworkGamer`. NetworkPrediction depends on `SessionProperties["0"]` (string keys, so keep `SessionProperties` an `IDictionary<string, object>`) and on `SimulatedLatency`/`SimulatedPacketLoss`, which currently do nothing
- [~] 9.5 Before the first NuGet publish. DONE 2026-10-29: (a) API freeze mechanism: a public-API listing per package kept in `Tests/*/PublicApiBaselines/` and a test that fails on any difference (Core 685 lines, Steam, EOS; Android and iOS are not covered, their tests are apps); (b) the API reviewed for the freeze: `SteamBackend` and `SteamP2PTransport` moved into `Net.Steam` like every other back-end type; (c) `VERSIONING.md` (lock-step versions, SemVer, `0.x` may break on a minor, what counts as the API, the wire protocol rules: bump on any wire change, exact match to join, no negotiation for 1.0) and `CHANGELOG.md` (everything since the development builds); the wire protocol is reset to 1 for the first public release, since nothing was ever published (development used 1 to 5); (d) all five packages pack cleanly in Release (MIT, readme, repository, symbols; EOS is 75 MB with its native libraries, under NuGet's 250 MB limit); (e) the publish workflow pushes to GitHub Packages and a plain `v*` tag publishes all five: the EOS job no longer runs on `v*` tags (only `eos-v*` or a manual run); (f) lower platform versions for the mobile packages are not possible (the Android and iOS workloads accept only API 36 and iOS 26). STILL OPEN, needs the owner: (1) Epic: the EOS package would declare MIT while containing Epic's SDK source and native libraries, whose files say 'Copyright Epic Games, Inc. All Rights Reserved' and which ship with no license text here; the agreement pages are behind an Epic login so it could not be read; 1,080 SDK files and 6 native libraries are already tracked in this repository; decide whether to publish EOS at all, with what license metadata, or to ask Epic. (2) Decide the open policy points in `VERSIONING.md`: first version number (`0.4.0-preview.1`?), whether to add `net8.0`, protocol negotiation (proposed: none). (3) nuget.org or GitHub Packages (CI only pushes to GitHub Packages today). (4) Package polish: an icon, XML documentation files in the packages (only Android ships one), a README per package; `licenseUrl` is deprecated metadata. (5) Publishing the previews themselves (a tag, which also needs the secrets on the repository)

---

## Phase 10: Dedicated / headless server (optional)

A headless server is a service with no window, game loop or local player, hosted in the cloud (for example on Azure; use a
VM, container or Kubernetes service for UDP, or PlayFab Multiplayer Servers, and verify Azure's current UDP support first)
so clients connect over the internet instead of to a player's machine on a LAN. Not part of the XNA contract and not needed
by ShipGame. Steam/EOS relay P2P already cover internet play without self-hosting.

Cheap choices to make earlier, so this stays possible: do not bake "host == local player" into new code (Phases 2–4), and
support join-by-address (5.x).

Needs Phase 6 (security) first, since a public UDP endpoint must not trust senders by IP.

- [ ] 10.1 Session whose host is not a gamer: no `LocalNetworkGamer`/`SignedInGamer` on the host; audit `IsHost`, `Host`, `IsEveryoneReady`, host-leave handling
- [ ] 10.2 Join by address/hostname over the internet (not only LAN broadcast)
- [ ] 10.3 Directory/lobby service: servers register, clients list; or use a platform lobby
- [ ] 10.4 Headless run helper: `Update()` pump on a timer, and/or `Immediate` event dispatch (see 2.5), graceful shutdown on SIGTERM
- [ ] 10.5 Container sample (Dockerfile, health check, structured logging via 8.1)
- [ ] 10.6 Load/soak tests: many clients, packet loss and reorder injection, flood and spoof resistance
- [ ] 10.7 (Optional, only if non-MonoGame use matters) A MonoGame-free networking core. Today the library compiles against `MonoGame.Framework` and cannot be used without that assembly: `PacketReader`/`PacketWriter` use `Vector2/3/4`, `Quaternion`, `Matrix` and `Color`; `SignedInGamer` and `Guide` use `PlayerIndex`; `GamerServicesComponent` uses `IGameComponent`, `GameTime` and `Game` (11 files in Core). The headless Core tests show a MonoGame-free *runtime* is fine (no window or GL; they reference only `MonoGame.Framework.DesktopGL`), so a server can use the library as it is by referencing that package. Making it independent would mean splitting a package with no XNA types (sessions, reliable channel, security, back-ends, `PeerId`, logging) from an XNA-shaped layer on top (gamers, packets with math types, `GamerServices`), keeping the public XNA shape unchanged. Not before the first publish. Untested: FNA/KNI use (a different assembly name, so it needs a rebuild against them)
- [ ] 10.8 (Post-1.x back-end, optional) A web back-end, so browser games (for example KNI's Blazor/WebGL platform) can play. Checked 2026-10-29: the Core source compiles unchanged against the KNI NuGet packages (`nkast.Xna.Framework` and `.Game` 4.2.9001) and all 160 Core tests pass against that build, but the compiled library is not binary-compatible (KNI's assemblies have different names), so KNI needs its own build from the same source (a build property choosing the packages, a second package, both in CI; the platform back-ends are untested against KNI). What stops a browser today: (1) no raw UDP sockets or broadcast, which the LAN back-end uses; (2) `AesGcm` is documented as unsupported on the browser, with conflicting reports for newer .NET, so the link protection may need a SubtleCrypto interop path (verify first); (3) the blocking wrappers and `Dispose`'s wait (`receiveTask.Wait`) would stall a single-threaded browser runtime, so a browser must use the `*Async` paths (my reading, untested). What it needs: a new `INetworkBackend` with a transport (WebSocket through a relay server is the simplest, WebRTC data channels the better fit because they carry unreliable datagrams) and discovery through a lobby or signaling server, since nothing can broadcast. Browser and native players can only meet if they share a transport, so cross-play needs a relay/headless server that desktop and phone clients reach over UDP and browsers over WebSocket or WebRTC; that is the Phase 10 server (10.1-10.5), so do those first. Before building it, check in a browser: AES-GCM, `Task.Run` and delay behaviour, and the blocking paths

Exit criteria: a containerised server accepts clients from another network and survives the 10.6 tests.

---

## Decision log

| Date | Decision | Reason |
|------|----------|--------|
| 2026-10-28 | Public XNA-shaped API is frozen; internals free to change | Compat goal and modern internals are not mutually exclusive |
| 2026-10-28 | Plugin seam: option 1. Plugins supply transport, discovery and optional capabilities; `INetworkSession` removed/internalised in Phase 4 | Plugins should not re-implement lobby/ready/reliability |
| 2026-10-28 | Phase 0 done. Original claim "CI never runs tests" retracted | CI had a test job; my grep was truncated |
| 2026-10-28 | Fixed the discovery orphan-receive bug immediately rather than only logging it | The 4-instance compat test cannot pass without it |
| 2026-10-28 | No `GamerJoined` replay on subscribe; no self-loopback in `SendData` | ShipGame relies on neither; both are inconsistent today |
| 2026-10-28 | Unreliable and sequenced data limited to one datagram (1100 bytes); larger data must be sent reliably | IP fragmentation drops the whole datagram if any piece is lost; XNA also limited unreliable packet size. Revisit if ShipGame or a backend needs more |
| 2026-10-28 | Selective acks added to the channel | Cumulative-only acks plus capped resends stalled under 20% loss; found by the lossy wrap test |
| 2026-10-28 | Removed `LocalNetworkGamer.EnableSendData`/`IsSendingData` instead of implementing them | Not XNA members, nothing uses them, and the signature has no sensible meaning; reinstate only if a real need appears |
| 2026-10-28 | Client-to-client game data is relayed by the host; the topology stays a star | XNA let any gamer send to any gamer; a mesh needs NAT traversal that LAN/Steam/EOS do not all give us. The cost is one extra hop and ~2 bytes of header |
| 2026-10-28 | A leave notice is only honoured from the gamer it names (on the host) or from the host (on clients) | Otherwise any peer could remove any other; cheap to close now, the rest of the trust model is Phase 6 |
| 2026-10-28 | `AllowHostMigration` defaults to false; migration is not built | Honest default; a real migration needs the host's state (roster, ids, properties) replicated to a successor, which is its own project |
| 2026-10-28 | Sync `Create`/`Find` take `NetworkSessionProperties`; Task APIs keep `IDictionary<string, object>` | XNA ports pass `null` or a properties object to the sync/Begin methods; two overloads of one method would make a literal `null` ambiguous and break callers such as ShipGame's `FindAsync(..., null)` |
| 2026-10-28 | Dropped `Begin*/End*` (task 3.10), added then removed | Owner's call: `IAsyncResult` is not modern and the port to `await` is trivial; the blocking calls remain |
| 2026-10-29 | `AdvertiseAsync` and `PrepareJoinAsync` receive the session's transport; `PrepareJoinAsync` returns a handle disposed when the session ends | Nearby and GameKit must tell their own transport to advertise or match; Steam and EOS lobbies must be left again when the session ends |
| 2026-10-29 | Test apps for Android/iOS must not mark the MonoGame.Framework package `PrivateAssets="all"` | The SDK treats that as "do not publish" and leaves it out of the app, so every test failed with "Could not find MonoGame.Framework" |
| 2026-10-29 | Android/iOS/EOS slices deleted rather than kept until verified | The owner chose to remove `INetworkSession` after 4.4; their lobby/Nearby/GameKit code was carried into the backends and transports |
| 2026-10-28 | (superseded 2026-10-29) EOS, Android and iOS get `*Backend` classes that run over the LAN (UDP) for now, with strict-mode checks | That is all their old provider path did: their real behaviour (Nearby, EOS sessions, Game Center) only existed in the `INetworkSession` slices. A genuine transport per platform is task 4.4 and cannot be verified without devices/credentials |
| 2026-10-28 | `INetworkBackend.AdvertiseAsync` completes once advertising has started and then runs until the session's token is cancelled | Lets a strict back-end (Steam) fail session creation by throwing, while LAN advertising keeps its endless loop |
| 2026-10-29 | Without a password the link key travels in the clear: forgery from off the path is stopped, a reader of the LAN is not | A key exchange with no shared secret cannot stop a man in the middle; the password is the opt-in for that. Replays are left to the channel, which discards duplicates |
| 2026-10-29 | EOS republishes the password flag only when properties change; Steam keeps it in step every second | A password set after creation is rare; the EOS loop only updates the lobby on a property change |
| 2026-10-29 | Voice (Phase 7) deferred until after Phase 9; work continues with Phase 8 | Optional, and nothing in Phases 8 to 10 depends on it |
| 2026-10-29 | A session's default logger is now the framework logger (debugger output), no longer the console | Console output is noise in a shipped game; `NetworkLogging.Logger` is the one place a game redirects it |
| 2026-10-29 | 8.2 (instance-based service container) skipped for now | The statics are the XNA-shaped facade and nothing needs several contexts in one process yet; revisit with Phase 10 |
| 2026-10-29 | EOS transport ticks the SDK from its receive loop, and holds a P2P connection request from a stranger for up to 10 s before refusing | Connection requests and lobby member changes are only delivered on a tick, and a joiner's request can beat the host's lobby member list; both would otherwise make a join fail |
| 2026-10-29 | EOS republishes the password flag with the properties; the owner's native-library probing in `EOSClient` is kept, its alias copy dropped, and the build copies the machine's own EOS library to the output | The committed resolver could not find the library when run from source (a test proves it); the alias was not needed (a test shows the resolver alone loads it) and wrote files into the app folder. The platform flags default stays `DisableOverlay` |
| 2026-10-29 | `Dispose()`/`DisposeAsync()` send the goodbye that only `Leave()` sent; a crash still says nothing and is found by the 9 s heartbeat timeout | Found while asking why ShipGame's host behaved differently when Android crashed: a game that exits with `session.Dispose()` (as ShipGame does) made the others wait out the timeout. XNA's `Dispose` ends the session for everybody. Tests: a silent client is dropped in about 12 s, a goodbye arrives at once, a host's disposal ends the clients' sessions at once |
| 2026-10-29 | The library keeps its compile-time dependency on `MonoGame.Framework` (private assets, no NuGet dependency declared); a MonoGame-free core is optional task 10.7 | The XNA-shaped public types (`Vector3`, `PlayerIndex`, `Game`) must be the real XNA types, and the headless tests show no window or GL is needed at run time. For 9.5: document the requirement and a minimum MonoGame 3.8.x (older versions untested) |
| 2026-10-29 | A web (browser) back-end is a post-1.x option (10.8), after the headless server it would relay through | Browsers have no UDP or broadcast and may lack AES-GCM; the back-end seam fits a WebSocket/WebRTC transport, but it needs a relay and lobby server first, and Core also compiles unchanged against KNI (binary-incompatible, so a second build) |
| 2026-10-29 | The EOS publish job no longer runs on a plain `v*` tag | The package carries Epic's SDK source and native libraries under Epic's terms, which have not been checked for redistribution; a release tag must not publish them by accident |
| 2026-10-29 | The wire protocol number is reset to 1 for the first public release | Nothing was published before, so there are no deployed peers to tell apart; the development numbers (1 to 5) were for formats that never shipped. A development build that also says 1 (the June 0.3.1-era ones) would not be refused, so rebuild every device before mixing |
| 2026-10-28 | Events move to `Update()` | This matches XNA's behaviour, so it is also a compat fix |
| 2026-10-28 | Phase 2 shipped as one commit, not the two planned (A and B) | The event queue and the per-session local gamer change the same lines of `NetworkSession.cs` |
| 2026-10-28 | `SessionEnded` is raised at most once per session | Previously a client whose host left, then disposed in the handler, got it twice (HostEndedSession then ClientSignedOut) |
| 2026-10-28 | Framework state changes stay immediate; only the public events are deferred | Handshake and readiness logic must not wait for the game's `Update()` |

## Open questions
0. ~~Self-loopback~~ DECIDED 2026-10-28: no echo. `SendData` never delivers to the sender; drop the `includeSelf` branch in `NetworkGamer.SendDataInternal` (task 3.17). Verify against XNA docs when implementing
1. ~~Is `Write(byte[])` length-prefix behaviour relied on by ShipGame?~~ No (checked ShipGame source). Still check any other consumers before release (3.7)
2. Plugin seam: remove/internalise `INetworkSession`, keep as game-facing interface, or keep as full plugin seam? (4.1)
3. ~~Headless/dedicated server?~~ Optional, tracked as Phase 10. 2.5 stays low priority until then
4. ~~Voice in scope?~~ Yes. Design in Phase 7
5. Minimum target: any need to support TFMs below `net10.0`?
6. **NEEDS ATTENTION once everything else is done (parked 2026-10-29 at the owner's request):** the iOS test app (`Tests/iOS`) runs in the simulator but NUnitLite finds 0 tests, although its fixtures and `[Test]` methods are present at runtime (the same runner finds all 14 on Android). Not found yet: whether it is the interpreter, the trimmer or NUnitLite's discovery on the iOS runtime. Until fixed, `GameKitTransport`/`IOSBackend` have no on-platform test at all. Needs a Mac with Xcode (simulators for iOS 18.x are installed)
