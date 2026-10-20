# PR-09h — retained Windows CPU experiment

**Measurement inconclusive; operator accepts the performance tradeoff for now.**
The second fixed experiment completes all four verification processes and all
32 timing processes. Its predeclared observer-quality control fails on both
maps, so scheduler CPU estimates cannot establish a qualifying 5% result.
The experiment's threshold remains unchanged. No Mac simulation timing is collected while
the operator's host is busy.

## Fixed protocol and provenance

The [CPU protocol](../../tools/movement-cpu/CPU-PROTOCOL.md) uses two
Native/Momentum ABBA cycles on each map, with adjacent traced/control arms and
reversed treatment order. Each fresh process runs 1,200 warm-up and 3,600
measured fixed-60-Hz steps with eight genuine authority bots. Both treatments
use the same Step-span and cycle observers. Verification-only processes hash
all 3,600 published snapshot payloads; timing processes do not hash them.
Every captured process and failed quality check is retained.

The actual benchmark binary remains the PR-09f build on `b05e74d418ca5a8c8eccb8ad570d99c3ad4f473a`
plus its recorded overlay. The reused normal Core is the verified current5d
production binary with the collision-query change. Later fixture-only changes
were reviewed for source compatibility; this does not relabel the old binaries
as a fresh latest-main build. Full source, binary, runtime, frozen-plan and
provider hashes belong to the numeric evidence. Subsequent NCX03/NCX04 add
default-Off combat-observer/paid-receipt hooks, and other merges change camera/cosmetic
presentation. Their focused compatibility checks are separate. Even an Off
guard has a possible cost; these captured timings do not measure the newer
source or establish its current performance budget.

The first retained attempt and its decoder failures remain separate in
[PR-09f](pr09f-results.md). The second attempt omits unused ReadyThread events
and captures every ETL before any export, parsing or compression. Offline
analysis subsequently processes one trace at a time with the declared disk,
memory and timeout guards. Raw system traces remain untracked. Lossless gzip
exports are verified to EOF for CRC, byte count and SHA before redundant owned
scratch is removed; original ETLs remain preserved.

## Observer controls

The contract requires absolute trace/control wall and whole-loop CPU
perturbation within 5%, and differential wall-ratio distortion within 1%.
Absolute controls pass, but differential distortion is **+1.303044% on
Proving Ground** and **+1.853131% on Sanctorus**. Neither failed control is
waived after seeing the results.

| Room | Native control / traced p95 (ms) | Momentum control / traced p95 (ms) | Control Momentum increase | Traced Momentum increase |
| --- | ---: | ---: | ---: | ---: |
| Proving Ground | 0.26910 / 0.27240 | 0.30055 / 0.30820 | +11.6871% | +13.1424% |
| Sanctorus | 0.32445 / 0.32635 | 0.33475 / 0.34295 | +3.1746% | +5.0866% |

These are medians of per-process elapsed-Step p95 values, not scheduler CPU
p95, achieved frame rates or causal solver overhead. Whole-loop CPU includes
post-Step observations and remains a separate quantity. Within each map/model,
workload hashes agree across all traced and control arms. Cross-model gameplay
differs: Proving Ground records 687 Native versus 1,040 Momentum shots, while
Sanctorus records 462 versus 756. The entire world cost difference cannot be
attributed to the horizontal solver, and dividing by shots would not repair
the failed acceptance control.

## Completed scheduler accounting

All 16 trace audits complete successfully, accounting for all 57,600 measured
Step spans with required zero-loss, clock/schema, identity, pinned-CPU and
switch-chain checks. All 48 raw/normal/dump exports pass lossless SHA/byte-count
and gzip CRC checks; all 16 original ETL hashes match capture receipts.
Decoder totals retain unrelated tracerpt processing errors explicitly. Only
the required scheduler/lifecycle/interrupt providers are independently joined;
this is not a claim that every system provider decodes.

| Room | Native CPU p95 (ms) | Momentum CPU p95 (ms) | Diagnostic increase | Native / Momentum CPU p99 (ms) | Worst observed Native / Momentum CPU Step (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Proving Ground | 0.26770 | 0.30235 | +12.9436% | 0.33015 / 0.36325 | 3.4552 / 5.7473 |
| Sanctorus | 0.31985 | 0.33665 | +5.25246% | 0.36675 / 0.38835 | 2.6645 / 6.8530 |

p95/p99 are medians of four per-process quantiles per model/map; worst is the
maximum across all four traced arms. Accounting intersects the target thread's
scheduled intervals with each Step and subtracts the union of interrupt/DPC
intervals. The largest numerical error bound is 4.8 microseconds. Conservative
p95 ratio upper estimates are +13.1899% and +5.51119%; these bounds cover clock
quantization, not logging bias or all background/firmware effects.

Both map results remain **measurement-invalid/inconclusive** because of the
predeclared observer-quality failures. The diagnostic estimates do not satisfy
or override that control. All 32 wall/cycle tails, world allocations, workload
counters, 16 CPU audits, archive hashes and original source leases remain in
[the numeric evidence](evidence/pr09-cpu-attempt02.json). The collector's 344
checks and an independent 338-check review pass; the earlier saved-capture
review also recomputes all wall quantiles directly from the raw arrays.

## Remaining acceptance

After reviewing the Windows investigation and failed observer controls, the
operator accepted the current performance tradeoff on 2026-10-19 and deferred
optimization. This supersedes the provisional 5% budget as a release blocker
for this implementation. It does not turn the measurement into a qualifying
CPU result or establish a new numerical performance guarantee. Keep the full
failed controls and historical cost estimates visible for later optimization.

Mac load can explain contamination of the earlier Mac wall-clock experiment.
It does not resolve the independent Windows Proving Ground difference, and
the failed trace control prevents a clean CPU-tail acceptance claim.
Warmed solver allocation checks remain distinct from nonzero whole-world
allocations. Classic remains the default and rollback path; Momentum remains
an explicit match-wide opt-in. Remaining correctness, compatibility and hosted
CI checks are separate from this accepted performance tradeoff.
