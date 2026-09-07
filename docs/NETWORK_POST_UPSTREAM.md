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

## Reproduction

Use .NET SDK 9 and your own extracted AMHE1 data:

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release -p:MphReadServer=true
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -o /tmp/fruity-nettest
python3 tools/run-network-baseline.py --nettest /tmp/fruity-nettest/nettest.dll \
  --server /tmp/fruity-nettest/FruityPrime.dll --simulation --data /path/to/AMHE1 \
  --seconds 20 --output /tmp/fruity-upgrade-matrix
```

Each matrix output directory must be new. Test runners stop only their own local
processes; no server is publicly listed by these commands.
