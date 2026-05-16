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

### Platform Test Execution Policy (required)

When adding or updating backend tests, classify each test project into one of these modes and enforce it in CI:

- Host-runnable tests
  - Target host-compatible framework (for example net9.0).
  - May run with `dotnet test` on standard CI runners.
- Platform-targeted compile validation
  - Target platform framework (for example net9.0-android, net9.0-ios).
  - Validate with `dotnet build` in standard CI runners.
  - Do not run with `dotnet test` on non-platform hosts.
- Runtime smoke tests (opt-in)
  - Execute only on emulator/simulator/device jobs prepared for that platform.
  - Gate with explicit env var or explicit test attribute.

Default rule for mobile backends:
- Keep platform TFM for backend test projects.
- Use compile-only validation in regular CI.
- Add separate optional runtime smoke jobs for execution.

### Mobile Test Project Decision Tree

Use this decision flow before creating or modifying test projects:

1. Does the test project reference a platform-only backend project?
   - Yes: keep platform TFM and use compile validation in regular CI.
   - No: prefer host TFM and run with `dotnet test`.
2. Does the test require real runtime APIs (Play Games, Game Center, native transport)?
   - Yes: put it in an opt-in smoke suite for emulator/simulator/device.
   - No: use seams/mocks and keep it in regular CI validation.

### iOS Simulator and Signing Guidance

For iOS backend projects and test projects:

- Build/package iOS artifacts only on macOS runners.
- Install iOS workload in any job that builds or packs iOS projects.
- For compile validation, use simulator-oriented settings and avoid device-signing requirements.
- For runtime/device execution, configure signing/provisioning explicitly in dedicated jobs.

### CI Runner Matrix Rules

Use these defaults unless user requests otherwise:

- Core/shared and Steam host tests: all supported runners.
- Android backend tests:
  - Regular CI: compile-only on supported host runners.
  - Runtime execution: emulator/device job only.
- iOS backend tests:
  - Regular CI: compile-only on macOS runner.
  - Runtime execution: simulator/device job only on macOS.
- iOS package build/publish inputs: macOS jobs only.

### CI Consistency Rule

Validation and pack pipelines must use the same backend-test policy:

- If validate uses compile-only for a backend, pack must also use compile-only checks.
- Avoid mixing compile-only in one job and runtime execution in another unless intentionally documented.

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

## Warnings Hygiene Checklist

Before completion, resolve or intentionally suppress with rationale:

- Async test methods with no await.
- Platform manifest warnings (for example iOS orientation/manifest defaults).
- Package dependency version mismatch warnings.
- Platform build warnings introduced by new project defaults.

Do not mark done while avoidable warnings remain in newly added backend/test projects.

## Completion Criteria

Only mark done when all are true:
- Backend compiles and wires through existing abstractions.
- Existing backends still compile and pass relevant tests.
- At least one end-to-end happy path is validated for each in-scope feature.
- Residual risks and next smallest increment are documented.
- Test execution policy is explicitly documented per backend (host-runnable vs compile-only vs runtime smoke).
- CI jobs are runner-correct for each platform (especially iOS macOS-only build/pack paths).

## Backend Parity Matrix Requirement

Update docs with a concise parity matrix for each backend touched in the change:

- SignIn
- Leaderboards
- Achievements
- Achievement media
- Networking
- Runtime smoke

For each cell, mark one of: implemented, partial, compile-validated only, runtime-validated.

## Reusable Workflow Snippets

Use these patterns when editing CI:

### Compile-only mobile backend validation

```yaml
- name: Build Android backend tests
  if: runner.os != 'Windows'
  run: dotnet build Tests/Android/MonoGame.Xna.Framework.Net.Android.Tests.csproj -c Release --no-restore

- name: Build iOS backend tests
  if: runner.os == 'macOS'
  run: dotnet build Tests/iOS/MonoGame.Xna.Framework.Net.iOS.Tests.csproj -c Release --no-restore
```

### macOS-only iOS pack job

```yaml
pack-ios:
  runs-on: macos-latest
  needs: pack
  steps:
    - uses: actions/checkout@v5
    - uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 9.0.x
    - run: dotnet workload install ios
    - run: dotnet restore iOS/MonoGame.Xna.Framework.Net.iOS.csproj
    - run: dotnet build iOS/MonoGame.Xna.Framework.Net.iOS.csproj -c Release --no-restore
    - run: dotnet pack iOS/MonoGame.Xna.Framework.Net.iOS.csproj -c Release -o artifacts/ios --no-build
```

### Optional runtime smoke gating

```yaml
- name: Run iOS runtime smoke tests
  if: runner.os == 'macOS'
  env:
    MGNET_IOS_SMOKE: "1"
  run: dotnet test Tests/iOS/MonoGame.Xna.Framework.Net.iOS.Tests.csproj -c Release --filter "Category=Smoke"
```

## Output Format For Each Run

Return:
- Concrete files changed and why
- Validation results (build/tests/smoke)
- Residual risks
- Next smallest implementation step
