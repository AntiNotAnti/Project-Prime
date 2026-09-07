# Multiplayer-only refactor implementation

The continuation begins at `f405497`, after the R0/R1 characterization and .NET
10 migration. R2 is included as an explicitly approved prerequisite to R3–R12.
Each pass remains a separate reviewable commit. Existing R1 release limitations
are recorded in [the baseline report](MULTIPLAYER_REFACTOR_BASELINE.md).

## R2 — Supported launch paths

Removed Adventure/save-slot and offline actions from the graphical and text
launchers, the legacy playable console menu, and desktop/Android match startup.
Persisted launch-kind numbers remain stable: Online=1, Host=3, Demo=5; removed
values and unknown values fail validation. Online/host startup requires the
authoritative client session and uses its admitted room and mode, without a
local-player fallback. Demo spectator playback is preserved.

Practice is not retained as a separate launcher mode. Existing hosting still
starts or requests an authoritative server and joins through the network path.
CLI local gameplay options are rejected; model and multiplayer-room inspection
remain non-playable asset tools. Campaign implementation, save data and parsers
remain temporarily present for the later deletion passes.

Validation: 347 C# tests (11 new launch-boundary cases), 34 Python tests, all 12
real-content mode scoring checks, and the data-enabled dedicated-server/content
regression suite passed. The server/nettest build had zero warnings or errors.
Desktop Release built successfully with the existing NU1903 dependency warning.
The Android Release APK published with both default ABIs, target API 36/minimum
24, verified signatures and ZIP alignment. Its seven XML documentation warnings
remain recorded in the build log; no emulator run was repeated for R2. Both asset
guards passed; available-room audit results exactly match R1, including the
explicit missing-content completeness limits.

## R3 — Scene-owned match model

Added multiplayer-only `MatchMode` with explicit legacy-format conversions,
validated immutable `MatchRules`, `MatchPhase`, `MatchRuntime` and per-slot
`PlayerMatchStats`. Each scene owns its runtime; statistics views share the
existing array storage without copying counters. Extra lives retain the legacy
spare-life meaning, float accumulators retain the Survival sentinel, and
Survival's effective radar state remains separate from configured rules.

The temporary `GameState` bridge forwards multiplayer counters, rules and
lifecycle flags to the scene. There is no detached global statistics runtime.
Setup captures configured time limits separately from the remaining clock.
Desktop/Android/headless teardown and failed setup release the binding. This
bridge and its compatibility arrays are R4 migration surfaces; campaign state
and the legacy format selector remain until the deletion passes. R5 still owns
complete server rule/capacity replication and synchronized phase transitions.

Validation: 357 C# tests passed (zero skipped), including new rule/conversion,
storage isolation and aliasing tests. Existing scoring assertions are unchanged;
their fixture now explicitly owns a headless scene and disables save slots.
The server/nettest build passed without warnings; all 12 real-content scoring
cases and the dedicated-server/content regressions passed. Desktop Release and
Android managed-only Release builds passed after the teardown fixes, retaining
the existing dependency/XML warnings. No full Android publish or emulator run
was repeated for this pass.

## R4 — Scene-owned scoring, flow and results

Moved multiplayer scoring and standings into `MatchLogic`, lifecycle and end
presentation into `MatchFlow`, and removed the temporary static match facade
from `GameState`. Callers now pass their owning scene. Headless completion
captures one deeply immutable `MatchResult` before the presentation clock replaces
the remaining match time. Legacy scoring arithmetic and valid wire values are
unchanged. Hosting now receives friendly-fire configuration explicitly.

World decoding rejects session-only/unknown match phases and goals outside the
rule constructor's bounds before assembling or applying state. Repeated unchanged
world updates retain the immutable rules instance instead of allocating copies.
Campaign state remains for R6–R9; R5 still owns synchronized authority and complete
rule replication.

