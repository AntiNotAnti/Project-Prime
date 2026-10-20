# Fixed Windows CPU measurement protocol

Freeze the source SHA/overlay manifest and Core/tool/runtimeconfig hashes before
starting. Both treatments use the same binary and post-Step timestamp stores.
The feature threshold remains 5%; no earlier failure is relabelled or waived.

## Fixed schedule and controls

Use four verification-only processes (Native/Momentum on Proving Ground and
Sanctorus), then 32 timing processes: two maps × two Native/Momentum ABBA cycles
× four model positions × two treatments. Each has 1,200 genuine warm ticks and
3,600 measured ticks in a fresh eight-bot authority process. Adjacent trace/control
pairs alternate order by position and reverse in the next cycle. No rerolls,
replacement arms, discarded outliers or model-count normalization are allowed.

Both treatments pin the simulation thread to the lowest allowed CPU and enable
identical thread-cycle and Step-span observers. Disable tiered compilation,
dynamic PGO and ReadyToRun; pin Shadow shot proof and accepted-shot playback.
Gameplay/evidence/NCX diagnostics remain off during timing. Never convert cycles
to CPU milliseconds. Sources, binary hashes, runtime/affinity settings, workload
hashes and numeric ambient load are retained. Require ambient CPU <=5% before
each unopened map and preserve every completed arm if the precondition fails.

Before capture, inspect WPR/logman inventory and release other performance or
functional-test leases. Require idle WPR. Each traced process gets a unique owned
instance, started before process birth and stopped after exit. Stop only after a
successful owned start; no shared stop/cancel, force or user-process termination.
The profile records lifecycle, context switches, DPC and interrupts. ReadyThread
is unused by this accounting and is omitted in the second protocol.

## Capture first, audit afterwards

The first protocol stopped after two completed arms when decoding the third
arm exhausted the continuous XML parser. Its traces, all timing data and both
parser failures remain retained. They do not constitute a complete CPU comparison.

The second protocol keeps the same fixed schedule but separates measurement from
export/analysis. During capture, retain ETLs and benchmark JSON only. No XML export,
CPU decoding or compression runs between timing arms. Start only with >=8 GiB
free and check that reserve before each traced arm. Record final hashes and an
explicit capture-complete/pending-audit status; capture alone cannot pass a gate.

After all 32 captures, release the performance lease. A separate offline command
processes one ETL at a time with >=12 GiB free scratch, an 8 GiB export limit and
2 GiB emergency reserve. Export raw/normal XML and xperf data, then decode all
events with bounded XML chunks and streaming joins. Keep the ETL and lossless gzip
exports. Verify original/decompressed SHA-256, size and gzip CRC before removing
only redundant scratch owned by that analysis. Prior failed-protocol files stay
untouched. The gzip reader also checks complete framing/CRC when used directly.

Any late loss/schema/clock/identity/decode/control failure makes the whole new
batch inconclusive. All captures remain visible; no favorable arms are selected.
There are no automatic retries. A justified subsequent experiment needs its own
recorded protocol and failure history. Offline processing may coexist with
correctness work after capture; no new performance measurement occurs then.

## CPU accounting and interpretation

Validate finalized QPC clock/frequency, all-event raw/normal time mapping, loss
counters, required schemas, complete target PID/TID lifecycle, pinned measured
window, scheduler chains and independent xperf joins. Unknown required evidence
fails closed. Intersect each Step's raw QPC span with scheduled target-thread
running intervals and subtract the union of ISR/DPC/TimerDPC intervals. This is
simulation-thread CPU, excluding waiting and those interrupts; it does not measure
all-process/background CPU or unperturbed hardware truth. Preserve whole-loop
process CPU separately.

Measurement-quality checks, declared before collection:

- Each map/model's traced/control median wall p95 and whole-loop process CPU
  differ by no more than 5% in absolute relative terms.
- Each map's traced Momentum/Native wall-p95 ratio divided by the control ratio
  lies in [0.99, 1.01], limiting observed differential tracing perturbation.
- Every within-map/model workload hash matches exactly across arms/treatments.
- Every sample's numerical boundary bound is below 15 microseconds. Keep lower
  and upper CPU arrays; the bound covers quantization, not logging bias.

Failure of a control is inconclusive, never PASS. These checks do not alter the
5% feature budget. For each map/model, aggregate the median of four traced-arm
CPU p95 values. Report Momentum/Native−1 and the conservative ratio of Momentum
upper-p95 to Native lower-p95. Only an upper increase <=5% with all controls
passing is within this CPU criterion. Preserve CPU p50/p99/worst and wall controls.
Cross-model AI/visibility/combat work differs; within-model repeatability is not
identical cross-model workload. Final feature acceptance includes the other gates.

`run_cpu_protocol.py` defaults to plan-only and requires
`--execute-after-lease-release` for capture. After capture, run
`analyze_cpu_protocol.py --batch BATCH --xperf XPERF`. Raw traces contain system
metadata and stay untracked; publish sanitized numeric evidence only. See
[the results](../../docs/movement/pr09f-results.md) for exact source leases/status.
