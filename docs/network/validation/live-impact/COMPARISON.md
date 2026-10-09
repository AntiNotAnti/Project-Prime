# Combat impact acceptance comparison

The implemented chain is **partial and default-off**. It preserves combat authority;
it has not passed the requested release/visual campaign. New retained text logs/CSV normalize line endings and trailing whitespace for repository
review; numeric/event payloads are unchanged. Machine-readable status is
in [after.json](after.json); the original observation is [baseline.json](baseline.json).

## Local contracts

All 20 final checks passed. The existing 3,338,739 health/shot assertions, claim stress,
2352 lag profiles, 1620 weapon timing profiles, transport stress, lifecycle and historical
replay contracts remain green. New checks cover 21 baseline, 569 live codec/identity/queue,
614 transport (includes the 569), 22 presentation, 39 settlement safety, 18 visual ticket,
and three profiling contracts. The asset-backed checks passed 57 authority/resource and
156 fire-context assertions on installed MP1 SANCTORUS data. Counts are not added together
where suites overlap. Raw commands/results/logs are under [after-tests](after-tests/results.json).

The [45-scenario transport simulation](after-transport.json) uses production outboxes and
codecs for 2/4/8 peers and 0/50/150/250/350 ms RTT with positive FIFO jitter 0/40/80 ms,
2% loss, 3% reorder and 1% duplicates. Each passes >=98% unique delivery at 10 facts/s/peer.
This is not the requested symmetric-jitter rendered UDP acceptance campaign.

## Real native UDP

The frozen client/server assembly hash is identical to the final contract build.
Eleven 30-second arms used isolated preferences and real 60 Hz native simulations.
Seven passed movement, firing and a nonzero authority-confirmed combat requirement;
four failed the combat requirement. All eleven retained their original successful native
simulation checks. No test was weakened or relabeled to hide zero confirmed hits.

| Players | Mode | RTT / loss | Result | Authority-confirmed client hits |
| --- | --- | --- | --- | ---: |
| 2 | Imperialist sniper | 0 / 0% | fail: zero confirmations | 0 |
| 2 | Imperialist sniper | 250 / 2% | fail: zero confirmations | 0 |
| 2 | Imperialist sniper | 350 / 2% | pass | 1 |
| 2 | Power Beam | 0 / 0% | pass | 14 |
| 2 | Power Beam | 250 / 2% | pass | 15 |
| 2 | Power Beam | 350 / 2% | pass | 18 |
| 2 | Shock Coil | 0 / 0% | pass | 1 |
| 2 | Shock Coil | 250 / 2% | fail: zero confirmations | 0 |
| 2 | Shock Coil | 350 / 2% | fail: zero confirmations | 0 |
| 4 | Power Beam | 250 / 2% + 322 ms observed pause | pass | 25 |
| 8 | Power Beam | 250 / 2% | pass | 92 |

Impaired arms used jitter `min(60, RTT/5)` per the existing network conditioner,
2% reorder and 1% duplication. Their exact commands, reports and source/binary freeze
checks are under [native-p2](native-p2/summary.json), [native-p4](native-p4/summary.json)
and [native-p8](native-p8/summary.json). This samples three rig modes, not all variants. **Actual facts are Power Beam (172)
and Missile (3); no Imperialist or Shock Coil fact was produced.** The nonzero sniper/
Shock Coil arms passed generic combat smoke using Missile facts, not intended-weapon
acceptance. Per-weapon counters are retained in each report.
The rig locally arms weapons; that does not confer authoritative weapon admission.
The [pristine baseline controls](native-baseline/summary.json) reproduce zero confirmations
in both the 250 ms sniper and Shock Coil arms, with successful native simulation checks.
Their frozen protocol-44 assembly hash matches the benchmark baseline. This demonstrates
the fixture gap before these impact changes. It does not validate the intended weapons.
Zero-confirmation arms cannot establish delivery or weapon fidelity. Weapon acquisition/
admission needs its own fixture repair without bypassing production validation.

Across these runs there are 175 retained authority facts and 934 unique peer ingresses.
All 934 exactly join to a server authority fact; zero have an absent authority identity.
There were zero diagnostic ring overwrites. This is an ingress integrity observation,
not a 100% visual success rate or a full eligible-delivery denominator: readiness,
lifecycle and staggered client shutdown can omit facts from a peer. Headless clients
never drew/dequeued cues and these runs stayed below the 128 pending-event capacity.

