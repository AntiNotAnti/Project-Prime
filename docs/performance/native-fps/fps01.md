# FPS-01: bounded frame attribution

Hypothesis: existing 240-frame aggregate windows cannot identify long-session
tails or correlate simulation, inclusive GL CPU scopes, UI, upload, GC and swap
within the same completed application interval. This is a diagnostics change;
no speedup or rollout eligibility is claimed.

The existing frame/pass profiler now accepts opt-in bounded value samples.
`PROJECT_PRIME_GL_FRAME_CAPTURE` selects a fresh output path, with capacity
controlled by `PROJECT_PRIME_GL_FRAME_CAPACITY` (262,144 default; 1,048,576 max).
Only process scene/window shutdown serializes the capture. Legacy schema-1
streams retain their fields and behavior outside capture; capture mode suppresses
their window logging. Schema 2 adds chronological samples and true slowest-1%-mean
FPS while preserving the reciprocal-p99 estimate under its original field name.

Simulation and input-poll CPU timings surround the existing owners; there are
no extra polls or simulation steps. The software sample-call entry timestamp
never claims hardware event time, Reflex, scanout or photon latency. Scheduler
and uninstrumented gaps are explicitly unknown. CPU pass scopes are inclusive,
not additive. Only a >=60% observed dominant scope is labelled as primary; GC
coincidence stays unknown/mixed. Actual GPU, streaming I/O and shader execution
cannot be inferred from CPU wall time. Native GPU timers remain separate.

Scene identity, focus/minimize, cap, presentation generation, skipped render,
replay seek, failed swap and context resets break interval continuity. Samples
already completed remain bounded and chronological. Ending the scene stops the
capture; a subsequent scene requires a new process/path. No static capture state
retains scenes or GL resources. Buffer overflow reports droppedRecords and
invalidates whole-run performance qualification. Export preserves existing files.

Validation: `tools/frame-capture-check` links production storage and verifies
partial windows, nearest-rank percentiles, p99.9 sample minimum, true tail mean,
budget thresholds, timer ordering/overflow, bounds/drop/cancel accounting,
chronological JSON/legacy metric and no overwrite. Warmed disabled/Add/full
storage loops allocate zero bytes. This is a storage-seam assertion only;
end-to-end enabled/disabled overhead remains UNVERIFIED pending uncontended A/A.
Native smoke receipts and final check results are in `validation.json`.

Rollback: unset both capture variables to restore existing profiling behavior;
revert this PR to remove the diagnostic seams. There is no settings-schema,
gameplay, network/replay format, rendering-default or GL ownership change.
