# Implementation Plan

Goal: **API-compatible with `Microsoft.Xna.Framework.Net` / `GamerServices` on the surface, modern and
pluggable underneath.** The public XNA-shaped API is a frozen contract; everything below it may be
rewritten freely as long as the compat suite (Phase 0) stays green.

This file is the single source of truth for progress. Update it in the same commit as the work it describes.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[?]` needs verification before work starts · `(!)` decision needed

_Last updated: 2026-10-28 (Phases 0, 1 and 2 complete; 2 compat known gaps remain)_

---

## Status at a glance

| Phase | Theme | Status | Tasks done |
|------:|-------|--------|-----------|
| 0 | Safety net: CI runs tests, compat suite scaffold | **Done** | 8/8 |
| 1 | Wire protocol correctness (envelope, InOrder, fragmentation) | **Done** | 7/7 |
| 2 | Threading and event model (`Update()`-drained events) | Done | 6/6 |
| 3 | XNA API parity (gamers, packets, sessions) | Not started | 0/19 |
| 4 | Backend unification (one public API, peer-id transport) | Not started | 0/6 |
| 5 | Online session types, matchmaking, invites | Not started | 0/5 |
| 6 | Security and robustness | Not started | 0/5 |
| 7 | Voice (pluggable) | Not started | 0/7 |
| 8 | Modernisation and cleanup | Not started | 0/8 |
| 9 | Samples, docs, release | Not started | 0/5 |
| 10 | Dedicated/headless server (optional) | Not started | 0/6 |

### Baseline (already done, commit `20aa893` "Lan fixes for more reliable UDP")
- [x] Broadcast on every network adapter (`LanBroadcast`), `SystemLinkSessionManager` fixes
- [x] `AllowJoinInProgress` enforced, join-rejection reported
- [x] Reliable delivery over UDP: per-gamer `ReliableChannel` (seq numbers, cumulative acks, resend, early-packet buffer); protocol v2
- [x] Any datagram from a peer counts as a heartbeat

### Known pre-existing working-tree change
- `EOS/EOSClient.cs` has uncommitted edits unrelated to this plan. Leave them alone; commit separately.

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
- [ ] 3.6b Keep `NetworkGamer.UnacknowledgedAge` (non-XNA, used by ShipGame's stall detection) as a supported extension, available on every back-end
- [ ] 3.6c (!) `ReceiveData(byte[])` silently truncates when the buffer is too small (ShipGame warns about this); decide throw vs truncate

Packets
- [ ] 3.7 `PacketWriter.Write(byte[])` length-prefixes; XNA writes raw bytes. Fix to XNA behaviour + explicit `WriteLengthPrefixed`/`ReadLengthPrefixedBytes`. ShipGame checked: it never uses `Write(byte[])`/`ReadBytes`, so it is safe for ShipGame
- [ ] 3.8 Add missing XNA overloads (`Point`, `Rectangle`, `BoundingBox`/`Sphere`, `Plane`, `Ray`, `Int16`/`Byte` array variants, `Seek`) and verify `PacketReader` symmetry
- [ ] 3.9 Allocation reduction: pooled buffers behind `PacketWriter`/`PacketReader` (API unchanged)

Sessions
- [ ] 3.10 `Begin*/End*` (APM) shims for `Create/Find/Join/JoinInvited` over the Task APIs
- [ ] 3.11 Integer-keyed `NetworkSessionProperties` (XNA: 8 int slots) with filtering in `Find`; keep dictionary overload
- [ ] 3.13 Discovery hard-codes `AvailableNetworkSession.CurrentGamerCount = 1` and derives open slots from max; the advertisement must carry the real count. Un-ignore `FindingASession_ReportsTheCurrentGamerCount`
- [ ] 3.14 (!) `GamerJoined` `+=` in the Lobby state re-raises the event for every gamer already in the session (including the local one), and invokes the whole delegate, so earlier subscribers get duplicates too. ShipGame does not rely on it (`LanLobby` adds existing gamers itself and de-duplicates by id). DECIDED 2026-10-28: no replay; only gamers joining after subscription. Still confirm what XNA does for local gamers on the first `Update()`
- [ ] 3.15 A client's `AllGamers` holds only itself and the host, not the other clients. Un-ignore `EveryGamer_CanSeeEveryOtherGamer`
- [ ] 3.16 The host gamer on a client is a synthetic object with a random `Id`, not the host's real id, so the same gamer has different ids on different machines
- [ ] 3.17 Remove the sender self-loopback in `NetworkGamer.SendDataInternal` (SystemLink case) and make the Local case consistent; add a compat test that a sender never receives its own packet
- [ ] 3.12 Host migration: implement, or set `AllowHostMigration` default to `false` and document. Also `RemoveFromSession`, kick, `SessionState` semantics (`Joining`, locked-when-full), `LocalGamers` cap

Commit groups
- A. `gamers: real Machine, SignedInGamer, EnableSendData` (3.2–3.4)
- B. `gamers: bounded queues, RTT everywhere, byte Id` (3.1, 3.5, 3.6)
- C. `packets: XNA parity for byte[] and missing types` (3.7, 3.8)
- D. `packets: pooled buffers` (3.9)
- E. `sessions: APM shims, integer properties, filtering` (3.10, 3.11)
- F. `sessions: host migration / kick / join-state` (3.12)

Exit criteria: no `// Mock implementation` left in `Core/Net`; compat suite covers each member.

---

## Phase 4: Backend unification

