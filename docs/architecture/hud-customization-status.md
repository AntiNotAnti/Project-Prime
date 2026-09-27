# HUD customization implementation status

The supplied multi-stage HUD plan is **not yet fully complete**. This document
separates working code from remaining acceptance work; a profile field alone is
not counted as an implemented editor or renderer feature.

## Implemented

- Versioned local JSON profiles in `Savedata/hud-profiles`, next to the existing
  settings. `active.json` is the active profile; named profiles are independent
  files. Existing Features values remain readable/writable for migration.
- Migration for Classic/Project Prime, crosshair style and size, radar visibility,
  background and outlines, kill-feed visibility, weapon presentation, and native
  reticle opacity. Classic and Project Prime keep their original layout paths.
- Fourteen registered components and nine anchors, uniform logical units, safe-area anchors and inverse transforms.
  Missing nested fields inherit element defaults. Unknown fields are ignored;
  invalid values are bounded. Unsupported schemas are rejected rather than
  silently downgraded. File names cannot supply paths.
- Detached validation and runtime publication. JSON, dictionaries and geometry
  construction stay outside the game draw path. Runtime values and geometry are
  immutable, with a generation increment at publication.
- Atomic active/named file replacement, last-good backup recovery, bounded file
  size/depth, and a nonfatal migration/load warning displayed in settings.
- Shared cached rectangle/annulus crosshair geometry for game and editor. Existing
  five shapes, additional geometric presets, inner/outer arms, T style, square/circle/diamond/cross dots,
  brackets, ring, independently styled parts, outline color/opacity, tint, opacity, scale, sparse per-weapon overrides and an Imperialist zoom
  override. The original reticle position remains the drawing center.
- `PPCH1:` crosshair sharing; bounded JSON import/export and named profile save/load.
- Scoped custom-mode transforms for health, ammo, inventory, radar, score, timer,
  kill feed and combat notifications. Scopes restore all state on disposal and
  do not mutate HUD sprite coordinates, world state or camera projection.
- Health/ammo number and gauge visibility, sizing, orientation, thresholds and
  colors. Inventory orientation, icon scale, spacing, ammo/unowned visibility,
  unowned icon opacity and selected outline. Radar now has eight styles, shared
  contact/frame geometry, dynamic editor bounds, orientation, reduced range,
  elevation, facing, objectives, trails and presentation-only scanner effects.
  See [enhanced radar](enhanced-radar.md) for architecture and diagnostics.
- Kill-feed row count (1–10), lifetime (1–10 seconds), spacing and weapon/HS/TK
  indicators. Hit-marker X/plus/dot geometry, size, gap, thickness, color, opacity
  and visibility consume the existing marker signal; event validity is unchanged.
- Global text/icon scaling, reduced transparency and notification motion, blue/amber
  and high-contrast palettes, notification spacing/count/latest-only controls.
- Predefined conditional visibility (multiplayer, spectator, damaged, ammo not
  full, objective modes). Conditions can hide available information, never enable
  information disallowed by the existing game code.
- HUD Studio from Settings → Display → Edit HUD: canvas/tree selection, drag,
  grid/center/edge/safe-area/neighbor snapping with alignment guides, Alt-click
  overlap cycling, locking/lock-all, anchors, opacity/tint/size controls, keyboard nudges,
  Shift-click group movement and left/top alignment, bounded undo/redo with
  coalesced gestures, property/element/section resets, presets and aspect previews.
- Detached editor draft: Cancel does not publish or persist it; Use in settings
  waits for the outer Apply. Named Save deliberately writes a library copy without
  activating it. Editing a shipped preset activates a custom copy.
- Controller routing for selection, canvas move mode, resize and back; responsive
  stacked inspector and touch drag/pinch handlers. No static reference to an
  editor view/draft is retained.
- `-hudprofile NAME`, `-crosshairprofile NAME`, `-hudpreview DIRECTORY`,
  `-hudmetrics`, golden PNG comparison, and legacy crosshair arguments.
  Named profile lookup uses the normal user-data directory in CLI runs.

## Remaining implementation and acceptance

1. Complete shared text/sprite rendering for preview/match parity. Crosshair parts,
   gauge geometry and radar frames now share descriptions. Text uses Avalonia's
   preview font; inventory, score, opponent/objective and notification previews
   still contain schematic sample content. Classic's complete native visor is not
   reproduced in the editor. Preview scenarios are available.
2. Finish separate rank/score-limit, weapon presentation, spectator name/status,
   name-tag and damage-indicator controls. Opponent info, grouped objective widgets,
   FPS diagnostics and independently detachable gauges are integrated. Match
   gates remain authoritative. Playing/POV/free-camera/replay visibility is present.
