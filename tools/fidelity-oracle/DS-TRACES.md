# CPP-07 original-game trace contract

Status on 2026-10-18: **comparison protocol implemented; original accuracy
NOT_VERIFIED**. Two private, repeatable original-ROM strafe captures now exist;
their matching Project setup/producer remains unqualified. No gameplay, Balanced
Mode, network, replay, saved-game or existing baseline changes were made.

This is a comparison extension to the existing fidelity oracle. It reads the
production `FidelityResult` format and validates its existing canonical hash. It
does not add another simulation harness, importer, recorder or source of truth.
The standard-library Python entry point keeps protocol verification independent
of the full app build and of locally extracted game content.

## Audited source and existing evidence

`git ls-remote https://github.com/BountyHunterKanden/SecondHunt.git HEAD` on
2026-10-17 returned `c16a907b3658c2fd4079e394a73711596a85caee`. The adaptation
uses these pinned primary sources:

- [Emulator workflow](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/README.md).
- [Post-frame CSV driver and fixed-point formatting](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/driver.lua).
- [Jump/button route](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/routes/probe_buttons.lua),
  [strafe route](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/routes/probe_strafe.lua),
  [roll route](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/routes/probe_roll.lua),
  and [slope route](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/tools/accuracy/routes/probe_slope.lua).
- [Second Hunt comparison implementation](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/src/MphRead.Tools/AccuracyTest.cs).

The driver records every DS display frame after advancing the emulator, with a
route-relative `f` column and a separate emulator frame count. Game logic updates
at 30 Hz. The Project engine advances at 60 Hz. DS `fx` fields are signed fixed
point with denominator 4096, emitted as six decimal places by the upstream
driver. This extension converts those decimals to the existing Project normalized
integers using midpoint-away-from-zero rounding.

The 47 existing baseline files are Project regression references with revision
`e80098f4`; their hashes remain unchanged. They do not become DS references by
adding a metadata file. In particular, the existing Proving Ground setup differs
from Second Hunt's landing-site setup. A real comparison requires the same room,
initial state, and input timeline on both sides.

## Private original-ROM capture and landing preparation, 2026-10-18

The [two-run scalar receipt](../../artifacts/cpp07-local-ds-source-audit-20261018/mac-original-strafe-two-run-receipt.json)
records actual software-only, windowless melonDS DS 1.4.0 execution of the
user-supplied AMHE1 ROM. Both fresh processes execute the unchanged landing
route and all 240 raw strafe-route frames. Their complete integer/input streams,
initial native state hashes and actor guards match exactly. Every observed
tilt/position/velocity/camera change occurs at an even probe-frame endpoint.
That scoped observation does not establish a global game-tick address or every
subsystem's update rate. The actual display rate is `33513982/560190` Hz;
60/30 are nominal index labels, and no samples were resampled. ROMs, native
states, framebuffers and raw captures remain private.

The supplied SecondHunt checkout has no `.git`. Its sixteen-file workflow bundle
and individual scripts are hashed in the receipt; the earlier audited remote
HEAD above must not be assigned to this snapshot. Format 1's Git-revision and
nominal-rate metadata need an explicit provenance review before admitting this
capture to a comparison. No accepted comparison manifest has been fabricated.

The [private Project preparation receipt](../../artifacts/cpp07-local-ds-source-audit-20261018/project-unit2-land-private-preparation-receipt.json)
records a direct-reference build against frozen RML66 with zero warnings/errors,
993 closed checks through the real input consumer and scalar sampler, and 29
source/hash checks. Right remains held at raw indices 20–59 and Up at 120–159,
with exact consumed edges and neutral unrelated actions. The real
`SetFreeCamera(false)` selects Player/All; the fixture separately reproduces
CameraOnly suppression. These checks never construct a Scene or run a world.

A read-only inventory of native AMHE1 assets confirms Project room 45
`UNIT2_LAND`, story-layer SamusShip entity 5/model 23, and its collision attachment
node. Its landed animation 2 has 90 frames; sixty isolated authored node frames
produce sixty distinct attachment matrices. These decoded-model observations
do not identify a live landing phase. Headless simulation advances animation
frames but lacks the normal renderer's complete node-collection schedule,
whose attachment matrices the next platform collision step consumes. Calling
only the ship's draw method would introduce an unreviewed evaluation schedule.