Problem: two parallel session APIs. `NetworkSession` (XNA shape) is what games use; `INetworkSession`/`INetworkMessage`
is a second one. Backends are split: `INetworkSessionProvider` ends up wrapping `NetworkSession` over a transport,
while `Steam/EOS/Android/iOS*NetworkSession : INetworkSession` are in-memory vertical slices that bypass it.
`INetworkTransport` is `IPEndPoint`-based, which Steam/EOS/Game Center peers don't have.

Tasks
- [ ] 4.1 DECIDED 2026-10-28: option 1 (remove or internalise `INetworkSession`). Plugin seam below. Recommendation: plugins supply a transport (`PeerId`-based), discovery/matchmaking and optional capabilities (voice, invites, identity); Core's `NetworkSession` composes them, so plugins never re-implement lobby/ready/reliability. Options:
      (1) remove or internalise `INetworkSession` (recommended); (2) keep it public as a game-facing read-only interface that `NetworkSession` implements, for mocking/DI in game tests; (3) keep it as a full plugin seam, only worthwhile if a back-end must own session semantics itself (service-owned lobby/authority such as Nakama or Photon)
- [ ] 4.2 Peer-id based transport abstraction (`PeerId` instead of `IPEndPoint`); UDP maps endpoint↔PeerId internally. Replace `INetworkTransport` outright (no adapter needed pre-release); all four plugins move in the same phase
- [ ] 4.3 Port Steam transport to the new abstraction (`SteamP2PTransport`); delete the Steam in-memory slice or move it to Tests
- [ ] 4.4 Port EOS (P2P), then Android/iOS (Game Center / Play Games match data)
- [ ] 4.5 Public constructor/factory path so third-party backends can create sessions without `internal` access
- [ ] 4.6 Backend conformance test suite (one set of tests run against every back-end)

Commit groups
- A. `net: peer-id transport abstraction` (4.1, 4.2, 4.5)
- B. `steam: move to peer-id transport` (4.3)
- C. `eos/android/ios: move to peer-id transport` (4.4)
- D. `tests: backend conformance suite` (4.6)

Exit criteria: all back-ends pass the same conformance suite; no back-end needs `internal` access.

---

## Phase 5: Online session types, matchmaking, invites

- [ ] 5.1 `PlayerMatch` and `Ranked` supported by the base `NetworkSession` paths (currently `NotSupported`/`NotImplemented`)
- [ ] 5.2 Property filtering and skill/mode matching in `FindAsync` across back-ends
- [ ] 5.3 `JoinInvited` wired to real invite events (Steam rich presence/lobby invites, EOS custom invites, Game Center); remove the simulated one
- [ ] 5.4 `NetworkSession.InviteAccepted` raised from back-end events and from `Update()`
- [ ] 5.5 Session end reasons audited against XNA's (`ClientSignedOut`, `RemovedByHost`, `Disconnected`, `HostEndedSession`) per back-end

Exit criteria: `NetworkStateManagement` create/find/join/invite flows work on Steam.

---

## Phase 6: Security and robustness

No auth exists in `Core/Net`: `SessionInfo.IsPasswordProtected` is never enforced; senders are trusted by endpoint.

- [ ] 6.1 Join token / optional password in handshake; enforce `IsPasswordProtected`
- [ ] 6.2 Per-session peer secret so spoofed `GamerLeaving`/`SessionState` from a LAN peer are rejected
- [ ] 6.3 Parser hardening: size limits and exception safety on all message `Deserialize` paths; fuzz tests
- [ ] 6.4 Rate-limit join requests and handshake replies
- [ ] 6.5 (Optional) payload encryption/MAC for LAN and relay transports

---

## Phase 7: Voice (in scope)

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

- [ ] 8.1 Replace `Debug.WriteLine` with injectable `ILogger`/`INetworkLogger` adapter
- [ ] 8.2 Instance-based service container behind the static `NetworkServiceProvider`/registries (statics remain as the facade)
- [ ] 8.3 Remove empty `Core/Net/NetworkMessages.cs`; consolidate duplicate `GamerCollection` and `MessageReceivedEventArgs`
- [ ] 8.4 Move game-specific `PlayerMoveMessage` out of the framework
- [ ] 8.5 Remove "Phase 1/Phase 2" comments and legacy duplicate `GameStateChangeMessage` broadcast once protocol v3 lands
- [ ] 8.6 Document sync-over-async wrappers; add `ConfigureAwait(false)` throughout Core
- [ ] 8.7 Nullable reference types and analyzers on Core
- [ ] 8.8 EOS: decide whether the generated 9.6k-line binding stays vendored or becomes a package reference

---

## Phase 9: Samples, docs, release

- [ ] 9.1 `NetworkStateManagement` (`/Users/dominique/Development/CartBlanche/MonoGame-Samples/NetworkStateManagement`) runs unmodified-as-possible on UDP, Steam, then EOS/Android/iOS. Note: it exercises the session lifecycle (create/find/join/invite/lobby/end reasons) but **not** `SendData`/`ReceiveData`
- [ ] 9.2 Add a small data-path sample (movement sync over reliable + unreliable) so the data path has a real acceptance test beyond ShipGame
- [ ] 9.3 README: replace "covered by tests" with accurate per-back-end status; add compat matrix (XNA member → status)
- [ ] 9.4 Update ShipGame for the `Write(byte[])` and any other intentional API changes (it is the only known consumer)
- [ ] 9.5 Before the first NuGet publish: freeze the public API surface, decide the compatibility/semver policy, set protocol-version negotiation if multi-version interop is wanted, then publish versioned previews for each package

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
