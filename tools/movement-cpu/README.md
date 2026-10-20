# Windows simulation-thread CPU accounting

Test-only tooling; disabled during ordinary gameplay. The benchmark exports
existing Step timestamps only when `PRIME_BENCHMARK_STEP_SPANS=1`. Inputs:

- benchmark JSON with `stepSpans` schema1 and opt-in pinned `threadCycles` metadata;
- one owned finalized WPR ETL exported twice by tracerpt: raw `-rts` XML and normal XML (plain or lossless gzip);
- the SAME ETL decoded with installed xperf `-a dumper` (integer-us timestamps).

Example offline invocation:

```
python etw_step_cpu.py --raw raw.xml --normal normal.xml --dump dump.txt --benchmark run.json --output cpu-accounting.json
python -m unittest discover -s . -p 'test_*.py'
```

On the validated Windows26200 / xperf10.0.26100 installation, raw headers use QPC
clock1, frequency10MHz. ALL raw/normal timestamps must verify a single exact
FILETIME−QPC offset before inverse-converting TDH interrupt InitialTime. Other
frequencies fail closed rather than assume conversion. xperf decodes CSwitchv5,
ThreadStart/Endv3, DPC68v2, TimerDPC69v2, ISR67v2 and ISR-MSI50v2. Each required
raw event joins1:1 with decoded rows by ordered timestamp and CPU (where supplied).
Thread lifecycle maps the target nativeTID to benchmarkPID; all CPU switch chains
must be continuous. Target migration is forbidden within measured-span envelope;
setup migration before pin is valid. Pinning remains the benchmark's responsibility.

Required providers have an independently checked xperf decoding path. This does
NOT claim tracerpt decoded all providers: its processing-error total and opaque
required switch count remain explicit output audit fields. Unrelated rundown
errors do not silently disappear and do not invent missing scheduler fields.
Lost/missing buffer/event counters, unknown needed versions, unmatched rows,
clock failures, ambiguous lifecycle, broken switch chains and invalid spans fail.
No cycles-to-time conversion occurs.

CPU definition: intersect each benchmark QPC span with this thread's scheduled
intervals, subtract the union of overlapping ISR/DPC/TimerDPC intervals on the
same CPU. Nested interrupts are unioned before subtraction. Output keeps wall,
scheduled, excluded interrupt and resulting CPU milliseconds separately. The
numerical uncertainty envelope counts relevant raw100ns boundary endpoints and
rejects a sample at>=15us numerical uncertainty. It is not a bound on logging bias.

Real short-probe validation:6102 switch joins,1554 interrupt joins,22290 identical
clock offsets, zero loss, all-CPU whole-thread210585.0us equals independent xperf
aggregate210585us exactly. Twelve xperf range checks differ+0.2..+2.0us due to
outward integer-us range boundaries/report rounding, within conservative[-1,+3]us.
`probe-target-numeric.json` retains only shifted target intervals: IDs remapped,
non-target TIDs collapsed0, no process names/kernel addresses/rawETL/assets.
The full original evidence and9411 tracerpt processing errors remain ignored.

Before future acceptance data: use owned uniquely named WPR instance only after
other workers release the machine; receipt successful start before any owned stop;
no bare cancel/stop or shared session changes. `-instancename` must be LAST in
WPR commands. Inspect existing kernel sessions before start. Keep failure traces.
Run identical tracing settings for Native/Momentum, retain untraced controls to
quantify tracing overhead/drift, record ambient load, sources/hashes/runtime
settings and actual workload hashes/counts. Per-step scheduler accounting is the
standard ETW scheduled-thread CPU definition. Scheduler logging location, SMI/
hypervisor time and observer perturbation are practical documented biases, not
reasons to demand an impossible unperturbed oracle. A useful acceptance protocol
must bound observed trace/control perturbation and report ratio uncertainty; it
must not relabel old wall/cycle failures or discard unmatched combat workload.
The provisional5% requirement is unchanged. See `CPU-PROTOCOL.md` and the PR-09 results for the retained measurement
status. The adapter alone does not establish gameplay acceptance.

The adapter resets the XML parser at complete Event boundaries in bounded chunks,
audits every raw/normal event in lockstep, and retains only the measured scheduler
window. This avoids retaining multi-gigabyte exports or using one continuous XML
parser for the whole document. Unknown framing, oversized events, truncation and
gzip corruption fail closed. Capture and disk-bounded offline analysis are separate;
see `CPU-PROTOCOL.md`.
