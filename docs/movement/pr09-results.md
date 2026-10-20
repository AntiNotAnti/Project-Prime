# PR-09 validation — draft, acceptance incomplete

**DRAFT / NOT RELEASE ACCEPTED.** Functional evidence is substantial, but the
performance gate fails and the platform/network/combat matrix remains incomplete.
This document does not declare Momentum complete or suitable for default rollout.
Classic remains the default and rollback path; Momentum is an explicit opt-in.

The principal earlier campaign uses main `f908c7e534bce67e07a3776b21a658790034544d`
plus the uncommitted PR-09 validation changes. Focused current-source checks now
use actual main `3de8887a8b04bd85db8b60ea14da9add15c1972a` plus the PR-09 changes,
including the merged CW-04c and PR-736 occlusion changes. The actual macOS Core
build04 and tool05 builds passed with zero warnings/errors. Each result retains
its original source/build lease. Latest-main `91f3a96d3240d612d4d7e97c1d198570d8197fad`
plus the validation overlay also builds on macOS with zero warnings/errors
(Core build10/tool11). Observer, retention, math, profile, replay-rule and CPU
frame-timing/handoff checks pass. PR-09a diagnostics are committed and merged in
[PR-750](https://github.com/AntiNotAnti/Project-Prime/pull/750). The subsequent
validation tools remain a separate slice; hosted CI and release gates are open.

## Completed scoped evidence

- A separate **current33 compatibility refresh passes after concurrent PR-754**:
  fresh Windows Core/tool build, 21 focused checks, both actual production durable
  packet comparators and both recorded replay scenes. Full preserved scene
  digests match. Fresh 60-second two-owner fault-combat arms pass all six process
  exits and 22 proof checks each: Native 15 joins/48 rewinds and Momentum 10/55.
  Opt-in NCX placement/input/render probes remain disabled in normal acceptance.
  The new Python evidence consumers pass 42 tests. This refresh does not relabel
  the earlier team or eight-owner source leases. See
  [PR-09d](pr09d-results.md) and
  [current-main compatibility evidence](evidence/pr09-current33-compatibility.json).
- The fresh **current46 Native/Momentum DefenderTeams pair passes**: two genuine
  human-owner processes plus two accepted level-three server bots, real balanced
  teams, 45 seconds per model on Proving Ground at 200:20. All six processes exit 0;
  both clients in each model adopt exact native objective facts and independently
  receive positive team time. Native solver steps are 0/0; Momentum 2,238/2,168.
  Current-source Windows gold56, rewind18,489, replay rules82 and frame timing
  also pass. This is representative team/objective coverage, not physical LAN or
  all-mode acceptance. The separate Native Nodes control has contact but no
  completed ten-second capture and remains unverified. See
  [PR-09d results](pr09d-results.md) and
  [source/binary leases and numeric witnesses](evidence/pr09-team-objective-draft.json).
- The additional **eight-human-owner fault combat arms pass** on the preserved
  Windows `3de8887a` production baseline with the current test-only overlay.
  Each model uses eight independent human-owner client processes (all seven
  Hunters plus a second Samus), no bots, and 60 seconds after genuine cohort
  arrival at 200 ms RTT / 20 ms jitter, 1% loss, 5% reorder and 1% duplication.
  All 18 server/client processes exit 0. The verifier checks all eight reports:
  each saw all seven remotes and obeyed its model's solver policy. Native has
  51 joined payments and 343 authority rewinds (5,157 frames); Momentum has
  59 joins and 414 rewinds (6,243 frames). All authority/client trace overflows
  and join drops are zero. Twenty corrupted copied-input proofs are rejected,
  including omitted/duplicate cohort members; no favorable pair is selected.
  [Combat evidence and exact scope](evidence/pr09-owner-combat-draft.json).
  These are process cohorts, not eight physical devices, balance comparisons
  or an eight-owner rerun on `91f3a96d`; the separate focused refresh follows.
- The separate **fresh `91f3a96d` focused Windows compatibility refresh passes**:
  56 native gold trajectories, 18,489 rewind assertions, 3,291 pure custom-weapon
  checks and diagnostic retention. Two genuine human-owner clients per model
  run 60 seconds at 200:20 with 1% loss, 5% reorder and 1% duplication.
  All six server/client processes exit 0; both strict proofs pass 22 checks.
  Native records 13 joined payments and 59 authority rewinds (898 frames);
  Momentum records 6 joins and 55 rewinds (828 frames). These authority totals
  do not prove individual paid-shot rewind depth. Exact hashes and scope remain
  in the [combat evidence](evidence/pr09-owner-combat-draft.json); this is not
  a rerun of the eight-owner matrix or a performance acceptance.
- **Owner combat passes within its stated scope:** a fresh Windows `3de8887a`
  checkout plus the hash-verified PR-09 overlay, real dedicated authority and
  two independent AI-driven human-owner clients per model, 60 seconds at
  configured 200 ms RTT / 20 ms jitter. All server/client processes and proof
  verifiers exit 0. Native has 6 certificate/claim/native damage-payment joins
  and 61 served rewinds (910 frames); Momentum has 12 joins and 66 rewinds
  (990 frames). Victim health decreases are observed; full payment identities
  are unique; authority overflow/join drops are zero. Rewind totals are
  authority aggregates, not depth proof for each payment. There is **no added
  packet loss, reordering or duplication** in these combat arms, and their
  counts are not cross-model balance comparisons. [Sanitized combat evidence](evidence/pr09-owner-combat-draft.json)
  includes ten rejected proof-corruption controls, two portable positive
  controls, unchanged original-input hashes and five retention-reset checks.
- The advisory observer now identifies model/profile in schema-2 samples, rows
  and bounded aggregates. Native thresholds and first fourteen aggregate indices
  remain intact. Momentum envelopes are broader diagnostics. Unknown profile
  evidence is invalid; `HardRejections` remains zero. See
  [advisory-validation.md](advisory-validation.md).
- Actual seven-Hunter Native/Momentum local scenes rendered world/HUD and ran
  native collision under virtual host cadences 30/60/120/144/240: 70 arms,
  119,166 checks, 180 fixed simulation ticks per arm. These are actual GL draws
  under synthetic scheduling, **not achieved display Hz** or physical-input runs.
  [Sanitized cadence measurements](evidence/pr09-cadence-draft.json) retain actual
  wall durations and draw counts so those quantities cannot be confused.
- Separate localhost UDP processes completed Native and Momentum eight-slot
  baseline arms and the later impaired arms. Eight slots mean four independent
  human clients plus four dedicated-authority bots, not eight human devices.
  The impaired fixture includes a 200 ms RTT / 20 ms jitter arm with loss,
  reordering and duplication; these are configured transport impairments, not
  Internet measurements. Later lifecycle arms completed reconnect/slot reuse;
  Native and Momentum two-client map-change arms completed. Raw earlier failing
  attempts remain retained. [Process outcomes](evidence/pr09-network-draft.json)
  distinguish normal exits from orchestrator-stopped servers. Snapshot age is
  local simulation frames since the newest received snapshot; it excludes the
  snapshot's preceding network flight time.
- Both complete spectator cohorts now pass: Native
  `native-spectator-baseline-02` and Momentum `momentum-spectator-baseline-03`.
  Each contains three playing human peers, one genuine spectator and four bots
  (eight occupied slots, seven playing entities). Native observed 1,200 fixed
  steps over 20 seconds; Momentum observed 2,361 over 40 seconds. Spectators kept
  native spectator flags, were ineligible for the movement adapter, and recorded
  zero solver steps. The Momentum playing peers earned 24/24/25 actual native
  accepted-jump samples, with Y = 0.2998047 bit-identical across the horizontal
  solver and within the unchanged native jump ceiling before gravity/collision.
- The earlier Momentum spectator-02 cohort failure remains retained: one owner
  ended a tick at Y = 0.29991472 versus jump impulse 0.2998047, with lateral
  collision flags and no pad lock. Its old log lacks solver delta and same-frame
  acceptance/contact witnesses; its exact cause remains **unclassified**. A
  final post-collision velocity is not a universal jump-impulse ceiling. The
  corrected extended Momentum fixture checks the actual native `LegalJump`
  label at the pre-gravity/pre-collision solver boundary, preserving the same
  tolerance and bit-identity checks. It does not change Native physics or the
  original no-options assertions. The follow-up source batch additionally captures
  every above-ceiling final-Y jump input, including native-bypass and rejected
  jump cases. Query scratch is explicitly not a resolved-contact witness.
- Existing authored-ramp evidence establishes the general distinction between
  solver Y and subsequent native collision response: PR-07 navigation-04,
  Samus/Momentum/first-veto tick 0, records same-frame solver Y 0 -> 0 and final
  native Y 0.04095151 while Standing, with no jump input. That offline fixture
  declares an initial cap-speed seed and makes no measured speed writes. It
  **does not identify the exact cause of the earlier UDP sample**, and is not
  relabelled as a fresh PR-09 rerun.
- Both actual different-map cases passed with two independent clients and
  orderly server exits: `MP3 PROVING GROUND` -> `MP1 SANCTORUS`, verified against
  loaded room identities. Both same-map cases separately passed two native
  rebuild/barrier cycles. Those earlier rematch workers used Sleep(16 ms) plus
  work, rather than a 60 Hz wall deadline; actual counts remain recorded. Later
  spectator/map workers use bounded deadline pacing, with each simulation call
  fixed at 1/60 second; load stalls can reduce processed calls per wall second.
- Momentum spectator-04's four clients also passed, but its server evidence was
  truncated when the runner's ten-second shutdown wait expired during
  serialization. That server JSON is invalid and is excluded. The fully orderly
  spectator-03 cohort supplies the green evidence; future runners allow sixty
  seconds for evidence flush.
- Windows current-source Release build completed with zero warnings/errors.
  Math, profile, admission, input packets, replay rules, movement-shadow and the
  existing Native golden fixture passed. Earlier Windows scene invocations with
  exit 2 were argument failures, not scene acceptance; later evidence must be
  judged from its actual command/results rather than those old runs.
- Windows OS-input work is limited interactive evidence. It does not establish
  the full controller, focus-loss, high-refresh, touch or multiplayer-input matrix.
- Seven-Hunter, two-map balance observations now include 112 actual macOS
  Native/Momentum scene trials and 38,640 bounded per-tick samples, with 215,756
  assertions; the original 84 Windows trials and 25,200 samples remain preserved.
  The matched seeded native spawns/facing, fixed input schedules, native jump
  impulses and Y-bit preservation pass. Per-Hunter/map speed distributions and
  radial traversal times include both cold and running manual-hop chains and
  neutral release. [Balance observations](balance-validation.md) and
  [numeric results](evidence/pr09-balance-runup-draft.json) retain the cold-start
  outcomes and censored objectives. No cap intervention occurred in these
  straight chains; maximum-cap stress and competitive balance remain unverified.
- Android evidence was captured at base `b1780d624` plus then-current PR-09
  preparation, not the later f908/CW-04 revision. The arm64 Debug APK built
  with embedded assemblies: zero errors, 29
  warnings, 2:30.49. Existing JDK 27 emitted version-parser warnings; the build
  succeeded. On an Android 36 read-only emulator, genuine kernel touch events
  became Android MotionEvent DOWN/MOVE/UP and moved the virtual stick and rendered
  world in both modes. Momentum was initially supplied as an explicit saved
  fixture, verified in the actual rules dialog; Classic was then selected and
  applied through that dialog. The default backend was Avalonia/GLES, not the
  optional native RmlUi package. Temporary AVD state was discarded. This is
  qualitative emulator evidence, **not physical Android**, precise velocity,
  successful jump, networked touch or solver-counter proof. SwiftShader rendering
  artifacts remain visible; the later Momentum death state prevents a jump claim.

## Replay and Native preservation

[PR-06](pr06-results.md) supplies the historical recording/context and
[PR-08](pr08-results.md) the earlier rule-label and replay evidence. The isolated
repair published as PR-752 feature commit
`e950d5cdce4e4c9fc690df66e5e3f6a24e484ec2` now passes both actual production
`-replaydurablecheck` recordings. Its binaries were built before commit from
base `91f3a96d3240d612d4d7e97c1d198570d8197fad` plus the then-dirty repair patch;
isolated Core build03/tool-build03 passed with zero warnings/errors, and tested
production source bytes match that feature commit. The evidence is not
relabeled as a clean post-merge build.

Both final runs preserve exact packet-decoder hash/player equality at targets
300/301/799/1250/1500/1800, actual world cold/backward bounded seeks, 801/351-frame
nested clip fidelity and corrupt-checkpoint fallback/recovery. The separate
scene fixture remains Native 85,673 assertions over 2,982 frames and Momentum
85,586 over 2,979 frames, with unchanged full gameplay/presentation digests.
All 22 scene fields exactly match preserved scene02/03 and committed PR-08;
against PR-06 all 21 fields other than assertion count match.

The repair declines saved-world indexes without an exact packet-only baseline,
reports explicit fallback and scans complete packet history, while draining
validated detached draw packets through the existing path. It weakens no hash,
player equality, malformed-packet or queue-budget check and makes no packet-index
performance claim. Original latest91f frame 301 failures remain retained, as do
isolated failed repair attempts at frame 300 and the existing 2048-packet bomb
queue budget at frame 664. Final repair run03 passes in both models.
[Replay validation](replay-validation.md) and
[sanitized evidence](evidence/pr09-replay-draft.json) retain exact build/source
leases, all failures and the earlier literal `durable` pathname invocation
error. This scoped repair result does not establish full PR-09 or hosted CI
acceptance.

The fresh verified Windows 3de checkout plus PR-09 overlay also passes all 56
Native golden trajectories at the unchanged 1e-5 tolerance and 18,489 earned
trajectory/historical-geometry rewind assertions. Asset-free replay/archive
fixtures preserve historical Native defaults and exact roundtrip bytes. These
are scoped preservation checks, not cross-platform world-hash equality. Any new
production gameplay/replay change must rerun the relevant golden,
checkpoint/seek and accepted-recording checks.

## Performance gate — FAIL

The later [PR-09e collision-query slice](pr09e-results.md) preserves exact seeded
workload and published snapshot bytes while removing duplicate static-face
hashing. Its fixed 32-arm Windows experiment retains all failures: Proving Ground
candidate median p95 is 0.25335 ms Native / 0.29720 ms Momentum (+17.31%); Sanctorus
is 0.31380 / 0.32510 ms (+3.60%). Both models show lower observed p95 than that
experiment's own baseline, but the original relative 5% proxy still fails on
Proving Ground. Fixed implementation-block order and wall-time measurement
limit causal CPU attribution. [Numeric evidence](evidence/pr09-collision-dedup.json)
retains p99/worst/allocations and all 32 arms. Earlier measurements below retain
their source/settings and are not overwritten.

The provisional limit remains +5%; no threshold or workload was changed to obtain
a pass. Each platform/map experiment used two sequential ABBA cycles, four runs
per model and 60 simulation seconds per run with eight genuine authority bots.
The measured value is elapsed wall-clock `ServerSim.Step`, including scheduling
and GC. Whole-loop process CPU totals do **not** supply per-step CPU p95; the
provisional CPU-tail budget and this wall-time proxy are not interchangeable.

| Host / room | Native median p95 (ms) | Momentum median p95 (ms) | Increase | Gate |
| --- | ---: | ---: | ---: | --- |
| macOS / Proving Ground | 2.32120 | 2.78195 | +19.8496% | FAIL; shared-host contamination |
| Windows / Proving Ground | 1.40105 | 1.82165 | +30.0203% | FAIL |
| Windows / Sanctorus | 1.39375 | 1.48630 | +6.6404% | FAIL |

[Sanitized original summaries](evidence/pr09-performance-draft.json) retain cycle
variation, p99 and worst-frame measurements. The Mac run was contaminated by
concurrent shared-host work and cannot establish an isolated regression estimate.
The operator also confirmed the Mac was busy with unrelated work during this
campaign; no further Mac performance timing is used for acceptance under that
load. The quieter, pinned Windows follow-up remains separate evidence.
Windows recorded workloads are deterministic within each model, but combat and
alive-player work differ across models (for example, Proving Ground mean shots
687 Native versus 1,040 Momentum). That prevents attribution of the whole delta
to horizontal solver cost. It does not turn the strict failures into passes.
Alive-conditioned and allocation analyses are diagnostic, not replacement gates.
A controlled workload/CPU-tail investigation and representative gameplay budget
acceptance remain required. Pure math throughput is not integrated acceptance.

### Controlled Windows investigation after PR-736

The fixed follow-up retained the original failures and ran both the original
baseline and the any-hit candidate on the same Windows Ryzen 7 9850X3D host.
Each version/map received two fresh-process ABBA cycles plus two separate
verification runs: 40 children and 32 timing arms in total, with no repeated or
discarded timing arms. Both versions explicitly set `DOTNET_TieredCompilation=0`,
`DOTNET_TieredPGO=0` and `DOTNET_ReadyToRun=0`, retaining 1,200 warm ticks and
3,600 sampled ticks. The original benchmark did not record tiering settings;
virtual warm-up duration alone did not establish wall-time JIT stabilization.
This is a declared protocol correction, not a reinterpretation of the old runs.

| Version / room | Native median p95 wall (ms) | Momentum median p95 wall (ms) | Increase | Strict 5% proxy |
| --- | ---: | ---: | ---: | --- |
| Original / Proving Ground | 0.37995 | 0.54930 | +44.5717% | FAIL |
| Any-hit / Proving Ground | 0.28480 | 0.32030 | +12.4649% | **FAIL** |
| Original / Sanctorus | 0.61580 | 0.59315 | −3.6781% | PASS |
| Any-hit / Sanctorus | 0.34755 | 0.35595 | +2.4169% | PASS |

These remain elapsed-wall proxies, **not p95 CPU measurements**. Mean whole-loop
process CPU totals for Native/Momentum were 894.53/1,226.56 ms before and
695.31/800.78 ms after on Proving Ground; Sanctorus was 1,562.50/1,531.25 ms
before and 925.78/921.88 ms after. Five one-second ambient CPU samples before
each block averaged 8.00%, 1.99%, 7.70% and 4.30%, respectively in the table's
order. All samples, per-arm wall/CPU results, p99 and worst frames are retained
in the [numeric evidence](evidence/pr09-performance-draft.json). Sequential
blocks and changing ambient load limit an exact causal speedup estimate.

The experiment used genuine base `f908c7e534bce67e07a3776b21a658790034544d`
plus the declared dirty PR-09 overlay, SHA-256
`bc3b769b4b7d7e169c58fd5f67a195e506f05109c485337fa4949b78836350a4`.
Recursive source comparison established that the clean candidate differed only
in the two AI/collision files; its patch SHA-256 was
`8faf234f89b164ae0f3f1baec0da56ac871eace79b7e07bc5a885f5de4e22034`.
SDK 10.0.401 Release builds passed with zero warnings/errors; .NET 10.0.12,
runtime configuration and both Core/tool binary hashes are recorded. This
isolated candidate preceded the merged PR-736 optimization; it is not presented
as a run of that later publication revision.

Diagnosis used bounded counters in separate source copies. During the 3,600
sampled Proving Ground ticks, Native/Momentum performed 265,247/320,071 LOS rays,
181,901/252,920 blocked rays, 1,906,155/2,402,413 candidate visits and
18,720,343/29,363,163 face visits. Momentum therefore caused 20.67% more LOS rays
and 56.85% more face visits through changed paths/forms/occlusion. Sanctorus LOS
work was nearly equal: 415,859/410,877 rays and 39,324,662/39,088,884 face visits.
The first instrumented Proving Ground slowest-frame sample attributed 55.5% of
Native and 61.1% of Momentum time to visibility/global LOS; projectile processing
was a small contributor. Instrumented phase timings are diagnostic only.

PR-736 preserves generic nearest-contact queries and adds early return only for
callers needing an obstruction boolean. Immediate same-world shadow checks of
1,931,862 actual AI LOS/global queries found zero boolean differences. All eight
shadow/optimized gameplay workload hashes and all 32 controlled timed workloads
exactly matched the original corresponding model/map records, including warm
bodies/RNG, per-step alive/eligible counts, per-slot work, shots, damage and
lifecycle. This is within-model preservation, not equal cross-model combat work
or an assertion that every unrecorded world transform was hashed. Windows also
passed 8,820 collision equivalence cases, preserved the 618-row nearest-result
snapshot and allocated zero bytes in 50,000 warmed any-hit queries.

The benchmark runner now explicitly pins the production-default
`PRIME_SHOT_PROOF=shadow` and `PRIME_ACCEPTED_SHOTS=1` values, records both settings,
and checks the effective shot-proof/playback state before creating a simulation.
The C# result records the effective states as well. Earlier experiments retain
their original source/binary hashes: their runner inherited these switches and
the benchmark did not record their effective values. Those historical settings
remain undisclosed; the new control is not evidence that the earlier arms used
these defaults. No timing arm was rerun or reclassified, and the existing failed
gates remain failed.

### Optional thread-cycle diagnostic

The benchmark can opt into Windows `QueryThreadCycleTime` observations with the
runner's `--thread-cycles` flag. It records preallocated per-step execution cycles
and p50/p95/p99, pins only its own simulation thread to the lowest allowed CPU,
records affinity and start/end processor, and restores affinity even on failure.
API failures fail closed. The wall timer ends before the end-cycle API call;
cycle measurements include the two wall timestamp reads. No calibration constant
is subtracted. macOS reports unsupported/null cycle evidence and makes no native
API call.

An isolated Windows calibration used .NET 10.0.12 with tiering/PGO/ReadyToRun
explicitly disabled and only its own thread pinned to CPU 0. Across 20,000 empty
paired reads, median/p95 were 940 cycles and 400 ns elapsed wall overhead.
Arithmetic loops scaled from 59,220 to 584,210 to 5,845,390 median cycles for
10,000/100,000/1,000,000 iterations. Sleeping for a requested 20 ms consumed
31.3023 ms median wall time but only 18,800 median cycles, including wakeup/API
work. Observed cycle deltas were quantized in multiples of 470. `GetThreadTimes`
was unsuitable for these sub-millisecond samples: most reads were zero and all
nonzero deltas were 15.625 ms. These are calibration observations, not simulation
acceptance or an assured hardware-independent resolution.

[Microsoft's API contract](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-querythreadcycletime)
includes user/kernel execution cycles and explicitly prohibits conversion to
elapsed time because hardware timer frequency behavior varies. Relative cycle
costs therefore remain a separate same-hardware/core diagnostic beside elapsed
wall p95 and whole-loop CPU totals. They do not change the existing 5% wall proxy,
establish CPU-millisecond tails, or repair unequal cross-model AI/world work.

The observer's fixed Windows follow-up used the unchanged current production
Core (`c3468982999a21d847d8297760768e200cf5b43c7ff60f2ad2f0509b3be06dca`)
plus a benchmark-only tool build. Two ABBA cycles per map, two verification
children per map and 16 timing arms completed without repeats. Every arm used
allowed affinity `FFFF`, selected mask `1`, CPU 0 at start/end and 3,600 raw
cycle samples. All recorded same-model/map gameplay workloads still matched the
originals exactly. Ambient CPU before the two blocks averaged 1.31% and 1.07%.

| Room | Native median p95 cycles | Momentum median p95 cycles | Cycle increase (diagnostic) | Native / Momentum wall p95 (ms) | Existing wall proxy |
| --- | ---: | ---: | ---: | ---: | --- |
| Proving Ground | 1,349,840 | 1,569,095 | +16.2430% | 0.31900 / 0.37040 | +16.1129%; **FAIL** |
| Sanctorus | 1,679,780 | 1,695,760 | +0.9513% | 0.40060 / 0.39565 | −1.2356%; PASS |

Mean whole-loop CPU totals were 722.66/765.63 ms for Native/Momentum on Proving
Ground and 972.66/972.66 ms on Sanctorus. These totals and cycle counts are not
CPU-millisecond tail measurements. Proving Ground's cycle increase supports a
real execution-cost difference beyond preemption alone; its different AI
occlusion work remains part of the genuine workload. The supplementary ratio
exceeds 5%, but it is not assigned a new acceptance gate. All original and
controlled failures remain retained; this result does not authorize a threshold
change, another optimization or feature completion. Source/tool hashes, per-arm
cycle p50/p95/p99, wall tails, aggregate CPU and affinity are in the
[numeric evidence](evidence/pr09-performance-draft.json).

### Performance budget decision remains pending

The candidate Proving Ground p95 wall values, 0.285/0.320 ms, are small relative
to a 16.667 ms fixed-step deadline, and both models' aggregate CPU totals fell.
That context does **not** meet or waive the agreed relative 5% gate: Momentum
still increased p95 by 12.4649%, and calibrated CPU-tail acceptance is absent.
The later pinned observer follow-up also leaves a 16.2430% execution-cycle
increase and a failed wall proxy on Proving Ground. No replacement threshold or
feature-complete claim is proposed here. Further acceptance needs a justified optimization/investigation or an explicit user
budget decision with these failures visible.

An eight-human-owner experiment with matched offered input would usefully
measure owner movement and representative server work without autonomous bot
LOS. Human server puppets correctly do not execute the owner solver, so server
measurements alone cannot substitute for owner-client measurements. Such an
experiment would complement, rather than erase, this genuine eight-bot failure.

## Remaining gates

- **FAIL:** strict integrated performance budget.
- **PASS (scoped):** the corrected actual accepted-jump boundary in the complete
  Momentum spectator-03 cohort; paired impaired-06 adds bounded owner traces
  with zero overflow. The earlier UDP final-Y sample lacks the same-frame
  witnesses and remains unclassified. No production jump ceiling was widened.
- **PASS (scoped):** two-human-owner Native/Momentum combat at 200 ms RTT /
  20 ms jitter: source certificate/claim/payment joins, victim health decreases,
  unique payments and positive authority rewind. [Proof and negative controls](evidence/pr09-owner-combat-draft.json).
- **PASS (scoped, preserved production baseline):** eight actual human owners,
  all seven Hunters, with 200:20 delay/jitter plus loss/reorder/duplicate faults;
  full-cohort accepted-source/payment/claim/rewind checks and negative controls.
- **PASS (scoped):** fresh91f native-guard/combat refresh: 56 Native golden
  trajectories, 18,489 rewind assertions, 3,291 custom contract checks and both
  two-owner impaired combat proofs (22 assertions each), all six processes
  exiting0. The preserved eight-owner cohort retains its earlier source lease.
- **OPEN:** representative actual team/objective gameplay and final retained-source
  refresh. Roster/movement passes and combat payment proofs remain distinct;
  additional simultaneous combat/lifecycle combinations are unverified.
- **UNVERIFIED:** physical Android, full Windows/Linux physical-input matrix,
  real controller/focus recovery, measured high-refresh behavior and sustained
  device/server operating budgets. Emulator and virtual-cadence evidence do not
  close those gates.
- **BLOCKED:** macOS OS-event input. The actual .NET 10.0.12 process TCC probe
  returned `CGPreflightPostEventAccess=false` and `AXIsProcessTrusted=false` on
  macOS 27.0.1 arm64. It made no permission request and created/posted no events;
  this read-only probe is not a keyboard movement acceptance run. The compiled
  movement fixture independently exits `BLOCKED_EVENT_POST_ACCESS` before a
  window is created, with zero posted events. Evidence is retained under
  `artifacts/momentum-pr09/macos-input-access-probe/` and `macos-keyboard/`.
- **PASS (scoped):** focused CW-04c + PR-736 compatibility on actual macOS
  Core3de + PR-09: the custom-weapons-check tool-only build passed with zero
  warnings/errors, followed by 2,093 contract, 45 offline scene and 104 rewind
  scope checks. Logs are retained under
  `artifacts/momentum-pr09/current-custom-compat-01/`. The rewind fixture seeds
  custom certificates and exercises native bodies/history/private-pool scope;
  custom live transport/admission, payment claims and custom replay acceptance
  remain outside this evidence. Fresh 3de Windows Native gold/rewind and macOS
  Native/Momentum recorded-world continuity pass in the scopes above.
- **PENDING:** final publication fixture/source refresh and subsequent retained
  tool-source checks; these are not new production-physics acceptance results.
- **PENDING:** relevant hosted CI pass. Protocol-registry and waitlist-source
  fixture failures were repaired in PR-735 and PR-737 without weakening the
  production contracts. The 13 remaining retired-Hunter commands pass on the
  verified portable 3de/PR-09 assembly lease, including eight-peer transport,
  replay-format, shot/bomb and mover checks. Latest-main hosted workflows were
  queued at inspection; no full-green claim follows from local checks.
- **PASS (scoped replay repair):** PR-752 feature commit `e950d5cdc`, tested as
  isolated base91f plus dirty repair patch, passes both actual durable recordings
  at exact targets 300/301/799/1250/1500/1800, world cold/backward seeks,
  801/351-frame nested clips and corruption fallback. All historical scene fields
  and digests remain unchanged. The original frame 301 failures and isolated
  frame 300/frame 664 failed attempts remain retained. Complete packet-history
  fallback replaces incompatible saved-world packet indexes; no hash weakening,
  packet-index performance or full hosted-CI acceptance is claimed.

Dense raw logs, screenshots, scripts, all failed attempts and per-frame traces
remain ignored under `artifacts/momentum-pr09`. Committed evidence contains only
small numeric summaries without personal paths or extracted assets. This draft
must be reconciled with subsequent results before a final PR-09 status decision.

The paired impaired-05 advisory rows were reconstructed against the actual owning
clients (Native slot 2/client 2, Momentum slot 7/client 3). Native source frame
1608 measures a 0.89566994-unit horizontal position delta; Momentum has six
candidates at source frames 969, 980 and 986–989. All seven match owner position
differences with the pre-step intent/after-step evidence frame offset, including
frame 980's two-frame interval. These are position-derived observations, not
measurements of native `Speed`. Momentum alternates X displacement against
constant Z=-24.699951, but retained evidence lacks per-tick velocity, contact
normals and impulse/exemption state. All seven remain unclassified; collision
causation and explosive native velocity are not established. Hard rejections
remain zero; advisory WouldRefuse counts remain Native 1/Momentum 6. Exact
position joins are in `artifacts/momentum-pr09/impaired05-advisory-analysis.json`.

A current-Core3de/tool06 follow-up (`native-8player-impaired-06` and
`momentum-8player-impaired-06`) passed all eight clients and both orderly
servers. Every owner anomaly trace had zero overflow. Native reproduced four
advisory candidates, Momentum one; all five join the actual owner step. For
example, Momentum source649/slot6 has X displacement -0.8659301 but pre-step
native SpeedX=-0.001570126, solver outputX=-0.0013188522 and final SpeedX=0;
solver Y remains0.0009375445. Native source848/slot6 has X displacement
+0.83398914 with both pre/post native Speed exactly zero. This positively
distinguishes large body-position corrections from explosive native velocity
at the reproduced steps. Native player separation directly changes Position
(`PlayerCollision.cs:116`), as do map contact pushouts, but the trace does not
witness the exact correction call. Collision subtype remains unclassified,
and these new observations do not retroactively classify the seven earlier
impaired05 rows. Full joins are in
`artifacts/momentum-pr09/impaired06-advisory-analysis.json`.
