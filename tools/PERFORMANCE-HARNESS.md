# CPU draw-work evidence and paired comparisons

`-perfcheck` runs the existing MapAudit scene, steps gameplay at 60 Hz and
batches the requested number of draw calls after each step. Reports explicitly
identify this as `MeasurementKind: batched-draw-cpu`. `PresentationHz` describes
the requested draw workload (60/120/144/240), not an observed display refresh
rate. Draw percentiles measure CPU service time around draw submission. The
historical `OnePercentLowFps` and `PointOnePercentLowFps` fields are inverse CPU
work-time proxies, not measured displayed FPS.

The harness does **not** measure actual presentation intervals, GPU completion,
input-to-photon latency, camera/gun phase error or thermal behavior. It does not
implement an extrapolation experiment or qualify high-refresh presentation.
Real display/device acceptance, actual pacing/GPU/input measurements and soak
results remain required for SH-07 activation.

## Capture

```sh
bash tools/run-perf-suite.sh /absolute/path/to/ProjectPrime perf-results 'MP3 PROVING GROUND'
```

Version 2 reports capture the loaded application image digest (the managed
assembly, or the executable for a single-file bundle), machine-name digest,
OS/process architecture/CPU count/GL identity, room-file source digests and
render settings once after initialization and before sampling. No source bytes,
local content paths or machine name are written to the report. This is not a
hash of the complete installation: managed dependency/native library changes,
external character assets, audio, shaders and other global assets may require
separate operator-controlled evidence. Room source identity covers the model,
collision and existing animation/texture/entity/node files. The machine digest
is a host witness, not hardware attestation; record actual hardware, drivers,
power mode and background workload with the captures.

Capture A and B with the exact same application image, sources, settings,
requested cadence, duration, player count, hunter selection and machine. Only
an explicitly isolated experimental condition may differ. This change does not
provide such a runtime experiment. A passing comparison means no regression
beyond the supplied CPU-work tolerance; it does not prove improvement or imply
feature acceptance.

## Compare

The existing cross-build mode retains its historical workload identity and
threshold policy:

```sh
python3 tools/compare-perf.py baseline.json current.json
```

Opt-in paired mode requires both reports' binary, hardware and workload metadata
and explicit measured variance limits:

```sh
python3 tools/compare-perf.py baseline.json candidate.json --paired-ab --tolerances measured-tolerances.json
```

The tolerance document has this contract:

- `Schema`: `1`; `MeasurementKind`: `batched-draw-cpu`.
- `ApplicationImageSha256`, `Hardware` and `Workload`: copied exactly from the
  matching baseline report.
- `ReportIdentity`: an object containing that report's `Room`, `Players`,
  `Width`, `Height`, `RenderScale`, `PresentationHz` and `Seconds`.
- `Evidence`: a nonempty reference to repeated same-machine baseline captures
  and the method used to choose the limits. A string cannot establish that the
  measurements occurred; retain and review the referenced evidence.
- `MaxRegressionFraction`: exactly these finite nonnegative fractional limits,
  each less than one: `DrawAverageMs`, `DrawP95Ms`, `DrawP99Ms`, `DrawP999Ms`,
  `SimulationP99Ms`, `AllocatedBytesPerDraw`, `OnePercentLowFps`,
  `PointOnePercentLowFps`. For example, `0.03` means 3% when measured variance
  justifies it; there is no default paired tolerance. A lower-is-better metric
  may grow by its fraction; a higher-is-better metric may fall by its fraction.
  Any allocation growth from a zero baseline fails.

Paired mode refuses missing, nonfinite, negative or malformed metrics and
identity mismatches. It refuses enabled texture/character replacement packs
because their bytes are outside the room-file source scope. A changed duration
or requested cadence also requires its own variance evidence. Exit codes are
`0` (within limits), `1` (regression), `2` (refused/unverifiable input).
`--no-fail` suppresses only a regression exit, never an evidence refusal.

```sh
python3 -m unittest discover -s tools -p test_compare_perf.py -v
bash -n tools/run-perf-suite.sh
```
