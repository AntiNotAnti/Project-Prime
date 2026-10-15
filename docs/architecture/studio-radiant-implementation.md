# Studio and Radiant implementation ledger

Baseline: `f17a5de74d82f6b201b90ef7017414108e1a6629` on `AntiNotAnti/Project-Prime/main`.
Implementation branch: `codex/studio-radiant-implementation`. Work began October 14
and continued October 15, 2026. This ledger distinguishes implemented source,
automated evidence, native executable evidence and release approval.

## Baseline and preserved boundaries

- Remote main was refreshed; no open PRs were returned at the initial inspection.
- Backup reference: `d08107a8a7a7ca64f87e96780aff8c6646cc2443`. Individual
  shell/protocol/presentation patterns were adapted; the backup was not merged.
- Fruity master observed: `78af64877c95370c3a14f752ca0e1db8a50c39c4`.
- NetRadiant source pinned: `8216133984031afaa9a857b56ea66dd9c3d54b26`, with
  the separately recorded C++ compiler-portability patch and
  source bundle recipe.
- SDK `10.0.401`, local platform `osx-arm64`. The initial remote baseline checks
  were green: [baseline build](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38656704060).
  Those are baseline checks, not CI for this implementation. An attempted
  isolated local baseline build was interrupted; it is not recorded as passed.
- Community PR-00..14 and activation #559 are **do-not-reimplement** dependencies.
  Existing creator/mirror source defaults remain enabled. Explicit rollback flags
  remain available. CAMP-01..07 on main are regression boundaries.
- `.ppmap`, multiplayer/replay wire identities and original cartridge-backed
  campaign remain canonical. Optional authoring metadata never establishes map Ready.

## Source inventory and acceptance gates

| Workstream | Implementation | Acceptance boundary |
|---|---|---|
| ST-00 | Community status/default drift reconciled; baseline and gate ledger | Native baseline performance comparisons pending |
| ST-01 | Per-MapId source directories, one recoverable source/sidecar/native-adjunct commit, leases, conflict review, one undoable geometry apply | Four injected commit faults and real source/compiler checks pass; native JSON saves also reject stale revisions |
| ST-02 | Separate Avalonia Studio executable, BCL-only authenticated bounded IPC, independent instance forwarding, recovery/jobs/safe mode | Process and offline host suites; native desktop qualification tracked separately |
| ST-03 | Verified paired launcher, explicit Studio/menu routes, owner-dispatched exact package broker and scoped publishing capability | Authenticated real-process fixtures; actual gameplay launch remains separate |
| ST-04 | Canonical engine projection, owner-context OpenGL surfaces, public CGL/IOSurface GPU transport on macOS, Safe Mode | Actual Mac scene and lifecycle checks pass; cross-platform pixels, picking, high-DPI and performance qualification remain |
| ST-05 | Private replay documents/player, non-destructive authoring, bounded detached export worker, loopback audio | Two native Mac video/audio exports pass; historical/cosmetic fidelity and other platforms remain separate acceptance |
| ST-06 | Canonical map editor/compiler/package and existing Community operations hosted in Studio; Game owns runtime install/playtest | Offline host cycles and broker tests; staging creator service acceptance pending |
| RD-01 | Pinned independent fork source/build/bundle recipe, original gamepack and graybox, native Cocoa Qt5 ARM64 packaging and candidate CI | Windows/Linux editor/compiler builds and real BSP checks pass; bounded Mac GUI/source/package proof below; wider platform, dependency/source licensing and release signing remain separate |
| RD-02/03 | Strict supported geometry reconciliation with Prime metadata preserved, revision review, source watch and cancellation | Unsupported geometry stays native; no promise that all native maps convert |
| MAP-01 | Vote/load/rotation exact readiness gates, bounded failed-generation retries, traversal and authored pickup probes | Automated/native kernel evidence below; live 1v1/8-player and Android pending |
| ST-07/08 | Normal Game FORGE opens paired Studio; Studio exposes primary Radiant geometry, review/apply and explicit native fallback | Embedded/legacy entry remains explicit; full source removal is held for functional/platform parity |
| RD-04 | Primary Radiant workflow and trusted installed-tool discovery; reversible preference and native fallback | Native Mac candidate passes bounded real editor/source/package loop; wider platform/gameplay/release signoff remains pending |
| REL-01 | Paired binary/hash/architecture metadata, coordinated update lease/recovery/rollback, sibling macOS apps, desktop CI | Synthetic pairing/updater tests; actual previous-release upgrade and remote candidate CI pending |
| REL-02 | Reproducible commands, migration/fallback guide and release evidence contract | **Gate held**: no public release, deployment, owner signoff or device certification recorded |

