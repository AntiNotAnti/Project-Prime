# PR-04: native collision and special-state acceptance

The public default remains Native. This slice validates the private offline
Momentum-v1 path against actual native collision, entity events and Hunter
ability processing at 60 Hz. It does not activate a network movement ruleset.

## Scope and ownership

The authored `TEST MOMENTUM COLLISION` course contains original numeric geometry
for walls, corners, shallow steps, a ramp, a ceiling, a narrow corridor and two
slippery surfaces. It obtains textures from locally extracted game assets at
runtime; no extracted geometry, textures, models or game files are included.

Live biped collision remains the native sphere-between-points `CheckCollision`
pass. The three-sphere clearance/recovery helper is not the live biped sweep.
The previously retired `ResolveMovementCollision` pass remains inactive.
Incomplete imported Quake moving-brush collision remains unsupported.

Separate offline fixtures drive real keyboard ability controls, native morph
animations, native damage/teleport/jump-pad endpoints, imported trigger
processing, and a real native moving platform. Fixture initialization and
explicit velocity seeds are identified in the evidence; they are not described
as earned player movement or network hit acceptance.

Directed native `TakeDamage` adds its impulse to `Speed` immediately. It does not
start the native acceleration timer. The test compares that immediate XYZ
impulse exactly before further steering. Normal Momentum horizontal steering
may differ afterward; native vertical processing remains authoritative.

## Test-harness compatibility correction

The pre-existing Quake traversal test called private `ProcessMovement` through
reflection with no arguments. PR-02 added optional C# arguments; reflection does
not supply those defaults. Running the existing fixture reproduced
`TargetParameterCountException`. Its Native kernel call now passes `false,
false` explicitly. This does not add a production overload or change collision.

## Executed checks and evidence

Base: current main `eb4eb71c9`, including merged PR-03, mouse-boost PR #711 and
UI compatibility PR #712. The initial matrix ran on PR-03
`c99931399320b4155957e93ce36f1d0319007009` / main `63a321221` before those merges;
the movement suites were rebuilt and rerun after updating to main. This PR adds
tests, original authored fixture geometry and evidence;
it makes no production movement/collision or replay/protocol changes.

Host: macOS 27 ARM64, Apple M4 Pro; .NET SDK 10.0.401/runtime 10.0.12.
All scene steps are the actual fixed 60 Hz offline CPU path. No network role is
installed to impersonate an eligible local player. The fixture skips the native
intro camera and binds synthetic keyboard input; it does not construct a GL HUD.

```sh
dotnet build tools/nettest/nettest.csproj -c Release --no-restore
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-collision "$ASSET_CONFIG" collision.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-specials "$ASSET_CONFIG" specials.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-abilities "$ASSET_CONFIG" abilities.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-carrier "$ASSET_CONFIG" carrier.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --q3-traversal-scene "$ASSET_CONFIG" "MP3 PROVING GROUND"
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-math
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-profile
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-admission
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol58-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -noupdate -pointercheck
bash tools/check-no-game-assets.sh
bash tools/check-no-game-assets.sh docs/movement/evidence
git diff --check
```

`ASSET_CONFIG` is the local configuration directory for an existing complete
extraction. The four new commands resolve output paths before changing cwd,
isolate the authored catalog from user maps, and use separate generated-map
namespaces. The recipe is copied to development outputs and excluded from
publication by the existing project rule.

| Check | Observed result |
| --- | --- |
| Managed harness build | Passed, zero warnings/errors |
| Collision | 364 fresh traces; 378,667 assertions; zero failures |
| Special events | 66 paired cases/132 fresh scenes; 98,556 assertions |
| Hunter forms and abilities | 14,442 assertions; all seven Hunters, plus actual Spire wall climb/ledge crest |
| Moving native carrier | 1,051 assertions; 191 carried/moving ticks per arm; exact 240-tick paired trace |
| Existing Quake traversal | 2,660 assertions; all seven Hunters in biped and native stable alt form |
| Native goldens | All 56 trajectories unchanged against pinned `dc7f097` fixtures; tolerance `1e-5` |
| Pure math/profile/admission | 104 / 20,046 / 21 checks passed; profile zero allocations over 100,000 warmed steps |
| Protocol-58 archival fixtures | 142 checks passed |
| Architecture invariants | Passed |
| Merged pointer/boost checks | 1,790 pointer assertions; native touch and touch sampling checks passed |

