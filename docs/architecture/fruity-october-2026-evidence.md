# October 2026 Fruity plan: implementation and acceptance evidence

Date: 2026-10-11. Base: `ec9d63f7afa7ddd1220cf9441f619c3576294014`.
Working branch: `fixes`. PR01 was declared complete by the
user and is not reimplemented here. Changes are local; no release or deployment
has occurred. This record separates completed implementation from unexecuted
release acceptance. It is not a claim that every platform or production gate passed.

Follow-up: the user additionally requested remote Shock Coil visibility and moving
Lockjaw bomb-jump fixes. Those changes advance the branch to protocol 51; see
[netcode regression evidence](netcode-remote-coil-lockjaw-fixes.md). The build,
archive, and benchmark results below describe the earlier plan implementation,
not these subsequent changes.

## Disposition by work item

| Plan item | Result |
|---|---|
| 00 Baseline | Reproducible native solo/one-bot campaign with source/diff/binary identity, fixed camera, independent renderer switches, GL identity, timings, counters/logs and separate image captures. See `fruity-baseline-contract.md`. |
| 01 Solo rendering | Already completed, per user. Existing renderer architecture preserved. |
| 02 HUD | Nonpositive alpha returns before any texture/uniform/draw work. Removed the inappropriate world-program cosmetic upload from the RTT pass; world pass still owns its reset. Real GL assertions cover draws, binds, visible pixels and following mask. |
| 03 Fog | Reproduced wrong-program upload leaving scene fog stale. Room setup now changes metadata only; world uniform update uploads authored fog with the correct program. Real GL checks cover distinct fog settings and program relinking/cache invalidation. |
| 04 Display | Existing 39 pacing policy assertions pass. No reproduced GLFW sizing/pacing defect; no speculative Qt backport. Physical fractional-DPI/multi-monitor/sleep matrix remains open. See `fruity-pr04-evidence.md`. |
| 05 Spectator | Stable normalized camera basis, hunter mouse scale/pitch bounds, focus-neutral hardware latch, held fire suppression. Space keeps its existing toggle/pause meaning; E ascends in desktop and Android live/replay cameras. See `fruity-pr05-evidence.md`. |
| 06 PK3 | Validated PK3/ZIP discovery, explicit level/scale/fallback choice, immutable bounded content cache, deterministic source/dependency/options/version invalidation, traversal/bomb/ambiguity limits, transactional publication and reimport rollback. Authored edits and identity survive reimport. |
| 07 Traversal | Typed push/teleport metadata, deterministic targets, swept feet contact, native movement/form/boost integration, safe destinations, lifecycle/cooldowns, authority observation through existing owner movement, format-3 bundles and replay capsule v5 with old readers preserved. See `q3-traversal-contract.md`. |
| 08a Combat | Reproduced owner and accepted remote shots dealing 6 HP through a wall. Shared native Sylux muzzle obstacle/impact guard fixes both without changing ammo/fire or protocol policy. Open/lateral-cover positive controls and actual Lockjaw bomb placement pass; see `sylux-native-muzzle-regression.md`. |
| 08b Fall damage | Reproduced raw HP scalar incorrectly converted as fixed-point: a landing expected to deal 21 HP dealt 1 HP. Corrected the scalar conversion; native floor fixtures cover thresholds, speed, transformations, pad grace and authority damage attribution. |
| 08c Equip | Native spawn/common selection fixtures across all hunters and rule sets; no reproduced equip defect. Physical input routing remains open. |
| 08d Touch | Reproduced and fixed final roll delta dropped when pointer releases between fixed steps. Actual Android touch source is exercised by deterministic sampling/lifecycle tapes. See `fruity-pr08-evidence.md`. |
| 09 Server | Strict license packaging, delivered tarball permission/architecture/manifest validation and Linux x64 extracted-archive smoke gate; server updater uses existing staged transaction/rollback/recovery. See `fruity-pr09-evidence.md`. |
| 10 Netcode | Source-backed present/partial/missing matrix plus concrete pickup/freeze packet/replay migration designs. This plan item did not change protocol 50 (the later bomb fix advances it to 51). Missing receipts and exact freeze timers are documented, not falsely marked implemented. Successor wire changes remain gated on current release acceptance and their lossy scene reproductions. See `fruity-netcode-gap-matrix.md`. |

## Executed checks

SDK: `/Users/jarrett/.dotnet/dotnet` 10.0.401. Host: macOS 27.0, Apple M4 Pro.
Real GL context: OpenGL 2.1, Metal driver 91.7. Local extracted game revision:
AMHE1, supplied by the user; no game files are added to source control.

- Desktop Debug source/tool and desktop Release builds: PASS, no errors.
- Android ARM64 managed Release compilation: PASS (`-r android-arm64 -t:Compile`),
  0 errors, 53 existing warnings including local JDK-version detection. This is
  not an APK build, signing, installation or physical-device acceptance.
- Linux x64 and ARM64 server self-contained single-file publishes: PASS. Both
  actual candidate tarballs pass architecture/execute/license/manifest validation
  and no-game-assets package gates; neither has been executed on Linux.
- Map editor contracts: **469 checks PASS**, including malicious archives,
  source/cache invalidation, authored reimport preservation, failed-publication
  rollback, old map behavior and typed traversal roundtrip.
- Native Q3 movement: **2,660 checks PASS**, all seven hunters in biped/native alt
  form, 60 fixed movement steps, boost interruption, swept feet, teleport safety,
  lifecycle/cooldown, authority observation dedup and checkpoint continuation.
- Content-free real GL final-composite/HUD/fog checks: PASS; captures at
  `artifacts/fruity-plan/render/hidden.png` and `visible.png`.
