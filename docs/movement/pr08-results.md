# PR-08: Authoritative movement choice and recorded labels

Classic remains the default. Both launcher implementations now expose one
Movement choice, Classic or Momentum, through the existing rule draft and
server command paths. This changes presentation and configuration, not movement,
packet layouts, profile coefficients, replay schemas or fixed 60 Hz timing.

The RmlUi rules sheets retain Third Person at online index 16/offline index 12
and add Movement at 17/13. Native and managed action parsers use the matching
bounded ranges. Host contracts own their bounds so source-only Android harnesses
remain independent of Core editor implementations. Existing boolean flag storage
stays separate from the movement enum.

Guests review accepted rules; the owner submits a draft and waits for the actual
server publication. Stale drafts, authority changes, pending requests, reconnect,
new match generations and reload are covered. The legacy choice retains a newer
edit across an older acknowledgment and refreshes from authoritative changes.
Offline Apply uses existing transactional persistence; unknown saved models need
an explicit selection, Cancel discards changes, and a failed save rolls back.
Campaign retains Classic without discarding the saved practice preference.

Lobby/session status and replay details display the effective mode. Labels use
recorded Match/Session facts, compressed construction packets or validated world
checkpoints. Contradictory, malformed, unsupported or missing current facts show
Unknown. The derived property is excluded from archive metadata JSON and cached
once per metadata object. Library cache version 5 stores this presentation
projection and invalidates older cache entries, preserving labels after reload.
Playback/seek still restores the recorded rules and never runs the owner adapter.

Actual pointer testing found that transparent space in the wide lobby action
strip intercepted clicks on visible Settings at 640×360. That container now
passes pointer hits through its unused space while its buttons remain interactive.
The center-click, framebuffer bounds, Start and keyboard assertions remain intact.
On short framebuffers the modal uses the full height so the focused Movement row
fits the actual scroll clip; a new assertion checks the entire row, not only its
center or outer framebuffer bounds.

An upstream Android capability CI failure was also repaired without broadening
platform admission. The fixture recognizes authored hidden ancestors; configured
local hosts hide five desktop workspace links they cannot serve. The existing
inline Hunter picker remains allowed. See the [compatibility note](../implementation/android-capability-fixture-2026-10-19.md).

## Validation

Source incorporates main `73d7586bd9b8560bce699f032241dfc320fd6799`, including the
merged custom-weapon dormant payload contracts and replay audio ownership repair.
Host: macOS 27 ARM64, Apple M4 Pro, .NET SDK 10.0.401, Release.

| Check | Result |
| --- | --- |
| Production / GL build | Zero warnings/errors |
| Recorded movement metadata and cache | 54 assertions |
| RmlUi offline editor/persistence and action codecs | 109 assertions |
| Live-menu/service and related contracts | 1,111 passing assertions |
| Legacy Avalonia movement controls and persistence | 19 assertions |
| Playback/viewport contracts | 188 assertions, including inactive label |
| Android source/capability/strict DOM coverage | 728 assertions |
| Native GL lobby, actual 640×360 / 1280×720 / 1920×1080 | 248 assertions at each size; full Movement-row scroll-clip bounds |
| Native GL offline rules, same three sizes | 126 assertions at each size |
| Pinned runtime / published UI | Source fingerprint/library/package verified; 148 authored assets match |
| Architecture / authoritative / replay rules | Pass / 146 / 82 assertions |
| Repository game-asset guard | Pass; no extracted assets published |

The actual existing PR-06 Native and Momentum recordings pass all 2,982 / 2,979
frames, actual world checkpoint continuation, memory and durable seeks, plus
metadata labels throughout playback. They retain their aggregate digests:

- Native: `BAE5662C01088E7195A093652563122EAB1B2F6C1E9169D5410BC54FA87618B6`.
- Momentum: `C866B47544BB8ADB6775B624237EE7D319C4796719A00E24C7A22B9016EA3A5B`.

Numeric replay evidence is in `docs/movement/evidence/pr08-*-replay.json`.
Dense logs and synthetic UI pictures remain ignored under `artifacts/momentum-pr08`.
These are within-environment replay comparisons, not cross-platform bit equality.

The first compressed-metadata test exposed an exception classification error:
`InvalidDataException` needs its own catch alongside `IOException`. The corrupt
construction case remains in the suite. Early GL fixture runs used an outdated
button ID and scrolled away from a disabled control; those fixture mistakes were
corrected without removing the real pointer/bounds/permission checks.

## Reproduction and remaining acceptance

Build `tools/nettest`, `tools/rmlui-live-menu-check`,
`tools/rmlui-offline-rules-check`, `tools/rmlui-playback-check`, and
`tools/momentum-legacy-ui-check` in Release. Run nettest
`--momentum-replay-metadata` for asset-free archive/cache checks. Give the legacy
UI harness an isolated `PROJECT_PRIME_USER_DATA`; it exercises real Avalonia
controls/callbacks and settings I/O against an explicitly staged UI session.
Neither synthetic UI authority fixture claims real networking or physical input.

Build the pinned native RmlUi runtime, verify its fingerprint and published
assets, and run `rmlui-opengl-check` with `--offline-rules` and
`--lobby-interactions` at the desired actual framebuffer sizes. On this Retina
Mac the requested client dimensions are half the actual framebuffer dimensions.

Publication follows the instruction to commit and merge completed slices without
waiting for CI or review. Local results are separate from the publication-time CI
snapshot. PR-09 still owns the final four/eight-player impaired-combat, performance,
platform/input and CI acceptance gates. Momentum remains an experimental opt-in;
[operator rollback](../../SERVER.md#movement-rules) retains Classic without a wire
change. No universal or physical-device completion claim is made here.