## Verified evidence collected locally

Durable run logs are under `artifacts/studio-implementation/` (ignored build
artifacts, not cartridge data). Tests must be rerun after relevant source changes;
intermediate passing counts do not certify a later candidate automatically.

- BCL Community writer cohort: 7 checks, actual matching/different-version OS
  processes, alive-owner refusal and crash takeover. New coordinated writers use
  one queue; older binaries that do not implement its locks still require a
  matched-release rollout and old-version negative acceptance.
- Game and Studio build with zero warnings/errors; an actual dedicated-server
  compile also passes. Its metadata and restored dependency inventory contain no
  Studio-only authoring helpers or Avalonia/Studio dependencies. Desktop replay
  helpers are compiled only into the Studio engine projection; the Android
  shared-source boundary has been reviewed, but needs its actual workload build.
- Authenticated IPC/process suite: 22 checks, including actual child processes,
  independent instance scopes, forwarding and crash recovery.
- Standalone host self-test: 91 checks, ten offline map document lifecycles,
  edit/undo/save/autosave/recovery, cancellation and exact package construction.
  Includes inner and outer source ownership routing. Safe Mode creates no native
  viewports; native rendering has its own evidence below.
- Game broker: 58 checks, real compilation/publication, authenticated owner
  dispatch, busy/in-use/malformed/wrong-identity rejection with byte-identical
  live library/runtime, duplicate request idempotency and narrow `ppm1` capability.
  Community and arbitrary local packages are frozen into verified private staging
  before IPC; caller sources remain unchanged and staging is retired afterward.
- Map readiness: 52 checks, missing/corrupt/stale outputs, package byte mismatch,
  client load gate, bounded server rotation rejection/recovery and in-use runtime
  preparation fencing.
- Native map lifecycle: 60 checks across ten real single-view/four-view/single-view/
  closed cycles, resize, metadata edit/undo/redo/save, and zero owned surface/world
  counters after close. A native Studio screenshot shows the canonical rendered
  arena and selection overlay. This does not establish Game/Studio pixel parity.
- Native replay lifecycle: 31 checks across ten real preview/seek/comparison/
  resize/close cycles with the source recording hash unchanged. Both map and
  replay lifecycle suites pass after the canonical GL binding/cache lifecycle
  fix, including last-surface close and device recreation. Earlier native UI
  inspection exercised playback, pause and comparison; a final visual revisit
  after the small-preview overlay adjustment was unavailable because the Mac
  was locked.
- macOS GPU transport: 12 complete device lifetimes, two concurrent surfaces each,
  five sizes each (120 draw/resize callbacks). Exact pixel transport, context/FBO/
  viewport/scissor/sRGB restoration and failure cleanup pass with zero GL errors.
  Production preview uses GPU blits without pixel readback. Conservative cross-
  context synchronization still needs sustained performance measurement.
- Native source revision checks: 13; full map editor suite: 497. External edits,
  deleted sources, stale tabs, symlink retargeting and simultaneous cooperating
  saves cannot silently replace a newer source; dirty state remains recoverable.
- Radiant source suite: 69 content-free checks, 71 with the actual pinned compiler,
  including all four source/sidecar/adjunct crash boundaries and post-crash author
  saves. The existing transfer suite passes 51 checks; nine broad Community/replay/
  map suites pass with canonical package baselines unchanged.
- Replay document/worker suite: 16 checks, including actual never-ready and stalled
  child processes, cancellation/reaping, immutable snapshot lifetime and protected
  output aliases. Registered media children are killed and reaped before a worker
  exits after losing its parent. Native historical MP1 SANCTORUS exports at 720p and 1080p each
  contain 120 H.264 frames at 60 fps and exactly two seconds of AAC stereo 48 kHz
  audio. Audio is non-silent. Original SHA-256 stays
  `4B03FA493BEDC797E4BE5CE42E550326E68E28506227B9A1E1A5F3C982B81571`.
- A third 720p export exercises comparison views and two ordered reel segments
  switching from the current camera to the director. It also produces exactly
  120 frames/two seconds with stereo audio and unchanged source bytes. Final
  decoded frames were inspected: the overlay clears native HUD/radar in full-
  width and narrow comparison views, and captures omit the wall-clock FPS counter.
  An actual missing-parent worker exits with code 130 in 0.077 seconds without
  publishing an output; the separate child-registry check verifies media reaping.
