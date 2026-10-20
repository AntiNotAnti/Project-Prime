# PR-09f — simulation-thread CPU accounting

This test-only slice separates elapsed Step time from Windows scheduled-thread
CPU time. Production movement, collision arithmetic, networking, replay formats
and the Classic default are unchanged. The provisional performance threshold
remains 5%; the accounting tool alone does not establish feature acceptance.

## Measurement contract

The benchmark opt-in `PRIME_BENCHMARK_STEP_SPANS=1` exports existing QPC start/end
timestamps and native process/thread identities. The two fixed arrays allocate
before warm-up. Stores occur after both wall and cycle timing have ended; no
marker events, timestamps or allocations are added inside `sim.Step`. Unsupported
platforms report null spans. The existing verification-only snapshot digest is
preserved. Whole-loop CPU/throughput includes post-step stores and remains a
separate supplementary metric.

The optional Python standard-library tool joins finalized owned WPR traces with
preinstalled tracerpt/xperf output. It verifies raw QPC clock/frequency, all-event
raw/normal timestamp mapping, loss counters, required schema versions, target
PID/TID lifetime, pinned CPU, switch chains and decoded event joins. It intersects
scheduled running intervals with each Step, subtracting the union of ISR, DPC
and TimerDPC intervals. It never converts execution cycles to milliseconds.
Unknown/lost/incomplete required evidence fails closed. Raw system traces and
private asset paths remain untracked.

The retained short scheduler probe matches independent xperf aggregate CPU
accounting exactly (210,585 microseconds). Twelve per-range comparisons differ
by 0.2–2.0 microseconds due to integer range boundaries/rounding. Its sanitized
numeric fixture covers busy and sleeping spans. The decoder explicitly reports
unrelated tracerpt processing errors; it does not claim every system provider
decoded successfully. Quantization bounds do not bound logging bias, firmware
interrupts or all-process/background CPU.

## Validation and retained experiment

All 48 portable parser/interval/protocol tests pass locally and on Windows.
They cover loss, clock/schema failures, target identity, nested interrupts,
TimerDPC, interval arithmetic, fixed schedules, bounded XML/gzip framing,
lossless archives, missing audits and failed observer/workload controls. The suite is added to build CI. Source review confirms timing endpoints
and the existing snapshot-digest path are preserved.

The predeclared [protocol](../../tools/movement-cpu/CPU-PROTOCOL.md) uses four
verification arms and 32 fixed traced/control timing arms, two Native/Momentum
ABBA cycles per map, 1,200 warm and 3,600 measured fixed-60-Hz ticks. Both treatments
use identical observer-enabled binaries. Trace/control wall and whole-loop CPU
perturbation must stay within 5%, and differential wall-ratio distortion within
1%, before CPU threshold inference. These are measurement-quality checks, not
changes to the feature budget. No automatic reruns, favorable-arm selection or
Mac simulation timing is allowed.

The first retained attempt builds its benchmark tool on actual main
`b05e74d418ca5a8c8eccb8ad570d99c3ad4f473a`; the normal production Core is the exact
previously verified current5d-plus-collision binary. Tool-only build passes with
zero warnings/errors. Four verification processes pass, followed by three actual
timing processes. Native's first traced arm accounts for all 3,600 Step spans.
Decoding the following Momentum trace fails, so the protocol stops with no CPU
threshold decision. All seven process reports and all traces remain retained.

The original decoder exhausts memory while retaining complete exports. A streaming
whole-document replacement also fails near the multi-gigabyte document boundary
despite low working set; both failures remain visible. The final decoder resets
its XML parser in bounded chunks at complete Event boundaries, streams all-event
raw/normal/xperf audits and retains only the measured scheduler window. It checks
root/prolog/truncation/size/gzip framing explicitly. The exact large saved trace now passes all 3,600 Step spans in 142.85 seconds
with 142 MiB peak working set, without running or replacing a simulation
measurement. Both earlier failures remain retained.

The Momentum trace has 2,613,581 events, including 1,499,701 context switches and
1,088,269 unused ReadyThread events. Its raw/normal exports occupy about 5 GB.
Available disk cannot retain that expansion for all 16 planned traces. The
second declared protocol therefore omits unused ReadyThread, captures ETLs before
any offline analysis, and audits/archives one trace at a time with resource guards
and verified lossless gzip. A late audit failure makes the whole batch inconclusive.
The benchmark C# and its binaries remain unchanged. These are instrumentation
repairs, not favorable gameplay reruns or a relaxed threshold.

See [retained numeric evidence](evidence/pr09-cpu-accounting.json) for original
source/binary hashes, all partial timing tails, allocation/workload data and
failure history. The [complete second experiment](pr09h-results.md) now retains
all 32 timing arms, 16 CPU audits and 48 verified archives. Its observer-quality
controls fail, so CPU-tail inference remains inconclusive. The operator
accepted the documented performance tradeoff on 2026-10-19 and deferred
optimization; this is separate from a measurement pass. Historical wall/cycle
failures retain their original source leases and scopes. No Mac simulation
timing is collected.

Concurrent [PR-759](https://github.com/AntiNotAnti/Project-Prime/pull/759) restores
a fixture-only replay-render failure hook. It is excluded from ordinary desktop
builds; its actual isolated Windows Core/fixture build and headless checks pass.
Actual graphical execution and hosted CI remain pending. PR-09 release acceptance
is incomplete, including performance and the documented balance/device gaps.