3. Zoom ring/scale/opacity/dot use the zoom crosshair profile, but animated zoom
   transitions and optional movement/fire/charge/event effects remain unimplemented.
   Per-part styling, all four dot shapes and sparse property inheritance now work.
4. Add confirmed event-specific hit-marker colors/shapes/timing without exposing
   speculative headshots or changing confirmation semantics.
5. Complete property descriptors/reset-property for non-crosshair controls, box
   selection, center/right/distribution tools, and actual game layer ordering.
   Shift-click groups, group dragging, left/top alignment and reset-section work.
   Layer remains stored/bounded but is not used to reorder game draws.
6. Physical controller verification and complete trigger/category behavior; Android
   SDK/JDK/workload and real touch/pinch/rotation/pause/resume tests; Windows/Linux
   runtime, fullscreen/borderless and window resize checks. Stick movement/resize,
   fine horizontal adjustment, X reset and Y visibility have synthetic input tests.
7. Approve native match golden images across the platform/resolution matrix and
   remove remaining whole-HUD allocations in all contexts. `-hudpreview DIRECTORY`
   exports deterministic **editor** captures. `tools/hud-golden-check.py` compares
   supplied approved PNGs without changing them. `-hudmetrics` measures the 2D
   HUD-object pass, not only the new profile code. Native HUD models are separate. Neither tool itself proves acceptance.
8. User assets remain explicitly deferred as requested in Phase 42. Advanced
   animations and spectator-specific property overrides are still later work;
   contexts currently select visibility rather than distinct property values.

## Checks

`dotnet run --project tools/hud-check` exercises all anchors across eight output
sizes, inverse math, legacy shape geometry, nested default inheritance, migration,
serialization, limits, backup repair, named-path rejection, detached drafts,
undo/redo/coalescing/history bounds, overrides, sharing and allocation-free runtime
traversal.

`dotnet run --project tools/hud-ui-check` instantiates the real Avalonia editor,
checks input/history/save/cancel/controller paths, captures desktop and narrow
screens, and compares aim-assist results with normal, 800%, and disabled crosshairs.
These are local diagnostics and do not require extracted game data.

Full game captures require a working paths.txt. Use `PROJECT_PRIME_USER_DATA` for
an isolated test settings directory on macOS; do not overwrite the installed app.
Other development tasks may concurrently change this checkout. An isolated test
snapshot can exclude unrelated unfinished work; report that distinction rather
than claiming a successful build of a concurrently broken combined workspace.

## Validation run — 2026-09-27

- Pure HUD suite: 437 assertions passed; 100,000 iterations of the measured runtime
  lookup/geometry/layout path allocated zero managed bytes.
- Actual Avalonia editor suite: 27 checks passed in the combined working checkout,
  including guide snapping, undoable lock-all, pointer drag, controller routing,
  responsive layout, transactional save/cancel, and aim-assist independence.
- Desktop compilation passed in the combined checkout. Windows x64, Linux x64 and
  server builds passed in an isolated source snapshot (18 desktop / 10 server
  existing warnings). The snapshot excluded concurrent unfinished cosmetics work;
  these cross-builds preceded the final editor snapping addition.
- Real accelerated macOS match captures passed for Project Prime (1280×720),
  custom (1280×720), Classic (1492×811; macOS clamped the requested 1920×1080),
  Accessibility (1440×600) and Competitive portrait (450×800). The Accessibility
  capture caught edge clipping; preset offsets were corrected and recaptured.
  The corrected health bar and radar stay inside the frame.
- The custom match used `-drawrate 3`: its frame-timing diagnostic reported 608
  simulation steps, no draws advancing gameplay and no Lockjaw draw-time RNG
  changes. This is a targeted presentation check, not full gameplay equivalence.
- Captures used extracted AMHE1 data already installed under Application Support,
  an isolated `PROJECT_PRIME_USER_DATA`, and the validation snapshot. The installed
  `/Applications/Project Prime.app` and normal user settings were not modified.
- Android workload/device tests, native Windows/Linux execution, comprehensive
  screenshot parity and whole-HUD allocation measurements remain outstanding.

## Second implementation pass — 2026-09-27

- Expanded the shared geometry and editor features described above. Profile format
  remains schema 1: additions are optional, old whole-crosshair overrides retain
  their behavior, and newly created overrides inherit unmodified properties.
- Model checks: 479 assertions; cached part traversal/layout measured zero managed
  allocations. Avalonia checks: 32, including stick gesture coalescing, Y visibility,
  group alignment, save/cancel, and actual aim-assist invariance.