The private preparation therefore refuses capture before constructing a Scene.
It is unregistered, emits no Project trace, and leaves the 47-case catalog and
all baseline files unchanged. A matching live landing/collision-node phase,
independent original room/tick/input-latch proof and reviewed provenance remain
required. No input shift, coordinate placement, fitted warmup or numeric
accuracy claim is justified by these preparation checks.

A subsequent private normal-renderer witness uses frozen RML71, actual main-thread
toolkit initialization and the ordinary renderer loop. Its first completed step
preserves the strict gate: every renderer-state invariant passes, but OpenGL
reports `InvalidOperation`. No probe rows or matching Project trace were accepted.
The [diagnostic receipt](../../artifacts/cpp07-local-ds-source-audit-20261018/project-rml71-native-landing-attempt03-gl-diagnostic-receipt.json)
includes true exit 1 and the separate terminal observation after cleanup and
window disposal. The [managed IL proof](../../artifacts/cpp07-local-ds-source-audit-20261018/project-rml71-movie-uniform-type-proof.json)
confirms that the movie's integer `Uniform1` call reaches the integer OpenGL
overload for a shader-declared float. The proposed `1f` correction still requires
a central build and the unchanged runtime gate; no failure is reclassified as a
setup or accuracy PASS.

The [all-13 source feasibility matrix](../../artifacts/cpp07-local-ds-source-audit-20261018/all13-source-capture-feasibility.json)
records exact route hashes, effective input intervals and remaining observable
owners. Existing no-poke routes support further jump, morph-camera, roll and
slope capture plans. Their presence is not numeric evidence. Routes that write
RAM remain excluded, and missing accepted-shot, boost-damage, boss, cue and
pickup-event semantics are explicit gaps.

## Running

Keep local captures, routes, savestates, ROMs, emulator output and private reports
outside the repository. Only derived numeric CSV is consumed; do not supply a
ROM, RAM dump, screenshot, audio file or extracted asset as a trace. This tool
does not acquire or distribute game content.

```sh
python3 tools/fidelity-oracle/compare_ds.py status
python3 tools/fidelity-oracle/compare_ds.py compare /local/private/comparison.json \
  --ds-csv /local/private/trace.csv --project-json /local/private/project.json \
  > /local/private/comparison-report.json
```

Use the existing actual-engine entry point to obtain Project JSON for a matching
supported fixture, with `-allow-content` and a full source revision. Do not relabel
an unrelated fixture or edit its normalized values to match the DS:

```sh
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -fidelityoracle run movement.jump \
  -allow-content -engine-revision FULL_40_CHARACTER_COMMIT > /local/private/project.json
```

The current F2 producer is only usable when its actual setup matches the chosen
DS route. Producers listed as gaps below need instrumented, reviewable actual
engine captures before comparison. The tool does not generate missing fields.

Exit codes: `0` means the declared comparison window matches its tolerances,
`1` means numerical divergence, and `2` means invalid or unavailable evidence
(`NOT_COMPARABLE`). Results are JSON on stdout. There is no baseline write path.

## Manifest format 1

All properties below are required and unknown properties fail closed. JSON
duplicate properties are rejected. Hashes must be full lowercase SHA-256 unless
specified otherwise; abbreviations and all-zero placeholders fail. Input files
are bounded to 8 MiB each. Supply actual reviewed values, not the explanatory
placeholders in this sketch:

```json
{
  "format": 1,
  "case": "movement.jump",
  "evidenceKind": "original-game-trace",
  "reference": {
    "sha256": "<hash of exact DS CSV bytes>",
    "captureKind": "emulator",
    "game": "AMHE1",
    "romSha1": "a8679d4f8dcb445cbe946a424241ef44901b0caf",
    "logicHz": 30,
    "sampleHz": 60,
    "samplePhase": "post-step",
    "logicUpdateParity": 0,
    "tool": {"name": "BizHawk", "version": "<actual version>", "core": "<actual core/version>"},
    "workflowRevision": "c16a907b3658c2fd4079e394a73711596a85caee",
    "routeSha256": "<hash of actual capture script>",
    "initialStateSha256": "<hash of local initial state artifact>",
    "room": "<exact room identifier>",
    "setupSha256": "<shared reviewed setup specification hash>",
    "inputsSha256": "<shared canonical timed input timeline hash>"
  },
  "project": {
    "sha256": "<hash of exact FidelityResult JSON bytes>",
    "sourceRevision": "<full 40-character Project commit>",
    "sourceStateSha256": "<hash of reviewed source snapshot or source-state manifest>",
    "binarySha256": "<hash of actual executing binary>",
    "simulationHz": 60,
    "balancedMode": false,
    "protocolVersion": 53,
    "replayFormatVersion": 1,
    "replayFingerprint": "<actual paired replay fingerprint>",
    "collisionSha256": "<actual Project room collision identity>",
    "room": "<same room identifier>",
    "setupSha256": "<same reviewed setup specification hash>",
    "inputsSha256": "<same canonical timed input timeline hash>"
  },
  "alignment": {
    "dsFrameColumn": "f",
    "firstDsFrame": 330,
    "firstProjectTick": 2,
    "logicTicks": 60
  },
  "fields": [
    {"dsColumn": "py", "projectField": "position4096.y", "encoding": "fixed4096", "projectSampling": "endpoint", "absoluteTolerance": 1, "rationale": "<predeclared numerical reason for this tolerance>"},
    {"dsColumn": "vy", "projectField": "velocity4096.y", "encoding": "fixed4096", "projectSampling": "endpoint", "absoluteTolerance": 1, "rationale": "<predeclared numerical reason for this tolerance>"}
  ]
}
```

