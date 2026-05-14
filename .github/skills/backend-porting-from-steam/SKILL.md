---
name: backend-porting-from-steam
description: "Use when: implementing a new platform backend (Android, iOS, Epic, or others) for this repo using the Steam backend as the reference pattern; defaults to full Steam-equivalent scope including SignIn, Leaderboards, Achievements, Achievement media, and Networking, with support for narrowed 'only' requests."
---

# Backend Porting From Steam

## Purpose

Deliver a minimal, production-minded backend slice for a target platform while preserving existing behavior for Local/SystemLink and Steam.

This skill is for adding platform backends that implement some or all of:
- Guide SignIn
- Leaderboards
- Achievements and achievement media
- Network session factory/session/gamer path

Use the existing Steam backend in this repository as the reference architecture and test style.

## Required Inputs

Collect these before editing code:
- Target backend name (example: Android, iOSGameCenter, EpicOnlineServices)
- Target package/project path and desired NuGet split
- Runtime policy mode expectations (strict vs fallback)
- Any explicit API compatibility constraints

Feature scope default and override rules:
- Default scope is full parity with Steam backend and includes:
  - SignIn
  - Leaderboards
  - Achievements
  - Achievement media
  - Networking (host/find/join/reliable message)
- If user request includes the word "only", implement only the explicitly listed features and skip all others.
- If user lists features without "only", treat that list as the requested scope.

If required non-scope inputs are missing, ask a short numbered question set once, then proceed.

## Repository Reference Pattern

Use Steam implementation in this repo as the canonical pattern for:
- Runtime bootstrap and callback lifecycle
- Composition root wiring for Guide, network session factory, leaderboard and achievement providers
- Provider interfaces and service routing
- Test structure (composition tests, provider behavior tests, session smoke tests)

Match shape and behavior first. Replace only platform-specific API calls and lifecycle semantics.

## Implementation Workflow

0. Resolve scope from request text
- If no explicit feature list is provided, implement full Steam-equivalent scope including Networking.
- If request contains "only", enforce exact-feature scope and do not add extra features.
- Confirm resolved scope in one concise sentence before edits.

1. Read architecture and current platform seams
- Read README and STEAM_INTEGRATION status.
- Locate the existing seams:
  - Net abstractions and service provider routing
  - GamerServices providers and local fallback layers
  - Steam composition root and runtime wrapper

2. Create platform package structure
- Add a separate backend project under a new platform folder when requested.
- Keep dependencies isolated to that project.
- Do not introduce platform SDK dependencies into core Net/GamerServices projects.

3. Implement runtime wrapper
- Add a platform runtime class analogous to Steam runtime responsibilities:
  - Initialize
  - Callback/event pump if needed
  - Shutdown and cleanup
  - Signed-in identity refresh bridge to SignedInGamer
- Fail clearly with actionable logs when native/runtime prerequisites are missing.

4. Implement service providers
- SignIn provider:
  - Integrate with Guide sign-in flow.
  - Set SignedInGamer signed-in state and gamertag consistently.
- Leaderboard provider:
  - Implement read/write calls with cancellation checks.
  - Preserve local fallback behavior when not signed in or platform runtime unavailable.
- Achievement provider:
  - Implement get/progress/unlock path.
  - Preserve hidden flag behavior when the target platform supports hidden achievements.
  - Store/persist stats where the platform requires explicit commit.
- Achievement media provider:
  - Implement icon/media retrieval.
  - Use bounded cache and null-caching on failure paths.

5. Implement networking path (if in scope)
- Implement target session factory/session/gamer classes through existing abstractions.
- Keep Local/SystemLink and Steam behavior intact.
- Maintain current lifecycle semantics unless the user explicitly requests a policy change:
  - deterministic session end reasons
  - no accidental host migration if unsupported
  - reliable message send/receive viability

6. Add composition root bootstrap
- Add a platform bootstrap helper mirroring Steam bootstrap behavior:
  - Configure providers and session factory
  - Enable live providers only after successful sign-in
  - Keep local persistence setup by game name

7. Add tests before claiming completion
- Required tests based on scope:
  - Composition root wiring
  - Provider routing signed-in vs signed-out
  - Achievement metadata projection including hidden flag where supported
  - Networking host/find/join and one reliable message flow if networking was implemented
- Add an opt-in smoke test harness for real platform runtime where feasible.

8. Validate build and focused tests
- Build core and new platform project.
- Run focused test files first, then broader tests if needed.
- Report concrete pass/fail outcomes.

9. Update docs
- Update README backend list and startup wiring examples.
- Update integration status doc for the new backend with:
  - implemented
  - partial
  - remaining

## Guardrails

- Do not rewrite unrelated networking subsystems.
- Do not break existing Local/SystemLink behavior.
- Do not break existing Steam behavior.
- Preserve public APIs unless a compatibility change is explicitly requested.
- Keep changes minimal, testable, and production-minded.
- Avoid temporary debug hacks in committed code.

## Completion Criteria

Only mark done when all are true:
- Backend compiles and wires through existing abstractions.
- Existing backends still compile and pass relevant tests.
- At least one end-to-end happy path is validated for each in-scope feature.
- Residual risks and next smallest increment are documented.

## Output Format For Each Run

Return:
- Concrete files changed and why
- Validation results (build/tests/smoke)
- Residual risks
- Next smallest implementation step