- Replay format: **3,013 checks PASS**. Replay world checks: PASS across every
  selectable multiplayer mode, detached restore/continuation/file/frozen-clip seeks.
- Real GL replay cadence: **600 identical gameplay hashes per cadence** at
  60/120/144/240/360/540 virtual Hz, including render-only frames, zero dropped
  steps/stalls. The 540-Hz case uses a virtual clock (numeric cap clamps to 500);
  none of these are claims about physical panel refresh or measured throughput.
- Spectator camera/focus-neutral contracts: PASS, including ten focus cycles.
- Desktop pointer/action sampling: **1,788 assertions PASS**. Actual Android
  `TouchControls` linked tests: PASS across 30/60/120/144 rendering with 30-Hz
  samples and 60-Hz simulation, release, consumption and lifecycle reset.
- Native fall/equip diagnostic: **406 assertions PASS**, all seven hunters ×
  Vanilla/Balanced/Enhanced; exact HP and authority receipt, threshold/exemptions,
  pad grace and 42 respawns with common Missile selection.
- Sylux native muzzle diagnostic: **27 assertions PASS**, after a before-fix run
  with exactly two blocked-shot failures. Combat arbitration: **94 PASS**.
- Shot source journal: **84 assertions PASS**; shot proof scene: **324 PASS**;
  movement validation shadow: PASS; continuous snapshots: **337 PASS**, maximum
  live snapshot 1,172 bytes. Network evidence self-audit: **31 PASS**.
- Archive policy: **9 PASS**; release manifest writer: **4 PASS**; updater suites:
  PASS, including direct server faults/rollback/tamper/retained image handle and
  preservation of operator data.
- Baseline planner contract: PASS. No-game-assets and shipped-map source gates:
  PASS. OpenGL-only gate: PASS in a clean non-ignored source snapshot. Running
  it directly in this checkout sees a pre-existing ignored retired tools directory;
  that local data was preserved. `git diff --check`: PASS.

## Stored renderer baseline

`artifacts/fruity-plan/baseline/manifest.json` records eight completed paired
solo/one-bot profiles at 60/120/144/240 caps; `baseline-ab/manifest.json` records
fourteen profiles at 60 with each of the six requested legacy switches isolated.
Each profile has a separate timed run and fixed-step image run. Camera is
`(4.574463, 5.241699, -14.647217)`, facing `(-0.15, 0, 1)`, Sanctorus,
1280×720, Original, render scale 100, VSync off, shadows/outlines off, Samus.
This is a fixed free-camera baseline, not a HUD/viewmodel/shadow or walking-route
acceptance matrix. An initial camera-overlap trial is retained separately and
excluded from this comparison.

All runs completed without dropped simulation steps. Every frame-zero world
capture matches its immediate no-simulation redraw byte-for-byte. Five legacy
arms match the default world PNG exactly. Display lists differ in both profiles:
about 117,200 of 921,600 pixels, mean channel delta 0.640/255, maximum 144/255;
`baseline-ab/legacy-list-pixel-comparison.json` retains the measurements. The
images were inspected; this difference is recorded, not declared pixel parity.
No FPS improvement or universal renderer equivalence is claimed. Requested
240 does not imply achieved 240; raw achieved rates and p95/p99 are in the JSON.
Apple GL timer queries were unavailable, so no GPU-time measurement is invented.

Baseline engine SHA-256:
`beb7b66d62b31992b00f7e2cc5c6ea8855b5346d39e5cea72e1791c1edcd4316`,
MVID `4433705d-9200-4b83-8443-25624b77dc9d`. This is the renderer-evidence build
before the final Sylux correction; the renderer is unchanged by that correction.
Final source file hashes are recorded separately in
`artifacts/fruity-plan/final-build-source.json`. Asset images remain ignored local
artifacts; they are not distributable repository fixtures.

## Acceptance limits and rollout

The new CI gates are executable but GitHub Actions has not been run for this
uncommitted branch. No Windows/Linux real GPU or Android physical GLES, touch,
pause/resume, APK signing/install or macOS signing acceptance is inferred from
managed compilation. No Linux x64/ARM64 real published archive startup,
oldest-supported-glibc environment, service restart or staging deployment was
available on this macOS host (Docker daemon unavailable). No network successor
is deployed or represented as certified.

Q3 tests call the real native movement kernel and checkpoint appendix. They do
not certify a full custom-map late-join/loss/spectator/replay-seek campaign.
Existing owner movement remains owner movement; authority observes traversal
rather than becoming an untested second pose author. Scripted Q3
`target_teleporter` remains unsupported with a warning. Version-1/2 legacy maps
keep native pad behavior; version 3 must be loaded by compatible builds.

Do not release based solely on these local results. Outstanding gates from the
plan remain: target-device visual/input/display acceptance, owned-map platform
launch equivalence and full network traversal, broader native combat/form/input
fixtures, delivered Linux startup/service/platform acceptance, exact-commit CI,
and protocol-50 release prerequisites before any 10a/10b successor.

Rollback: revert each feature's paths independently. Renderer diagnostic flags
remain available for investigation; they do not replace a correctness fix.
Legacy maps do not opt into typed traversal. Preserve a compatible reader for
already-created version-3 bundles/v5 checkpoints when rolling back code. Server
`-noautoupdate` disables automatic installation; transaction recovery remains
available. No experimental protocol is enabled.

Final logs are retained under `artifacts/fruity-plan/logs/`; `verified-artifacts.json`
records DLL/candidate archive SHA-256 values. The final production source hashes
match the saved build snapshot. Final renderer/cadence repeats include the Sylux
correction and remain PASS. These artifacts are ignored local evidence.