Collision covers wall impact/sliding, corner containment, shallow step, both ramp
directions, ledge falling/landing, shallow initial overlap, roof interception,
narrow corridor, reversal, and actual slipperiness 1/3 contact. Seven Hunters run
Native/Momentum with earned input and explicitly labeled initial speed seeds.
All 28 ledge and 28 ceiling cases activate their intended event. All 28 slippery
Momentum traces bypass the solver and resume after leaving the surface.
Maximum observed displacement is `0.28254488` world units per tick; every trace
has zero contact overflows and recovery fallbacks.

The wall stress earns a real native jump, then seeds planar speed at the private
v1 cap. First impact is neutral for both modes, avoiding Native's later ordinary
input impulse obscuring the collision's normal projection. Fourteen Momentum
impacts deliver `0.35998532..0.35998535` native speed (approximately `10.79956`
world units/second) to the collision pass, with zero wall gap and normal velocity
afterward. Ordinary held-input impacts remain separate cases. Roof contact is
proved by an upward trajectory that would cross the underside, native velocity
projection, and actual nonpenetration. Native's fourfold downward ceiling
correction intentionally leaves a small gap; exact tangency is not required.
Nonpenetration bounds retain their original `0.002` world-unit tolerance.

Specials preserve immediate directed XYZ impulses and paired vertical traces.
They include real entity teleport/pad processing, lethal damage/keyboard respawn,
Prime earned by killing an initialized registry victim, native form cycles,
Trace's timed lunge, and supported Samus/Kanden/Sylux bomb placement/explosion.
Samus actually receives a bomb-jump impulse. Imported Quake trigger metadata is
attached to a native room, then the native traversal cache is invalidated as
fixture registration; normal scene processing must earn each trigger event.
This is not full imported-map or moving-brush acceptance. Across Momentum arms,
5,058 ticks suspend the solver and 6,996 resume it; 161 neutral-air samples retain
actual incoming overspeed. The deliberately large directed endpoint impulse is
stress input, not evidence of a physical projectile hit.

The carrier is a serialized native `PlatformEntity` with canonical model
collision and two moving waypoints. Falling contact sets the real standing
`EntityCollision`; the test never writes the carrier pointer. Keyboard walk-off
then proves native ownership release and actual solver resumption.

Committed numeric summaries are in `docs/movement/evidence/pr04-*.json`. Dense
per-frame traces remain in local `artifacts/momentum-pr04/`; the commands
regenerate full JSON. Collision summaries aggregate seven Hunters per case and
preserve extrema and representative contact evidence. No extracted assets or
native model geometry are committed.

## CI and compatibility limits

Publication does not wait for CI or review. The asset-free Momentum matrix builds
the new fixtures on Windows, Linux and macOS; execution with extracted assets
remains the local matrix above. These are not real input-device, render-cadence,
network latency, rewind/hit-registration or whole-scene performance results.

PR-03 and both compatibility changes merged during final validation. This PR is
based directly on their resulting main commit. The two upstream UI fixture
failures discovered in PR-03 CI were fixed by PR #712 (`78911fefe`), validated by
91 offline-rule and 1,200 native GL checks. The merged mouse/stylus Morph Ball
boost fix (#711) changes separate input files; its deterministic pointer/native
touch checks run alongside the movement matrix. Physical pointer/device checks
remain outside this keyboard suite. Full current CI is not claimed green.

## Remaining feature work

PR-05 through PR-09 remain mandatory: match-wide validated configuration and
protocol activation, replay/rewind compatibility, input and bot parity, lobby
editing, and multiplayer/device/performance acceptance. The feature is not
complete, and missing device or multiplayer checks must not be inferred from
these offline CPU fixtures.