The protocol/replay versions, frame offsets, tick count and tolerances in this
sketch are explanatory values, not measurements or approved acceptance thresholds.
Read actual protocol/replay identities from the tested build. Source-state hashes
must account for uncommitted source changes; a Git HEAD alone cannot identify a
dirty build. Preserve the source-state and setup specifications locally for review.

`setupSha256` identifies a shared specification containing room, starting position,
orientation, hunter/form, health/ammo/equipment, warmup, RNG, moving-world phase,
game rules and relevant campaign/save flags. `inputsSha256` identifies the shared
timed input sequence in physical time. DS savestate and Project save serialization
need not match byte for byte; their declared comparable starting conditions must.
Room, setup and input identities must agree between captures. Artifact hashes bind
the metadata to exact numeric files; they cannot independently prove that a user
declared the right ROM, binary, route, setup or replay. Review those witnesses.

## Timing, units and event semantics

For zero-based comparison index `i`, the mapping is exactly:

```text
DS display frame = firstDsFrame + 2*i
Project simulation tick = firstProjectTick + 2*i
original logic tick in report = i + 1
```

`firstDsFrame` is the post-logic endpoint, matching the measured route-relative
`logicUpdateParity`. Do not substitute the CSV's absolute emulator frame count.
At least two logic ticks must be declared. The DS CSV must retain every display
frame, including the frame preceding the first endpoint. Gaps, duplicates,
out-of-order frames and prefiltered 30 Hz CSVs fail. The tool performs no automatic
phase search, time stretch, spatial offset, smoothing, resampling or velocity
scaling. Input holds on the Project side span the same physical interval, using
two 60 Hz frames for each original 30 Hz tick.

`fixed4096` converts DS decimal world values to normalized integers; an absolute
tolerance of one means `1/4096` in the observable's declared unit. Position and
velocity are distinct observables; do not infer a velocity conversion solely
from the different update rates. Confirm the producer's units. Integer fields
require zero tolerance. NaN, infinity, fractional integer values, overflow,
duplicate field mappings, missing observables and missing mapped checkpoints fail.

`endpoint` compares the state after the second Project simulation frame. Odd
Project frame states are not averaged into the comparison. `sum-pair` is reserved
for nonnegative **per-frame event deltas**: it adds the Project values at ticks
`n-1` and `n`, then compares the original tick's event count. `shotsFired`,
`bossTransitionCount`, `soundCueCount` and `pickupCount` require this mode and
integer encoding. A cumulative lifetime counter must first be exported as
per-frame deltas. A current projectile population cannot stand in for shot count.
Captures containing only even Project ticks cannot verify pair event counts.
Sound count and endpoint cue ID do not prove the order of multiple within-tick
cues; inspect an ordered event/audio trace for that separate claim.

The JSON report contains capture, source, binary, collision, setup, input,
protocol and replay identities, exact mapped frame range, per-field maximum and
mean absolute error, divergence count, and first difference with DS/Project frame
numbers. `ORIGINAL_TRACE_MATCH` means only that the declared numeric window
matched; `originalAccuracy` remains `COMPARED_DECLARED_CAPTURE`, not release or
physical-device acceptance. `SYNTHETIC_PROTOCOL_MATCH` always carries
`originalAccuracy: NOT_VERIFIED` and requires `--allow-synthetic`.

## Capture coverage and remaining dependencies

