# N13 server measurements and abuse hardening

Status: focused correctness PASS; matched local performance FLAGGED; Default-Shadow UDP gameplay PASS with live settlement telemetry INCOMPLETE; local timing INCONCLUSIVE after A/A control. Target hosting hardware is unspecified; this record does not certify the plan's target-hardware 5% p99 gate.

## Implementation

Default-off `PRIME_SERVER_PERF_OUTPUT` telemetry aggregates Step, PumpBefore, PumpAfter, Replication, Eligibility and Settlement. Durations are Stopwatch elapsed wall time, including scheduling and GC. Overlapping scopes cannot be summed or interpreted as CPU attribution or network latency. Separate process CPU and total GC pause measurements cover the entire run. Fixed six-by-257 outcome aggregates and six-by-32 power-of-two microsecond histograms contain no player, shot, endpoint, position or account identifiers; bucket31 includes overflow. `FileMode.CreateNew` preserves existing reports.

A demonstrated ingress defect is also fixed: malformed Shock Coil claims missing continuous tick/phase shape could enter generic rescue in Off/Shadow and pay separately from native continuous damage. `6e5988b7` rejects malformed continuous shapes in every proof mode. The dedicated regression covers Off/Shadow/Enforce, seven shape variants, and before/after payment and retained terminal receipts. Native continuous-combat has 625 passing checks; the same new runner against the preserved pre-guard engine fails the literal Off/missing-flag wire case as expected. The separate 63-assertion continuous-scene suite tests native targeting, not this guard.

No speculative caching, scratch-state reuse, queue enlargement, or gameplay default change is introduced. Existing rewind, inventory and affliction rules remain required.

## Matched local measurements

The committed driver is `tools/nettest/run-net13-matched.py`. Preparation is the default; `--execute` runs serially without competing builds/tests/campaigns. Use the preserved N12 attempt-09 baseline and candidate with the same runner, SDK, asset profile, room, seeded scene RNG and verified active hunter roster. Workload counters, population, simulation failures, hashes and source identities must match before interpreting timing.

The matrix requests 2/4/8 players, three alternating-order baseline/candidate pairs per size, 300 warm-up steps and 3,600 measured steps (60 simulated seconds). Report actual active/alive populations and shot/damage/claim counters; requested population alone is not workload proof. Synthetic asset-backed ServerSim does not establish real UDP capacity or target-host behavior.

| Players | Valid workload | Paired p99 | Allocations/step | GC pause | Status |
| --- | --- | --- | --- | --- | --- |
| 2 | All 3 exact matches | +5.465%, -6.286%, +11.426% | 1,482.36 bytes | 0 | FLAGGED |
| 4 | All 3 exact matches | +1.134%, +0.559%, +2.409% | 1,676.91 bytes | 0 | No local pair >5% |
| 8 | All 3 exact matches | +3.652%, -0.756%, -7.217% | ~1,898.20 bytes | 1 Gen0; ~1.3 ms | No local pair >5% |

A paired p99 increase above 5% is an investigation flag and cannot satisfy acceptance. Invalid workloads cannot satisfy it either. Inspect p95/p99/p99.9, allocations, GC counts/pauses and each pair independently; do not pool unlike workloads or present local observations as a confidence interval or hosting recommendation.

## Correctness evidence

Verified candidate attempt-05 source is `3ce797f0ed995c6a534ec84b3df43445e3be5cd5`; engine SHA256 `f1ef2dcd5ed933ae48e2a8d0398a64d7f4f94ab282c1081a138dc576b026ac49`. Final focused correctness PASS: eight cheap routes, six RTT claim-load arms each with 7,200 exact payments and terminal acknowledgements, 625 native continuous-combat checks, 63 native targeting assertions, and 327 Python checks. Default-Shadow UDP gameplay passes; live Settlement coverage remains incomplete.

Relevant focused routes include `--server-performance-evidence off/on`, `--queue-budget`, `--claim-stress`, `--transport-stress`, `--allocations`, `--netcode-performance`, `--dynamic-geometry`, asset-backed `--claim-load-scene`, and protocol/architecture contracts. The exact executed routes and results are retained in the focused manifest; unlisted protocol/reconnect/custom-map stress is not implied. Real UDP `run-net2.py --server-performance` evidence must retain the existing gameplay, spectator and bounded-drain gates alongside valid aggregate reports.