Validation: 370 C# tests and 34 Python tests passed with zero failures/skips.
All 12 real-content scoring modes, the dedicated-server/content suite and real
rotation/late-join/stale-replay checks passed. Server/nettest built without warnings;
desktop Release and Android managed Release built with the previously recorded
dependency/documentation warnings. No native publish or emulator run was repeated.

The frozen R4 artifacts also passed all 16 real UDP impairment cases (74 client
runs, 20 seconds per case). These local socket tests do not prove rendered play
or an external Internet path.

## R5 — Authoritative lifecycle and rules

The server now owns WaitingForPlayers → Countdown → Playing → Ending →
Intermission and rotates from the same tick-based lifecycle. Countdown lasts
three seconds; ending and intermission last three and five seconds. Waiting and
countdown never advance the competitive world. Countdown start resets players,
scores, RNG, combat history and pending inputs, and verifies that initialized
items/objectives remained unchanged. Losing quorum during countdown returns to
waiting. Player life identities remain monotonic across resets. Captured results
prevent terminal joins/disconnects from changing competitive counters.

Protocol 7 is the single deliberate revision. Reliable welcome/rotation packets
carry every immutable rule before room/player initialization. World updates carry
explicit phase deadlines, phase revision and effective radar state. Input bundles
carry the current Playing revision, rejecting delayed pre-countdown commands.
The server assigns teams; live clients freeze local gameplay during pre-match
phases and show the shared countdown. Protocol-5/6 demos use isolated legacy
layouts; protocol-4 passive playback remains supported.

Rotation keeps the existing four fields and adds optional objective seconds as
a fifth field. Defender/Prime Hunter hold-time goals are independent of score
goals. Invalid configuration and durations beyond the tick comparison window
fail before lifecycle mutation. See [SERVER.md](../SERVER.md).

Validation: 409 C# tests and 34 Python tests passed. All 12 real-content scoring
modes, the dedicated-server/content suite and process-based rotation/late-join
checks passed. The real-UDP phase fixture checks lossless rules, team quorum,
reset, stale input rejection, terminal counters and the deadline cycle with
deterministically driven ticks. Additional Bounty/Nodes probes verify unchanged
objective worlds while waiting/counting down. The server/nettest build passed
without warnings; desktop and Android managed Release builds retained only the
previously documented warnings. Rendered client/platform acceptance remains a
separate release gate.

The frozen R5 artifacts passed all 16 UDP impairment cases (74 client runs,
20 seconds per case). The live Imperialist duel passed with three damage events,
one death, two occluded shots and no miss damage; both clients observed the
damage/death events. These are local socket checks, not rendered Internet play.

## R6 — Remove campaign persistence and progression

Removed `StorySave`, save-slot runtime, clean-save restoration, checkpoints,
inventory/progression persistence, campaign encounter state and logbook writes.
Player initialization retains the existing multiplayer health, ammunition and
weapon values. Settings persistence remains unchanged.

Shared entities retain the legacy initial-state sentinel behavior without save
storage. Trigger-state bits now belong to each scene, with explicit bounds
validation. First Hunt and shared force-field machinery remain for the later
content-aware deletion pass. Campaign scan/dialog and transition behavior still
await R7; no campaign-free runtime claim is made for this intermediate pass.

Validation: 413 C# tests and 34 Python tests passed, including initial-state
goldens and independent scene trigger bits. All 12 real-content scoring modes,
the dedicated-server/content suite, and UDP phase/objective checks passed.
Server/nettest built with zero warnings/errors. Desktop and Android managed
Release builds passed with the existing dependency/documentation warnings.
Both asset guards and the scoped whitespace check passed. No native publish
or emulator acceptance run was repeated.

## R7 — Remove player and scene campaign behavior

Removed campaign scan/dialog HUD, pause/escape state, landing/movie playback,
encounter music, projectile effects and item attraction. Runtime mode consumers
now use scene-owned rules. Full admitted rules are installed before entity
construction. Settings, nicknames and reset remain in `GameState`.