Every case remains **NOT_VERIFIED** until a local original trace and matching
actual-engine producer are reviewed. The catalog's required observables prevent
claiming a case using unrelated values. Current Project regression scenarios
remain useful for regression detection within their own declared setup.

| Case | Existing Project regression | Remaining capture work |
| --- | --- | --- |
| Jump/gravity | `movement.jump` | Same DS/Project starting room and complete jump route. |
| Air friction | `movement.air-control` | Dedicated airborne release/coast route; existing input holds forward. |
| Walk friction | `movement.walk` | Dedicated release/coast route. |
| Strafe/walk tilt | None | Actual tilt observables and matched input route. |
| Morph roll | `alt.samus` | Flat-ground acceleration, cap and coasting route. |
| Morph boost | `alt.samus` | Original boost trace with matching charge/release route. |
| Camera follow | None | Actual camera position plus player position. |
| Slopes | `movement.slope` | Same incline geometry, world phase and route. |
| Corner collision | `movement.collision-corner` | Same corner geometry and sustained-input route. |
| Weapon cadence | Capture-only `weapon.powerbeam.cadence`, plus existing weapon packs | Power Beam F2 producer validated on Mac at four rates; matching original counter trace remains absent. Other weapons still need dedicated producers. |
| Boss transitions | None | Original/actual-engine boss state and transition event deltas. |
| Sound cues | None | Original/actual-engine cue IDs and event deltas; ordered cue audit separately. |
| Scripted pickups | None | Original/actual-engine pickup deltas, health and ammo changes. |

`python3 tools/fidelity-oracle/compare_ds.py status` exposes the same 13 cases and
capture gaps as JSON. It describes the committed pack; it does not scan private
directories or promote locally generated reports into accepted evidence.

## Capture-only Power Beam cadence producer

`weapon.powerbeam.cadence` uses the existing F2 scene, input, weapon and capture
loop. It adds no second engine harness, gameplay counter, event subscription,
network pump or presentation owner. It is available only by explicit `run`:

```sh
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -fidelityoracle run \
  weapon.powerbeam.cadence -allow-content -presentation-hz 60 \
  -engine-revision FULL_40_CHARACTER_COMMIT > /local/private/powerbeam-60.json
```

Use the same frozen build and isolated configuration at other rates to check that
presentation scheduling preserves the entire canonical capture. `all` retains
exactly the existing 47 regression scenarios. Explicit
`verify` or `record` for the cadence case exits nonzero before capture or baseline
IO. No new baseline is supplied or permitted through this diagnostic route.

Actual Mac F2 execution on 2026-10-17 used the immutable final58 client DLL SHA-256
`b64c20891de93a6db4e9bbd0ed0b045c96029796ef030c1cdc3e02d4e1961aa3`
and dirty-source manifest SHA-256
`436f3ffe9f56876117e1383d1a30ef7cc8aacb9098bf902ae383eedc138c9687`
(Git HEAD `dc7f097067d755ec76d145fc7cc333068566863d` alone does not identify that
build). The asset identity was
`2ba4db35b462591dfdf02f51cd1bfe741b7928728a7baae37f9994bd94511603`,
3858 files / 840039391 bytes. Four fresh processes at 60, 120, 144 and 240 Hz all
exited zero and passed strict producer-witness validation with 181 checkpoints,
12 accepted shots, projectile population sum 420, and 168 frames containing live
projectiles but no new shot. All four canonical hashes equal
`8f7c5568945822c4905346447ea227855d6b00e0bf3b3eeb2952278d4b3cfa0c`.
This is **F2 capture-producer evidence**, not original DS accuracy or rendered,
network, device or release acceptance. No GL context was requested; the existing
headless path runs CPU simulation. Each process received a fresh user-data/cwd
directory with only normalized `paths.txt` referencing existing read-only assets;
the engine generated its ordinary map catalog cache there. No settings, saves or
credentials were copied. The receipt is
[`artifacts/cpp07-cadence-f2-20261017/receipt.json`](../../artifacts/cpp07-cadence-f2-20261017/receipt.json);
derived raw captures and diagnostic logs remain outside the repository.

The fixture uses AMHE1 Proving Ground, offline Battle, two Samus players, slot 1
as actor, seed 123456, 120 warmup simulation ticks and Power Beam. It refuses
Balanced Mode. The route then runs 180 simulation frames: frames 1–12 hold fire,
13–30 release, and that 30-frame pattern repeats six times. Each original 30 Hz
logic tick maps to two Project frames with identical held input. Press/release
edges remain captured separately. The initial checkpoint at tick 0 records the
actual room ID, slot, hunter, weapon, position, velocity, facing, health, charge,
ammo, RNG and accepted-shot counter after warmup and arming. Review those actual
values when preparing the same DS starting conditions; a native spawn recipe
alone does not establish matched initial state.