- Final combined-checkout Windows/Linux/server builds passed with zero errors
  (19 desktop / 10 server warnings). Final desktop editor checks and the 16-image
  editor export passed. Cross-compilation does not imply native platform execution.
- Whole-HUD measurement first found 31,109.45 bytes/draw across 1,824 draws. Reusing
  owned texture upload arrays and formatting routine scores into stack buffers
  reduced that run to 127.02 bytes/draw. Further text-key/boxed-color fixes were
  measured separately: 16.68 bytes/draw across 2,397 draws (peak 352 bytes, mean
  draw time 4.93 ms on this Mac). These measurements cover DrawHudObjects, not
  DrawHudModels, and include initialization/transient values. This is a measured
  improvement, not a claim that every HUD context is allocation-free.
- Golden comparator smoke checks accept identical images and reject a deliberately
  changed image. No screenshots were silently promoted to approved goldens.
- All rendered checks use isolated settings; the installed application is untouched.


## Enhanced radar implementation — 2026-09-27

- All eight styles, additive profile fields, grouped inspector, radar-only presets,
  shared preview contacts and dynamic bounds are implemented. Classic and Project
  Prime retain Basic defaults. Existing migration, profile history, contexts and
  custom layout remain the integration boundary.
- Native objective locator semantics cover Bounty, Bounty Teams, Capture, Defender,
  Defender Teams, Nodes, Nodes Teams and Prime Hunter. Replay uses the same
  locator-only helpers and a separate scratch list, without advancing native HUD
  timers/messages or changing the native locator draw list.
- Trail history is fixed-size, sampled at simulation frequency and reset by life,
  occupant, match, teleport and seek discontinuities. Replay pose lookahead is
  prepared in presentation capture, outside radar drawing. No new network packet,
  gameplay setting or simulation-state dependency was added.
- Pure HUD suite: 972 assertions, including zero warmed geometry allocations and
  allocation-free layout after forced collections. Actual Avalonia suite: 91
  checks, including all radar presets, resizing, dragging, undo/redo/reset and
  narrow/portrait layout. The existing replay timeline input checks also pass.
- Real contact integration: 600 frames in each of eight objective modes; eligible
  hunters, pickup filters and authorized objectives; zero warmed collection bytes;
  unchanged gameplay hashes before/after collection. Synthetic fixtures select
  installed rooms with the appropriate native objective entities; the generic
  Prime fixture seeds a winner before the read-only collection checks.
- OpenGL measurements: Scanner with trails produced zero radar bytes across 802
  warmed draws; Tactical/Capture in a 450×800 portrait window produced zero radar
  bytes across 796 warmed draws. Setup/frame/collection/trails/contacts/labels were
  measured separately. This does not claim zero allocations in the entire HUD.
  The tests caught and removed player-list iterator boxing and reflection-backed
  anchor validation from the radar hot path.
- Basic screenshot comparison: seven consecutive 1280×720 radar-region captures
  exactly match the prior renderer. These are region comparisons, not a claim of
  whole-frame or all-platform pixel equality. Later world images diverge outside
  the scope of this radar comparison. All eight editor style captures were rendered
  and representative Basic, Tactical, Holographic and Competitive images inspected.
- A Scanner match at three draws per simulation step completed 930 steps with no
  draw advancing gameplay and no draw-time RNG changes. Replay restore validation
  passed 1,801 gameplay/presentation hashes, 61 file/clip seek comparisons and
  frozen EOF, with foreground state preserved. The real GL replay cadence suite
  also passed 60/120/144/240/360/540 Hz: 600 identical gameplay frames per cadence,
  with no draw-time state changes, dropped simulation steps or stalls.
- Desktop and Windows/Linux/server cross-builds pass. Tests use isolated user data;
  native captures run on macOS. An isolated source snapshot was also used while
  unrelated audio edits were temporarily between compilable states; the combined
  checkout subsequently built successfully. No unrelated edits were reverted.
- Android builds successfully after installing the Android workload and SDK
  dependencies using the repository's CI dependency target. The local JDK 27
  produces a version-detection warning; the build finishes with no errors.
  Native Windows/Linux/Android execution, Android orientation and
  suspend/resume, physical touch/pinch and the full requested real-match golden
  matrix remain device-dependent acceptance work. Cross-builds do not establish
  those results.

Local validation artifacts are under `/tmp/prime-enhanced-radar-validation`:
`editor/`, `baseline/`, `basic/`, `basic-parity/comparison.json`, `scanner/` and
`tactical-capture-portrait/`. They are diagnostic captures, not automatically
approved repository goldens.