## Retained failures

| Evidence | Result and diagnosis | Correction or scope |
| --- | --- | --- |
| Attempt-01 legacy claim-load; preserved N12 baseline | Both FAIL. Diagnostic exposed malformed continuous ingress and double-payment path: 7,200 generic rescues plus 5,776 native physical calls. | Production shape guard plus independent native regression/red-control; diagnostic counts are not production incidence. |
| Generic claim-load fixture shape/order | Legacy continuous shape was unsuitable for generic discrete arbitration. | Actual Power Beam component, exact native-paid duplicate guard, normal ReplayAfterSimulation; does not substitute for continuous guard. |
| Attempt-03 drain | All 7,200 payments, eight bodies at exact expected 19,100 HP, pending/refused0; terminal ACK7,092/missing108. Six 80 ms fixture retries coalesced in a 400 ms stall. | Actual client intervals: minimum8frames (~133ms), RTT clamp8–40frames; same six attempts/180-step drain. |
| Attempt-04 drain | All 7,200 payments but106 ACK missing. Up-front future retry reservations advanced global NetFaultQueue FIFO lastDue and delayed other traffic. | Fixture schedules only actual due/nonstalled sends, cancels on ACK, maximum six attempts. No production ACK capacity, budget or drain widening. |
| First real UDP Enforce control | Strict base FAIL despite process exits0/drainPASS: client-only weapon grants were absent from authority spawn inventory;359 UnownedWeapon and bounded proof pool684 UnknownShot dispositions, no actual native commits. | Configuration/workload failure, not evidence of guard regression or Enforce acceptance. Valid continuous claim Flags192; fresh production-default Shadow control pending. |
| Pre-guard red-control | Expected FAIL on literal Off/missing-flag wire claim with the new runner and old engine. | Positive defect proof; preserved as failure. |

Diagnostic startup-hook binaries and their hashes are distinct from final candidate artifacts. Intermediate build success is not correctness acceptance. All original failures remain retained; later fixture fixes do not overwrite them.

## Limits and decisions

The evidence ledger is `tools/nettest/baselines/net2-n13-20261011.json`. All 18 matched runs exited0; all nine pairs matched seeded RNG, actual population, workload and unchanged binary hashes. Actual shots were 360/720/1,440 for 2/4/8 players; claims0. These are native simulation measurements with no transport or claim workload. Matrix-attempt-01 was invalid because copied-profile cache drift altered seed inputs and is retained; corrected driver `9d1d9687` produced matrix-attempt-02. Two-player p99 flags remain; One identical-baseline A/A control used the same N12 engine under both labels and the same candidate runner. Its three valid pair changes were +7.791%, +0.048%, and -3.085%; one also exceeded5%, with identical-engine p99 ranging0.2042–0.2269ms. A common largest step near measured step957 appears in both datasets; its cause is unproven and is not attributed to JIT. Local timing is INCONCLUSIVE, original matrix flags remain, and no candidate rerun or noise waiver turns the performance gate green. The moderate two-client production-default Shadow UDP control passes strict base adjudication and bounded drain with unchanged engine hashes. It records 53 Eligibility operations (51 result0, two result2) and zero Settlement operations; live accepted/rejected settlement telemetry is INCOMPLETE. The invalid Enforce workload does not fill that gap. Frozen synthetic claim-load and native continuous guard tests remain separate correctness evidence. No optimization is justified by instrumentation alone. Hosting targets remain unspecified; Mac-only measurements do not certify Windows, Linux, Android, arbitrary reconnect/custom-map workloads or unknown deployment hardware.

Implementation readiness is separate from performance certification: default-off private aggregate diagnostics and the malformed-continuous-claim guard have verified focused correctness. There is no optimization, default change, performance gain, or target-host capacity claim. Target hosting validation is NOT_RUN; it remains a release/deployment prerequisite when hardware is selected. N13 measurement/abuse implementation is reviewable, but its full performance and live-settlement coverage gates are not certified complete.
