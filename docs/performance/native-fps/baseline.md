# Native FPS baseline — October 19, 2026

FPS-00 audits fresh `origin/main` c0e7e01de, rather than the plan's older
a3a73617 snapshot. No open PRs were returned at execution start. The primary
checkout has unrelated UI/network/gameplay edits and is intentionally untouched.
This program uses its own managed worktree. The attached plan supplies the
technical scope; the user's request explicitly overrides waiting for PR review/CI.

## Evidence and limitations

[baseline.json](baseline.json) and [macos-manifest.json](macos-manifest.json)
retain machine, source, workload, exclusions and missing measurements. Native
assets are present. A separate chat is actively running a seven-Hunter native
presentation matrix on this Mac. Concurrent native captures/builds invalidate
performance comparisons. Consequently no uncontended A/A, ABBA, 10–15 minute
qualification or 30-minute soak has been accepted. Metrics remain null, not zero.
Windows/Linux/Android native devices and physical input-to-photon instruments
are not attached to this execution. Prior CPP-03/04 evidence is preserved but
cannot substitute for measurements of this binary or prove physical scanout.

## Source audit

- Fixed simulation remains 60 Hz (`FrameTiming`). The native harness uses the
  actual render loop and SwapBuffers. `-perfcheck` excludes presentation.
- Numeric caps use OpenTK pacing with VSync off, even at monitor refresh.
  Display requests VSync with Linux/macOS ignored-interval fallback; Unlimited
  disables both. The older numeric-240 overshoot policy is already corrected.
- `OpenGlFrameProfiler` emits 240-frame `[glframe-json]` schema 1 windows.
  Its `onePercentLowEstimateFps` is reciprocal p99, not a true tail mean.
  Render CPU, swap wall time and intervals have separate distributions.
- `OpenGlPassProfiler` owns inclusive (potentially nested) pass timers and
  allocations/upload counters. Do not sum those scopes as disjoint work.
- All default optimization names are recorded in `baseline.json`. Existing
  retained geometry, binding cache, batches and HUD caches stay enabled under
  their existing capability policies. PALETTE_PREP stays off; PROCEDURAL_STREAM
  remains opt-in. Their negative experiments are not repeated unchanged.
- LowLatencyController installs the null provider. Its InputSample marker labels
  the fixed-step boundary, omits draw-only samples and precedes late aim polling.
  It is neither an actual final hardware-sample timestamp nor Reflex evidence.
- Confirmed pacing-source defect: ApplyFrameRateSettings can classify the prior
  cap/monitor's MeasuredFrameHz as evidence that newly selected Display ignores
  VSync. FPS-02 must test measurement ownership rather than replace the pacer.

## Bottleneck selection

No measured >5% owner is established. FPS-03/04/05 are evidence-gated and remain
NOT_RUN; no renderer candidate or new default is justified. The confirmed
pacing-source correctness defect and missing bounded diagnostics are actionable
without asserting a speedup. Unavailable devices remain NOT_RUN, GPU timing
UNVERIFIED. See the final [decision](decision.md) for each program slice.

## Repeatable measurement

Build `tools/native-render-check` once with the pinned SDK; freeze the complete
output before measurements. Use fresh asset-only `PROJECT_PRIME_USER_DATA` for
each process, `-room 'MP3 PROVING GROUND' -players 8 -hz -1 -seconds 120`,
1920x1080, Original and Off/High shadows. The first 240 simulation ticks are
excluded. Run at least five valid A/A or ABBA runs per arm; short 120-second
runs are diagnostic, not 10–15 minute qualification. Do not overlap builds,
copies or captures. Keep every run, its process exit code, observedWorkload
binary/content bindings and raw chronological frameIntervalsMs. A nonzero
exit fails a run. Record cap, actual panel Hz, driver VSync/VRR, window mode,
focus, every GL override and fixed-step drops. Use the bounded capture added
by FPS-01 for longer sessions and correlated attribution. Physical refresh,
competitive networking and input-to-photon tests are separate gates.