Room transition state belongs to the scene. Synchronous live/demo rotation
rebuild remains; asynchronous campaign door/connector loading is removed.
Multiplayer intro/spectator cameras and the offline Vx export codec remain.
Retired input controls keep legacy wire bit positions reserved, and Android
spectator VIEW has its own control. The modern pause menu still suppresses
client input without stopping the authoritative simulation.

Validation: 417 C# tests and 34 Python tests passed, including transition-state
isolation and retired-input compatibility. All 12 real-content scoring modes,
the dedicated-server/content suite, UDP phase/objective checks, and process
rotation/late-join/stale-replay checks passed. Server/nettest compiled with zero
warnings/errors; desktop and Android managed Release builds passed with the
previously recorded warnings. Both asset guards passed. Rendered/native platform
acceptance was not repeated. Enemy runtime and campaign catalog deletion remain
R8 work.

## R8 — Remove campaign runtime entities and catalogs

Deleted the campaign enemy hierarchy, campaign spawner and artifact runtime.
The shared force-field lock is a standalone entity, retaining its original tick
ordering, reflection weapons, movement, damage rules and same-frame death
behavior. Its 64 projectiles are shared within the scene. First Hunt's spawner
placeholder was moved intact; raw entity parsers and memory layouts remain.

The runtime room catalog retains 39 built-in multiplayer records with their
original sparse global IDs. First Hunt's separate local metadata IDs remain
unchanged, and custom IDs still start at 138. Generic model lookup metadata is
retained because shared objects/platforms and export tools depend on it.
External teleporter targets cannot become intra-room destinations when campaign
catalog entries disappear. JSON reports without geometry/imports no longer
register an empty custom room.

AI action 82 (campaign Echo Hall logic) now rejects explicitly without shifting
later action numbers. It is absent from all reachable multiplayer personality
trees in the supplied AMHE1 data (SHA-256
`22a6b88091a754123720fbdb83336023d19765a7aa6f004d654a6057c62b81b3`).

Validation: 433 C# tests passed. The shared-lock fixture passed all nine beam
mappings and focused beam/bomb/alt/same-frame checks using synthetic map records
with real AMHE1 assets; this is not proof of unavailable First Hunt maps.
All 12 scoring modes, UDP phase/objective checks and the server/content suite
passed. The 42-room audit preserves every baseline room ID and entity-file hash;
its original six missing First Hunt roots and six retail null entity paths remain
explicit completeness limits. Server/nettest compiled without warnings, and final
desktop/Android managed builds passed with the known warnings. Asset guards and
scoped diff checks passed. No rendered/native acceptance run was repeated.

## R9 — Multiplayer-only source guard

`python3 tools/check-multiplayer-only.py` checks every C# source below `src`,
including future project folders. It rejects retired campaign runtime symbols
and deleted player scan/dialog files. Exceptions are exact files for raw format
identities, export codecs and negative tests, scoped independently to each rule.
Comments and plain literals are excluded; executable interpolation remains
checked. Invalid roots, unreadable files and source symlinks fail explicitly.

Validation: the current 402 C# files pass with zero violations. Seven dedicated
guard tests cover detection, narrow exceptions, interpolation, deterministic
reports and future project paths; all 41 Python tests pass. CI integration and
allowlist path migration are part of R12's final project/build cleanup.

## Planned remaining passes

R10–R12 split the projects, converge
Android and enforce dependency boundaries. No later pass is marked complete
before its implementation and checks finish.

The content deletion gate remains conservative: the available audit covers 27
retail and three custom rooms, not the six missing First Hunt data sets. Shared
Door/ForceField/Platform/FH implementations cannot be deleted merely because
they were absent from that subset. R8 extracted the force-field lock and First
Hunt spawner placeholder before deleting the campaign enemy hierarchy.
