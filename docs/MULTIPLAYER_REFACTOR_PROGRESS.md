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

## Planned remaining passes

R6–R9 remove
campaign behavior with content-aware guards. R10–R12 split the projects, converge
Android and enforce dependency boundaries. No later pass is marked complete
before its implementation and checks finish.

The content deletion gate remains conservative: the available audit covers 27
retail and three custom rooms, not the six missing First Hunt data sets. Shared
Door/ForceField/Platform/FH implementations cannot be deleted merely because
they were absent from that subset. In particular, ForceField dynamically uses
the existing Enemy49 lock implementation, and EnemySpawnEntity.cs also contains
FhEnemySpawnEntity; these dependencies must be separated before enemy deletion.
