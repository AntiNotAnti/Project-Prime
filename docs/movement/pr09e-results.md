# PR-09e — collision query hashing and retained performance evidence

**Correctness PASS; performance release gate remains open.** This slice removes
a duplicate hash lookup when ray queries encounter a new static collision face.
It does not change movement coefficients, collision arithmetic, query order,
mask filtering, dynamic-entity handling, protocol or replay formats.

The old path checked `Contains`, then called `Add` for an unseen static face.
The new path uses `Add`'s return value for that same decision. Dynamic entities
retain their existing `Contains`-only behavior. Mask checks still happen first.
The face equality/hash definitions remain unchanged.

The benchmark gains a verification-only SHA-256 digest of every actual published
snapshot payload in its measured window. Verification asserts complete positive
coverage. Timing arms create no hash object and record a null digest/count 0.
This proves published snapshot bytes for the tested seeded worlds, not every
nonserialized game-state field. Both versions use identical observer source.

## Verification

The Windows experiment uses genuine main
`64894cd4558f5869a7968e02e9594c897d338c3e` baseline and a separate candidate with
only the collision source patch, plus the common test observer. Both full builds
and focused tools pass. Each version passes Native gold (56 trajectories), rewind (18,489 assertions) and
replay rules (82 assertions). Collision checks cover masks, any-hit/nearest equivalence, nested
workspace lifetime and 50,000 warmed zero-allocation calls; the candidate's 618-row
nearest-contact snapshot is byte-identical to the baseline.

All eight diagnostic verification arms pass. Four map/model pairs preserve exact
canonical workload/RNG/stable-counter hashes and byte hashes covering all 3,600
published snapshot payloads. Every one of the 32 timing arms also matches its corresponding
verification workload hash; all processes exit 0. Native collision contact
overflow, recovery fallback and failed simulation-step deltas are 0 throughout.
Core/tool hashes remain unchanged during each frozen lease.

The candidate also passes both actual recorded replay scenes with durable-file
seeks and both production durable packet checks. All 22 scene-report fields
exactly match current33 results, including Native 85,673 checks/2,982 frames and
Momentum 85,586/2,979, complete gameplay/presentation digests and six durable seeks.
No GL, physical input, new UDP cohort or universal cross-CPU claim follows.

## Performance — original gate not cleared

Each map/version receives two fixed fresh-process Native/Momentum ABBA cycles,
with 1,200 warm ticks and 3,600 measured ticks per arm. All 32 predeclared timing
arms remain retained. Production-default Shadow shot proof and accepted-shot
playback are explicitly pinned; tiered compilation, tiered PGO and ReadyToRun
are disabled in both versions. NCX probes, soak/evidence instrumentation and
thread-cycle instrumentation are disabled. No Mac timing was collected.

| Map | Version | Native median p95 ms | Momentum median p95 ms | Momentum increase | Original 5% wall proxy |
| --- | --- | ---: | ---: | ---: | --- |
| Proving Ground | Baseline | 0.28840 | 0.32255 | +11.84% | FAIL |
| Proving Ground | Candidate | 0.25335 | 0.29720 | +17.31% | FAIL |
| Sanctorus | Baseline | 0.35330 | 0.36180 | +2.41% | PASS |
| Sanctorus | Candidate | 0.31380 | 0.32510 | +3.60% | PASS |

Observed same-model candidate p95 is lower by 7.9–12.2%. The implementation
blocks ran in fixed order, so these wall-time observations do not establish a
randomized causal CPU effect. Median p99 is also lower in all four map/model
pairs, while maximum individual worst steps include increases on Proving Ground;
all mean/p99/worst, GC, allocation and collision results remain in
[numeric evidence](evidence/pr09-collision-dedup.json).

Faster Native performance makes the relative Momentum gap larger in this run.
The threshold stays 5%; no favorable run is selected and no prior failure is
relabelled. These are elapsed `ServerSim.Step` times, not per-step CPU
milliseconds. Native/Momentum worlds perform different AI/visibility/combat
work, so the full gap is not a measurement of solver cost alone. Accurate CPU
accounting and the release decision remain open.

## Integration and remaining work

The concurrent [PR-756](https://github.com/AntiNotAnti/Project-Prime/pull/756)
repairs a stale UI range fixture without production changes. Subsequent
[PR-757](https://github.com/AntiNotAnti/Project-Prime/pull/757) changes ordinary
claim receipt retention and continuous-shot identity. This slice is rebased onto
that main (`5d68d2ee8cfbf592c698c24139f99a8222dbe10b`) and freshly rebuilt on
Windows, with the same two collision/benchmark source files verified by hash.
The 648 timing observations above retain their original production lease.

Latest-source checks pass: 15 focused cases, including 698 receipt checks,
1,157 actual arbitration assertions, 667 continuous-combat assertions, Native
gold/rewind and both replay scenes; both production durable packet comparators;
and 51 NCX Python tests. Four verification-only bot arms preserve the earlier
candidate's exact workload/counter and 3,600-payload snapshot hashes. They are
correctness comparisons, not new timing measurements.

Both actual two-owner 60-second fault cohorts (200 ±20 ms, 1% loss, 5% reorder,
1% duplication) pass all 22 payment/authority proofs. Native/Momentum respectively
record 13/9 joined payments and 57/90 authoritative rewind samples. Natural
death/respawn and claim retries occur in both. All six processes exit 0 without
evidence overflow or disconnects. Both replay scenes preserve all 22 report
fields against current33, including the digests and six durable seeks. See
[current5d compatibility evidence](evidence/pr09-current5d-compatibility.json)
for source/binary hashes, counts and scope. No new team/device/GL claim follows.

Source/asset and whitespace checks pass; hosted CI remains queued at publication
and is not waited on. Classic remains
the default and rollback path. PR-09 is incomplete: performance, the retained
Nodes capture control, and the documented device/input gaps are still open.