The [same-process report](native-p2/report.json) retains every latency sample and counts
negative arrival-before-local-hit candidates separately. For Power Beam at 250 ms,
14 nonnegative samples have p90 217.445 ms; at 350 ms, 18 have p90 350.069 ms. Those small,
conditional groups are not a global p90/p99 claim. Cross-process clocks are never
subtracted. Local firing-to-audio, rendered cue latency, multi-camera correlation,
observer-only views and predicted-kill reversal rates were not measured.

The native profile now exercises `NetAttackPaths.Supports`: at two-player Power Beam
250/350 ms, 133/163 calls totaled 1.673/1.985 ms with zero measured allocation. Catch-up
used 23.692/23.449 ms over the complete runs; historical geometry used 2.759/2.619 ms.
Scopes can overlap and are not additive. This is a small workload, not an optimization win.

## Alternating before/after benchmark

The original baseline timings overlapped builds and are retained as observations only.
The new [matched comparison](matched/comparison.json) alternates frozen baseline and
current binaries, three repeats each, 300 warm-up + 1800 measured steps per repeat,
2/4/8 synthetic-intent players, same room and .NET 10 runtime. Other task-owned native
runs/builds were complete before measurement. Feature gates use defaults.

| Players | Before p99 ms | After p99 ms | Change | Allocation bytes/step, before = after |
| --- | ---: | ---: | ---: | ---: |
| 2 | 0.2681 | 0.2680 | -0.04% | 1670.20 |
| 4 | 0.4549 | 0.4825 | +6.07% | 1858.27 |
| 8 | 0.9848 | 0.9666 | -1.85% | 2238.41 |

These are medians of three run percentiles, not pooled percentiles or confidence bounds.
Four-player tail variation exceeds 5%; it is retained, not rounded into a pass. All
steady-state allocation measurements match exactly. The baseline runner lacks p99.9;
the new runner records it in each raw JSON. No collision optimization was enabled and
no reduction is attributed to one. There are no sockets or mixed bots in this benchmark,
so the plan's enabled eight-player <=5% p99 gate remains **unverified**.

Each live payload is 74 bytes, framed datagram 98 bytes, below the 1472-byte ceiling.
Snapshots are unchanged. Two unreliable attempts are bounded to four transmissions per
simulation frame per ready peer: <=240 datagrams / 23,520 framed bytes/s per peer,
<=188,160 for eight peers, excluding IP/UDP overhead. This is the enforced saturation
ceiling, not measured typical bandwidth. Cosmetic receive capacity is separate and
ordinary realtime gameplay drains first. Saturation tests retain full gameplay/critical
admission while cosmetics are full.

## Platform/build verification

The complete `tools/check-engineering-contracts.sh` passed on macOS, including
replay format/control, input/lobby/health/transport, renderer/pacing, map/replay
preparation contracts and the final dedicated-server compile. The raw log is retained.

The Android ARM64 Debug build passed with 128 warnings and zero errors, with embedded
managed assemblies and RmlUi enabled. The initially stale native bridge was rebuilt from
its pinned sources; the produced APK passed the repository's RmlUi package verifier.
No device installation/rendered combat is claimed. Logs are in `platforms/`.

The final implementation CI snapshot has successful Linux and Windows impact contracts;
macOS is queued. Overall CI is **not green**: the Windows native RmlUi UIA fixture accepts
four requests but observes no acknowledged native invocation. The migration aggregator
also fails on cancelled dependencies from a superseded run. Exact links and logs are
under `ci/`. Those jobs exercise unchanged UI bridge code; this report does not infer
that a failure is pre-existing without a baseline CI comparison.

## Acceptance still required

The new local contract and headless evidence does not certify Windows/Linux/macOS/Android
rendered play, Replay Studio seek/render/export, custom/dynamic geometry, all charged/
affinity/child/alt/turret/burn paths, mixed bots, spectator-only clients, 60–360 Hz,
physical mobile focus/resume, or an impaired predicted-kill reversal rate. CI/build results
are recorded separately in `after.json`; a compile pass cannot close these runtime gates.

Early claim settlement is explicitly refused because the exact native component
suppression and closed attack-order proof are missing. Shadow sampling changes no
reservation, outcome or grace interval. The prototype kill ticket and impact rendering
remain off. See the [implementation report](../../implementation/pr109-inspired/IMPLEMENTATION.md)
for the PR chain, rollback commands and prioritized security, fidelity, visuals and
performance work.