- Native movement kernel: 2,662 checks across all seven hunters in biped and
  alternate forms. Native authored pickup kernel: 11,364 checks across 18 supported
  item types, respawn intervals, quotas and dropped lifetime.
- Additional pickup protocol/claim/network/readiness/replay assertions ran using
  existing suites. Local extracted AMHE1 data was read with private user/runtime
  namespaces: all 3,826 original files were SHA-256 checked unchanged afterward.
- Real Q3Map2 on macOS ARM64: source compiled, original sealed graybox produced a
  nonempty BSP46 with collision brushes and draw surfaces. The first attempt
  exposed inverted Prime `.map` winding; it was recorded as failed and corrected.
  Old exact Prime export headers select an explicit legacy reader; arbitrary
  malformed plane inversions remain errors. New exports use native Quake winding.
- Fork recipe tests: 5 checks reject extra/staged/untracked source and altered
  patch bytes. Source/patch/dependency/output receipts and corresponding upstream
  source are included in the local compiler candidate.
- The first remote Linux fork build exposed a GCC 13 explicit-vector initializer
  error; subsequent builds reached temporary-array errors in VFS and editor tools.
  A source sweep replaced those temporary arrays in five more editor call sites.
  The recorded portability patch covers these errors and mesh assignment. Native
  Q3Map2 rebuild, original graybox compile and all 71
  source/compiler checks pass again; the refreshed candidate includes the exact
  combined patch and source receipt. Current remote results are recorded in
  [PR #561 checks](https://github.com/AntiNotAnti/Project-Prime/pull/561/checks).
- Source revision `b3339620` builds the full Radiant editor/compiler on Linux and
  Windows and passes each real BSP check. Linux candidate upload passes. Windows
  upload exposed a mixed C:/D: artifact-root error after successful build/compile;
  the workflow now uploads platform outputs and dependency receipts separately.
  This is source/build evidence, not GUI or redistribution certification.
- Paired updater rollback/archive checks passed, including actual child-process
  denial of Game startup during an exclusive update and recovery afterward.
  The final BCL updater suite passes 119 checks, including real pre-lease legacy
  Game/Studio apphosts in flat and sibling-bundle layouts, physical/case aliases,
  interrupted recovery and unrelated-process controls. Bundle transactions refuse
  replacement while either exact apphost is alive. macOS one-click in-place
  updates remain disabled; users replace the complete paired folder manually.
  Python pair checks 9/9, release
  manifest checks 4/4, synthetic double-app macOS signing/extraction/tamper checks
  passed. Synthetic fixtures do not establish a signed production release.
- CI source `b3339620` passes standalone Studio contracts on Windows, Linux and
  macOS, Windows/Linux client delivery and all server deliveries. Android APK
  publication passes; device acceptance remains separate. The actual macOS
  package exposed portable PDB files being treated as nested code. Packaging now
  preserves them under `Resources/symbols`; the complete local two-app signing,
  extraction, manifest and tamper fixture passes with PDB inputs in both apps.
  The updated actual package jobs must pass before macOS delivery is accepted.
- CI `61f43e31` confirms both Windows/Linux Radiant candidate uploads, Apple
  Silicon package signing/extraction/pairing and Android package acceptance.
  A Windows transfer-test receipt race was exposed in that run; all receipt and
  rendezvous markers now publish by close-then-rename. Its full 51-check suite
  passes locally. Current exact-head checks remain linked above; no cancelled
  or still-running job is counted as passed.

## Native Mac geometry replacement candidate, October 15

The earlier private `8ffeaa2d` Mac copy contained Game and Studio only. It did
not contain the Radiant replacement; its 47 successful CI checks do not certify
these subsequent changes. The new candidate adds a separate native Cocoa Qt5
`Project Prime Radiant.app` discovered beside the paired Game/Studio apps.

- Normal Game FORGE launches Studio Maps through the paired trusted launcher,
  including repeated FORGE requests while the explicit embedded workspace is
  active. Navigation guards and delayed unsaved-settings intent are preserved.
  Process/route checks pass 33 cases; Main/Studio build with zero warnings/errors.
- Studio exposes Edit Geometry in Radiant, Review / Apply Changes and an explicit
  native geometry fallback. Its actual headless host passes 91 checks, including
  twelve ownership/primary-action routes. Opening/restoring documents never
  implicitly launches another editor. Tool discovery/bridge checks pass 50 cases.
- Workspace v2 uses one durable `func_group` per primitive with `prime_id`, plus
  explicit patch collision/tessellation keys. Stock comment stripping cannot
  erase IDs. Changed v1 workspaces are preserved for explicit reconciliation;
  unchanged source/identity/native baselines can upgrade transactionally. Strict
  v2 review rejects every unmarked or comment-only primitive before mutation.
  Source/compiler checks pass 106 cases with the actual pinned Q3Map2.
- Native Project Prime Save assigns containers to newly authored worldspawn
  primitives while preserving the same primitive nodes and selection. Ordinary
  brush Duplicate copies its container as a separate object with a fresh ID;
  Save As keeps existing IDs. Malformed multi-primitive groups refuse Save.
- The first real Mac launch exposed an invalid framebuffer during shader
  validation. The native bootstrap now supplies a complete temporary FBO, keeps
  shader validation enabled, and restores Qt's exact GL bindings afterward.
- Actual Candidate 03 GUI opened a protected six-object brush/patch fixture,
  moved a wall, saved, and quit cleanly. Strict review retained all six IDs,
  native labels/layers/locked/terrain/shade, patch collision/tessellation and
  every gameplay/project domain. Apply/one undo and package build/reload pass
  15 checks. A separate real Save As, ordinary selected-brush Duplicate, drawn
  brush, Undo/Redo, subsequent move and Save produced eight stable unique IDs.
  Strict review/package of those exact saved bytes passes 15 further checks.
  Actual input SHA is
  `e7d10ae0dee3cda5d129f4736fa961df8d74d0da29e0bbe0e82470206016a624`.
- Native package closure verifies 73 ARM64 Mach-O images, eleven flattened Qt
  frameworks, zero symlinks, source-backed editor artwork/provenance and ad hoc
  nested/app seals. Optional three-app canonical packaging and ownership/tamper
  fixtures pass. Private receipts/logs are in `artifacts/studio-radiant-mac/`.

This proves a bounded Mac geometry workflow, not complete Windows/Linux GUI,
physical multiplayer/device, UV/content corpus, performance or public-release
qualification. Existing native material bindings remain authoritative; new
Radiant shaders need an explicit native texture binding before publishing.

The broader `55cefa46` CI run subsequently exposed three independent failures:
Windows immutable-download handles blocked permanent purge, both macOS package
fixtures passed the input after `lipo -verify_arch`, and the Linux GPU toolbar
oracle expected an obsolete SAVE-button color. Follow-up fixes permit deletion
while an existing immutable reader finishes, use input-first lipo syntax, and
compare the complete arranged toolbar region against its UI-only reference.
The community suite passes 74 checks including an intentionally held reader;
the native packaging fixture passes locally; Release builds with zero warnings
or errors and three headless layouts confirm the new toolbar reference. Hosted
Windows deletion and Linux GPU execution require the follow-up CI revision.

## Required evidence still pending

These rows cannot become passed from a build, a source scan, or an empty/skipped test:

| Gate | Required evidence |
|---|---|
| Native Studio GPU | Game/Studio pixel parity, final responsive-preview visual revisit, picking/high-DPI/context-loss and hardware frame/resource metrics on each supported platform; Mac map/replay lifecycle/transport checks above already pass |
| Real editor | Windows/Linux GUI and broader UV/angled/patch/two-map/conflict corpus; bounded native Mac open/edit/save/quit and strict package loop above already pass |
| End-to-end Game | Offline scene playtest, 1v1 and 8-player FFA, bots/pickups/rotation/Ready/spectator, independent crash/quit |
| Replay | Historical custom-map/POV/cosmetic/projectile/audio fidelity and full corpus/platform coverage; bounded Mac preview/lifecycle, overlay/multiview/reel and two-resolution video/audio checks above already pass |
| Android | Real arm64 install/join/spectate/room-transition/error UI; desktop payload absent |
| Community staging | Revision/parent conflict, visibility/privacy, tickets, concurrent mixed-app jobs, crash recovery, trusted CDN/origin failure |
| Install/release | Actual paired candidate on each RID, previous-release upgrade/downgrade/rollback/relaunch, exact-head remote CI, seals/signatures |
| Performance | Identical-hardware before/after startup/idle/frame-time/upload/seek/export metrics; investigate >5% unaffected Game regression |
| Licensing | Per-component fork/dependency source+notice review and license-cleared assets before any redistribution |
| Owner release approval | Explicit release/cutover signoff after the above evidence; none inferred from implementation request |

Rollback keeps native JSON editing, Game Community, quick Theatre, recordings,
killcams, runtime compiler/Ready and original user sources. Disabling creator or
mirror features requires explicit `=0` flags; leaving variables unset enables them.
Never delete or rewrite map/replay archives or user assets to roll back a tool.
