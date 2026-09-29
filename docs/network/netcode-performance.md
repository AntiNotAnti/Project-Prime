# Netcode performance implementation

Adapted to the existing c9ddc8f8 working tree, including its uncommitted replay,
Enhanced Hunters and map changes. The supplied plan's protocol 24 → 25 stage is
**29 → 30** here. Both peers must upgrade. Changes remain in the working tree.

## Implemented behavior

- Deterministic shadow sampling: off, production 1/16 (default), study 1/4, full.
  Cheap shot/rewind records remain unconditional. Sampling affects diagnostics,
  not authoritative collision or damage.
- Pending/resolved/rescued ledger counts are maintained on transitions. A slow
  reference validator is available to tests. Rescues use fixed open-addressed
  storage, lifecycle-fenced keys, bounded pair capacity and backward-shift deletion.
- Geometry histories track exact revisions and state equality, skip redundant
  interpolation/inversion/application/restoration, and store door/force-field
  discrete state in bitsets. Transform-driven objects retain full history.
  Live capture remains the safety fallback: this engine has no mutation stamp
  proving that nothing changed since history recording. No speculative capture
  elision was introduced. Room-local trace IDs come from the inventory and the
  winning trace returns its own ID.
- Shot identity is match/epoch/shooter/generation/life/ShotId. Original source
  frame, ACK/subframe, weapon, charge, event kind and continuous phase travel in
  a repeated 16-event, 32-frame history. Claims, combat acknowledgements,
  projectile children, rescue lookups and telemetry carry identity. ACK and
  launch clocks are timing only. Recovered events use their original ACK before
  rewind validation. Live ingress rejects zero-ID beam claims.
- A shot-scoped fixed cache reuses historical player poses, bodies and turret
  positions. Tokens and lifecycle/frame keys prevent cross-shot reuse.
- Server telemetry batches 60 steps into at most five events. Exact counts,
  totals and extrema accompany an approximate quarter-millisecond histogram
  (overflow at 7.75 ms). Shutdown, match end, stop and exception flush partial
  batches. Repetitive continuous samples are counted; transitions and individual
  combat events remain correlated.
- Slow slots are indexed once; live fast packets decode directly into player
  state after complete validation. Bootstrap/offline/replay paths retain owned
  canonical state. Historical replay intents upgrade at the playback boundary;
  generated replay checkpoint accessors include the new value fields.
- The transport lock was instrumented and stressed, with no lock decomposition.
  The measured contention did not justify that conditional phase.

The fixed intent grows from 102 to 423 payload bytes. The retained fire history
costs 321 bytes per intent (about 9.6 KB/s at 30 intents/s, before transport
overhead). This is an explicit bandwidth tradeoff for loss recovery; no claim
of lower total network bandwidth is made.

## Measurements

Same Apple M4 Pro, macOS Arm64, .NET 10.0.12, Debug build. Each server case uses
300 warmup steps and 1,800 measured steps in MP1 SANCTORUS. The saved pre-change
ProjectPrime assembly and changed assembly ran the same new benchmark driver.
“Before” includes the user's existing working-tree changes, not a clean release.
Counters in server JSON include warmup; timing and allocations exclude it.

| Players | Mean ms before → after | p95 ms before → after | p99 ms before → after | Worst ms before → after |
|---|---:|---:|---:|---:|
| 2 | 0.0732 → 0.0733 | 0.1162 → 0.1087 | 0.1959 → 0.1521 | 8.6309 → 8.4164 |
| 4 | 0.1208 → 0.1172 | 0.2815 → 0.2124 | 0.4415 → 0.2915 | 8.4254 → 8.6278 |
| 8 | 0.2499 → 0.2370 | 0.7947 → 0.5617 | 1.1402 → 0.8033 | 8.4990 → 9.3200 |

Allocations were unchanged at 1,068/1,191/1,439 bytes per step for 2/4/8 players.
All cases had zero simulation failures, overruns, dropped steps and stalls.
Eight-player sampling selected 59/840 shots; historical cache hits were
8,066/59,223 queries. This room has no dynamic history objects, so these server
timings cannot establish a geometry CPU gain. Asset-backed UNIT1_RM1 checks
exercise doors, force fields and collision meshes separately.

The p95/p99 results are promising; worst frames and low-player mean results are
mixed. These short synthetic runs do not establish greater deployed
matches-per-server capacity, Android performance, or worst-frame improvement.
They drive actual simulation with synthetic intents but do not run real UDP.
The impairment matrix tests codecs/queues separately and is not a measurement
of impaired asset-backed combat.

The direct decoder eliminates 380/670/1,250 canonical bytes copied per 2/4/8-player
snapshot and both paths allocate zero bytes after warmup. See the isolated
[decoder measurements](performance/replication-decode.json) for CPU timings.
Both paths produce the same player array and consume its health values.
The eight-player result was 2.30 μs canonical versus 2.47 μs direct in this
Debug run (about 7.5% slower). No Android hardware was available; reduced
copying alone is not a proven CPU improvement.

The warmed eight-peer UDP stress run reported 8,924 lock acquisitions, 20
contentions (0.224%), 1.309 ms total wait, 0.505 ms maximum wait, 34.607 ms total
hold and 0.552 ms maximum hold. Mean hold was about 3.88 μs. An earlier run also
had contention below 1%. Occasional maxima warrant production observation,
but these runs do not demonstrate sustained contention or correlation with
simulation spikes. Keep the shared lock pending stronger evidence.

## Validation and evidence

The final targeted run passed 22 suites: architecture, lifecycle, health/shot,
claim stress, input edges, protocol 17/18/19, continuous targets, alt hits,
shadow policy, weapon policy, allocations, reliability, queue budget, new
performance invariants, mixed-quality UDP transport, and five asset-backed
combat/claim/continuous/geometry suites. Combat checks include 116 assertions:
recovered-fire duplicate damage, distinct same-clock IDs, retained charged
release and repeat events, child identity and posthumous projectile behavior.
Randomized historical cache checks compare uncached output across ring wrap.
New tests also cover sampling fractions, maintained counters against scans,
20,000 rescue operations against a reference map, exact geometry shortcuts,
direct/canonical byte equivalence and allocation-free telemetry batching.

Replay format and replay timeline checks passed. The pre-existing
load-lifecycle suite is blocked by the missing PRIME AIM LAB immutable.ppmap
bundle; the saved pre-change binary fails the same way. That unrelated map
problem was not changed.

Evidence:

- [Validation excerpts](performance/validation.txt)
- [Server before, 2](performance/server-before-2.json), [4](performance/server-before-4.json), [8](performance/server-before-8.json)
- [Server after, 2](performance/server-after-2.json), [4](performance/server-after-4.json), [8](performance/server-after-8.json)
- [Virtual network baseline](performance/network-before.json) and [after](performance/network-after.json)
- [Complete 2,016-scenario impairment matrix, gzip JSON](performance/network-extended.json.gz)
- [UDP stress and lock measurements](performance/transport-stress.txt)
- [Decoder comparison](performance/replication-decode.json)

The matrix spans 2/4/8 players; RTT 0/50/100/150/250/320/400 ms; jitter
0/20/40/80 ms; loss 0/1/2/5%; reorder 0/1/3%; duplication 0/1%.
The standard matrix retains eight-player clean and 320 ms / 80 ms / 2% loss
cases. New deterministic checks join normal networking CI; expensive
benchmarks remain explicit commands documented in SERVER.md.
