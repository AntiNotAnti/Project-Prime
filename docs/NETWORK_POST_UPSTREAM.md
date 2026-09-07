# Post-upstream authoritative networking upgrades

This work originally started from authoritative checkpoint `ab07a38`, based on
`830e15e`. The repository history was subsequently reorganized into the current
`main` baseline through `6b1f65b`; the earlier hashes below identify the original
validation checkpoints, not commits that must be restored or reapplied.
The reference is upstream [PR #13](https://github.com/liveteklol/Fruity-Prime/pull/13),
merged as `b5b6b1f`. Its relay implementation is reference material; the online
architecture remains one dedicated, single-writer simulation with ordinary clients.

## Scope and invariants

- One live authoritative wire family; protocol 6 replaces the unreleased live
  authoritative protocol 5. Legacy discovery and demo decoding are read-only.
- History and snapshots share the post-simulation state boundary.
- Input identifies the last presented world timeline, subject to server validation.
- Shot timing is resolved once, with 32 history slots and a 15-tick rewind limit.
- Bounded catch-up uses normal projectile physics and immutable historical player
  colliders. Current map geometry remains authoritative throughout.
- Server updating is opt-in, stages outside live files, validates current content
  with the staged binary, and waits for admitted players or owned matches to leave.
- No public deployment, upstream cherry-pick, live player repositioning, or regional
  orchestration is part of this upgrade.

## Validation record

The final integrated results are recorded under **Final verification** below.
The following checkpoint and pass records preserve the evidence gathered during
implementation.

The checkpoint passed all 152 C# tests, 10 Python tooling tests, the server and
nettest builds, and the full 16-case, 20-second gameplay WAN matrix. The matrix
covers 2/4/8 clients at LAN and 50/100/150/250 ms RTT with jitter/loss, plus
20/60/120/200 ms asymmetric clients. These are real authoritative game simulations
with UDP test clients; they do not render a game window.

The checkpoint excludes local cartridge data, unrelated map work and permission
changes. The two accidental Finder metadata files were removed. Game data and
local launcher output are now ignored. The staged source scan found no secrets.

### Pass 1: protocol identity

Live traffic now requires authoritative family 2 and protocol 6. The central
client connection performs read-only status discovery before sending any Join;
GUI, text and directory-host flows use the same compatibility rule. Discovery
parsers validate exact lengths and source endpoints. Operational flags and log
markers use `-authoritative-server`, `[server]` and `AUTHCHECK`.

An unmodified build of upstream `b5b6b1f` was run behind a counting UDP proxy.
`NetStatus`, `NetProbe` and direct `NetClient` all identified its protocol-5 relay
as online and incompatible. The capture contained five status queries and five
replies, zero authoritative Joins, zero legacy Hellos and no other client traffic.
Its status datagram was 130 bytes; the new authoritative status is 131 bytes.

The full C# suite passed 172 tests. A final focused run passed 28 discovery and
server-process tests, including spoofed replies and bounded query bursts. The
desktop and server/nettest builds passed without warnings. All 10 Python tooling
tests passed. Passive protocol-4 and authoritative protocol-5/6 demo tests passed;
relay protocol-5 recordings are rejected rather than misinterpreted.
The real AMHE1 server smoke check also passed: directory identity, status, hostname
join, lifecycle, input and snapshots, with two clients receiving 30 snapshots/sec
and zero queue drops or rejected packets.

### Pass 2: completed-frame history

`ServerSimulation.Step` now records historical colliders beside the snapshot
capture, after simulation and vector repair. Snapshot connection/life identity
labels both captures. The real-content invariant fixture failed on the old
ordering at tick 5 (pre-step facing differed from the completed snapshot), then
passed 597 actor comparisons across 609 ticks with the corrected ordering.
It covers movement, alt form, death, respawn, spectating, rejoining, disconnect
and replacement of the same slot. Every snapshot is serialized and decoded;
historical geometry also equals a fresh capture of the completed live collider.

Nineteen focused lag-compensation, combat and lifecycle tests passed. The
data-enabled dedicated-server smoke script runs the invariant fixture as a
separate process, keeping the engine's global scene state isolated.

```sh
dotnet /tmp/fruity-nettest/nettest.dll --history-boundary /path/to/AMHE1 AMHE1
```

### Pass 3: last presented view time

`InputCommand.ViewServerTick` names the shared remote-world presentation timeline.
Interpolation resolves that timeline once per picture, including startup,
buffer underrun, bounded extrapolation and the held endpoint. Input uses the
last successfully presented picture, captured before polling newer snapshots.
Before the first picture it uses the newest usable applied snapshot; no input
is sent until such a snapshot exists. Match and connection changes invalidate
pending and published presentation state.

Desktop and Android publish through `Scene.OnFramePresented` after successful
buffer swaps. Android cleanup and offscreen previews do not imply presentation;
a missing surface or failed EGL swap cannot publish a new view. Paused map views
also leave the remote-world timestamp unchanged.

The uint field floors fractional presentation time (less than one tick of
quantization). Per-actor life/form discontinuities still use their explicit
identity-safe presentation rules; this timestamp describes the shared continuous
timeline, not sub-tick rollback of every discrete transition. The server computes
`current - ViewServerTick` once, without adding six ticks again. Its measured RTT,
presentation allowance and scheduling margin still bound claims to 15 ticks.

A native rendered smoke attempt using the pre-pass desktop binary aborted before
window setup with `PAL_SEHException`; this does not establish rendered validation
of the new timing. Android has source verification only because its workload is
not installed. Focused tests and headless checks are separate evidence.
All 193 C# tests passed, including 47 focused input/interpolation/timing cases.
Desktop and server/nettest builds passed without warnings, and the real-data
directory/connection/history smoke check passed. The allocation check measured
zero managed allocation across 10,000 prepare/sample/publish/input-capture cycles.

### Pass 4: immutable shot timing

Accepted root beams now resolve server-validated timing at creation. `CombatShot`
carries the processing tick, reported view tick, validated action tick and rewind
distance. Pellets and inherited children retain this value; player collision
queries never re-resolve it from a later command or RTT sample. Bombs and contact
damage retain attribution without being counted as newly timed beam actions.

Diagnostics separate considered and eligible root shots, rewound/clamped shots,
requested/validated rewind samples, and historical collider queries/misses.
Mean rewind includes zero-rewind eligible shots. Future/ambiguous claims count as
zero requested history. Twenty-five focused tests passed, including allocation,
tick wrap, later-command immutability and metric denominators. The real-content
probe verified one action for a three-pellet Judicator shot, no new timing action
for an inherited child or bomb, and one eligible Imperialist action. The probe
temporarily supplied a bomb-pool entry to isolate an existing headless omission;
the separate pool correction below addresses that production defect.

### Headless bomb-pool prerequisite

The existing renderer initialized gameplay bombs as part of its graphical effect
allocation. Headless startup skipped that method and therefore had an empty bomb
pool. The shared 32-bomb allocation now has its own method, called once by each
startup path. The headless path still allocates no graphical effect pools.

The real-content regression failed on the first spawn before this fix. Afterward,
two generations each spawned 32 bombs, rejected a 33rd, respected the normal
86-tick fuse and reused the same 32 objects after expiry. The fixture runs via
`nettest --bomb-pool DATA [VERSION]` and the data-enabled server smoke script.

### Pass 5: bounded ordinary projectile catch-up

Eligible projectiles defer their ordinary scene step until all players have
finished moving. They then run the existing projectile simulation for completed
boundaries T+1 through N, at most 15 steps. The current endpoint uses current
player geometry; earlier steps require immutable history for the target's exact
connection and life. A missing historical identity never falls back to its
replacement's current body. Splash uses the same historical player position and
current map line of sight. Surviving caught-up projectiles use present targets
on subsequent ordinary frames, avoiding a second compensation interval.

A fixed 512-entry queue defers children until the parent collision stack has
unwound. Pool generations invalidate stale entries. Children born during
catch-up receive only the remaining interval; later ordinary children receive
no repeated catch-up. Parent impacts dispatch before a recycled slot becomes its
child, both during catch-up and during subsequent normal simulation.

The actual-content fixture passes bit-exact position, velocity and age checks
for seven ordinary weapons and all three charged Judicator pellets across nine
steps, with a distinct OFF control. It also checks zero rewind, the 15-step cap,
expiry, queue bounds, moving historical targets, wall order, life/connection
replacement, current endpoint hits, one kill, historical splash and splash LOS.
Both immediate and later single-slot child cases verify exactly one synchronous
parent impact and preserved child state. The test observes message dispatch,
because targeted impacts are synchronous and do not remain in the message queue.

The integrated C# suite passed 202 tests. Server/nettest builds passed without
warnings and real-content directory, connection, history and bomb smoke checks
passed. These results precede homing support and the final mixed-combat soak.

### Pass 6: server controls and reproducible comparisons

`-nolagcomp` disables every historical compensation mode;
`-noprojectilecatchup` retains historical traces. Both are immutable server
settings forwarded into each new simulation after map rotation. Logs report the
effective settings and bounded projectile work alongside shot/history metrics.
Actual process smoke checks verify both command-line flags.

The deterministic fixture uses identical encoded input delivery, accepted
commands, root-shot facts and controlled target paths for both OFF and trace-only
baselines. A dedicated match-owned root spread stream removes damage-timing
dependence from later spread seeds while retaining the ordinary global RNG
advance. Three focused seed tests cover unrelated random work, reset and seed
capture at match construction. All 30 paired cases passed across five mechanics
and 0/4/8-tick delivery delays, with deterministic jitter and 3% bundle loss.
The real-spawn weapon matrix passed 61 checks for all 18 multiplayer variants.

The separate real-UDP mixed-combat fixture uses eight peers, normal damage and
respawns, controlled loadouts and infinite ammo. Its preliminary ON/OFF smoke
held eight playing peers for 600 measured ticks at 100 ms RTT, 20 ms jitter and
2% loss with no dropped ticks or queue overflows. It reports actual root-shot
counts because combat outcomes can change the workload; these runs do not imply
identical accepted shots or causal performance comparisons.

### Pass 7: historical homing acquisition and steering

The resolved homing Power Beam, Volt Driver and Missile variants now acquire
players at the validated action boundary and steer against the corresponding
immutable player/turret point at every catch-up step. Selected connection and
life remain bound to the target. Missing or invalid history drops the target
without live fallback or reacquisition. Current world objects and ordinary map
collision retain their existing behavior. Continuous/area attacks and unverified
future homing children stay excluded.

Five actual charge variants matched timely controls bit-for-bit across nine
steps and the following normal frame. All three homing weapons hit a moving
historical player exactly once, without moving its live body. Acquisition,
missing/dead/spectating history, replacement identity, alternate form, turret,
invalid source and zero-rewind cases passed. The 61-case weapon matrix and 26
focused tests passed; six strict homing A/B pairs preserved 11 root shots each
across 0/4/8-tick delays and both baselines. Server/nettest built without warnings.

Final review added an actual retracted-Weavel-turret regression. It first failed
because the turret was absent from current candidate enumeration, then exposed
premature cancellation by a current destruction message. Historical acquisition
now considers the bounded player-owned turret set, and past steering follows
historical presence until the current endpoint. The regression matches the
timely trajectory exactly. The existing current-turret damage guard remains;
historical aiming does not recreate a retired turret health pool.

### Pass 8: staged dedicated-server updates

Updating is opt-in and requires an explicitly named authoritative release fork
and stamped dedicated package. The release ZIP and each executable/library are
checked against bounded family/protocol/RID/version/hash metadata before running
the staged content validator. Download deadlines cover response bodies as well
as headers. Cancellation reaps the owned validator before cleaning its stage;
unconfirmed termination preserves that stage for operator review.

The owner thread closes admission only when all admitted peers or owned/pending
children have left. It rechecks idleness after closure. Ordinary installation
failures roll back; failed rollback retains recovery files and keeps admission
closed. Cleanup failures cannot turn a committed install into an apparent
failure. Unix completes replacement before exit; systemd launches no competing
child. The standalone Windows helper releases its installation lock before
restarting with the exact original arguments.

The integrated full suite passed 286 C# tests and 29 Python tests. Dedicated
server/nettest and desktop builds passed with zero warnings. Actual subprocess
tests cover validator success markers, missing content, pre-execution integrity,
bounded output, cancellation and stalled downloads. Real UDP tests confirm that
loading peers count as occupied and that closed admission prevents a new Join.
Real filesystem tests cover replacement, rollback, cleanup failure and Unix
executable permissions.

The candidate validated raw AMHE1 retail and TEST ARENA/DUST2/PARALLAX Battle
scenarios, plus 260 supported hosting scenarios in about 13 seconds. Directory
validation requires no content. The 3,584-file data/map inventory and six focused
hashes were unchanged. Root also reran directory, retail and Parallax validation
with the integrated binary and observed explicit family-2/protocol-6 success.
Native Windows helper execution and real systemd restart remain platform gates;
local tests do not establish public update or deployment success. See SERVER.md
for operation and the documented per-file, rather than whole-directory, atomicity.

Actual stamped, self-contained, single-file dedicated publishes also passed for
Windows x64, Linux x64 and Linux ARM64, each with zero warnings. The manifest
generator built and reverified all three ZIPs (35.05, 34.86 and 33.30 MB).
Native header checks confirmed the Windows console subsystem and each target
architecture. These were local package checks, not execution of cross-platform
binaries or publication of release assets. Final opt-out checks also cover the
application’s double-dash argument aliases.

## Final verification

The final source through `95b8f45` passed all 286 C# tests (zero skipped) and the
dedicated server/nettest build with zero warnings or errors. The integrated
desktop build also passed with zero warnings, and all 29 Python tooling tests
passed. Shell checks, the final diff check and the source secret scan passed.

The integrated gameplay build passed all 16 real-UDP WAN cases and all 30 strict
deterministic ON/OFF or ON/trace-only pairs. Real-content fixtures also passed
the 61-case weapon policy matrix, completed-frame history, pooled bombs,
projectile collision/catch-up and historical homing checks. The later narrow
turret-lifecycle and updater argument-alias fixes followed the WAN run; they
received focused regression checks, the full C# suite and the final soak below.

Two sequential five-minute mixed-combat soaks used eight UDP clients, configured
100 ms RTT, 20 ms jitter and 2% loss (seed 20260906). Both retained eight playing
peers throughout measurement and passed every server/client health gate.
Loadouts exercised traveling projectiles, historical traces, acquired homing
targets and continuous beams with ordinary damage, deaths and respawns.

| Measured server metric | Compensation ON | Compensation OFF |
| --- | ---: | ---: |
| Simulation ticks | 18,001 | 18,000 |
| Root shots: travel / trace / homing / continuous | 1,066 / 216 / 101 / 22,403 | 1,081 / 225 / 107 / 23,090 |
| Tick p50 / p95 / p99 (ms) | 0.651 / 1.191 / 1.695 | 0.591 / 1.470 / 1.980 |
| Maximum tick (ms) | 13.404 | 7.892 |
| Process CPU seconds | 20.297 | 16.541 |
| Allocated bytes per tick | 63,587 | 64,221 |
| GC collections: generation 0 / 1 / 2 | 138 / 12 / 2 | 140 / 12 / 2 |
| Dropped ticks / transport queue drops / reliable overflows | 0 / 0 / 0 | 0 / 0 / 0 |
| Combat queue drops / catch-up queue drops / pending catch-up | 0 / 0 / 0 | 0 / 0 / 0 |
| Projectiles caught up / total catch-up steps / maximum steps | 1,167 / 4,971 / 13 | 0 / 0 / 0 |

The ON run recovered two scheduler catch-up ticks without dropping simulation
work. Projectile replay stayed below the 15-step bound. Allocation and GC counts
showed no material increase in this workload. ON consumed 3.755 more CPU seconds
over five minutes, averaging 6.77% of one core versus 5.51% OFF. These are observed
process costs: combat outcomes and actual shot counts differ despite identical
configured schedules and impairment seeds. They do not isolate causal overhead;
the separate strict A/B fixture establishes equal accepted inputs and root-shot
facts for behavioral comparisons.

This completes implementation and local headless validation. Rendered desktop
validation remains blocked by the previously observed macOS OpenGL context
failure; Android runtime validation requires its missing workload. Native Windows
updater execution and an actual systemd restart remain platform checks. No public
release, deployment or push was performed.

## Reproduction

Use .NET SDK 9 and your own extracted AMHE1 data:

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release -p:MphReadServer=true
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -o /tmp/fruity-nettest
python3 tools/run-network-baseline.py --nettest /tmp/fruity-nettest/nettest.dll \
  --server /tmp/fruity-nettest/FruityPrime.dll --simulation --data /path/to/AMHE1 \
  --seconds 20 --output /tmp/fruity-upgrade-matrix
python3 tools/run-mixed-combat-soak.py --nettest /tmp/fruity-nettest/nettest.dll \
  --data /path/to/AMHE1 --seconds 300 --modes on off \
  --output /tmp/fruity-mixed-combat-soak
```

Each matrix output directory must be new. Test runners stop only their own local
processes; no server is publicly listed by these commands.