Each subsequent checkpoint records the actual pre-step shoot down/pressed/released
flags, canonical shot counter before and after the step, and `shotsFired` equal to
their difference. The source is `scene.GameState.ShotsFired[1]`, which
[`MatchReportStats.AcceptSemantic`](../../src/MphRead/Mods/Network/MatchReportStats.cs)
increments for accepted canonical `WeaponFired` events, excluding alternate-form
and turret events. The production firing path emits that fact after successful
spawn in [`PlayerInput`](../../src/MphRead/Entities/Players/PlayerInput.cs).
The diagnostic only reads this owner. `FidelityEventDelta` receives scalar
observations and has no scene, player, RNG, replay or event-bus access. It refuses
missing/duplicate frames, observations changed between frames, counter rewind,
negative/wrapped values and integer saturation. Its rejected observations do not
advance the sampler. The scene producer also requires a positive accepted-shot
total, exact sum of per-frame deltas, and a live projectile on a frame with zero
new shots, distinguishing event cadence from projectile population.

The capture and its canonical hash include `capture.simulationHz=60`,
`capture.sampleHz=60`, initial `capture.postStep=0`, then per-frame
`capture.postStep=1`, `capture.balancedMode=0`, `capture.diagnosticOnly=1` and
`capture.originalTraceVerified=0`. The comparator validates all these witnesses
and the complete route before accepting this producer's data. These flags state
the producer's scope; a future reviewed DS comparison still has to supply the
independent original capture and manifest.

For a full three-second comparison use Project endpoints 2, 4, …, 180
(`firstProjectTick=2`, `logicTicks=90`) and the measured first DS post-logic
endpoint/parity. Compare `weapon` as an integer endpoint and `shotsFired` as an
integer `sum-pair`, with zero tolerance. A different window must still include
both Project frames for every event pair. Do not select a phase or tolerance
after observing the error.

Source/build/setup provenance follows the existing manifest contract above:
`engineRevision` must equal the full reviewed `sourceRevision`; retain the exact
dirty-source-state manifest, executing binary SHA-256, collision identity, replay
format/fingerprint, setup specification and input timeline hashes separately.
The capture cannot infer its own binary/source identity from Git HEAD, and no
placeholder metadata is generated. `setupSha256` must bind the reviewed numeric
tick-0 conditions and fixture recipe; `inputsSha256` must bind the paired physical
timeline and captured input edges. Keep local captures and reports private.

A bounded read-only availability check on 2026-10-17 found no supported DS
emulator executable on PATH or installed melonDS/DeSmuME/RetroArch/BizHawk app in
the standard application locations. No supplied original numeric trace was
available. No ROM/content search, download or capture was performed. Required
external evidence remains an independently obtained original AMHE1 trace with
reviewed route, emulator/core version, measured update parity and matching room,
initial state and inputs. That missing evidence keeps CPP-07 original accuracy
**NOT_VERIFIED**. That dated availability result is superseded by the private
original-ROM capture described above; it remains part of the historical record.

## Verification and adjustment gate

```sh
python3 -m unittest discover -s tools/fidelity-oracle -p 'test_*.py' -v
```

These 43 F0 checks generate synthetic values in memory or temporary directories.
One test compiles the actual scalar delta helper, catalog and CLI sources against
closed facades in a temporary .NET project (28 checks); it never builds the main
project or executes a world. Seven new synthetic producer-witness checks verify
the complete route and counter evidence. The existing protocol checks verify
mapping, pair event counts, tolerance boundaries,
artifact/canonical hashes, metadata rejection, malformed inputs, CLI exit codes
and compatibility with all 47 committed baseline hashes. They never label those
fixtures as measured DS data or regenerate a baseline.

No runtime adjustment is justified by this change. A subsequent adjustment needs
reviewable original-versus-current **before** and **after** traces from the same
declared setup, predeclared tolerances, and a gameplay/network side-effect audit.
Re-run the existing deterministic oracle without baseline regeneration, and
separately validate 1v1, eight-player, bots, server shadow, saved games and old
replay hashes. Consider replay versioning explicitly if canonical simulation
changes. Keep Balanced Mode intentional behavior separate. Those runtime and
device acceptance gates remain open; protocol tests cannot satisfy them.
