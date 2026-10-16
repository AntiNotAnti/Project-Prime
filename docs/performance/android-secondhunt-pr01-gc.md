# Android GC profiling / nursery experiment (PR-01)

Baseline stays unchanged. The SecondHunt Mono GC configuration is **not** blindly enabled
on Project Prime's .NET 10 Android builds.

## Run
Build identical Release APKs using no property (baseline) and
`-p:AndroidGcNurserySize=16m`, `32m`, and `64m`.
Only an explicit property packages `AndroidEnvironment` with the corresponding
`MONO_GC_PARAMS` settings. Use the same map, hunter, graphics cap/scale, ROM,
device, power state and build source for every run. Run one warmup match, then
record three >=5-minute matches and a transition/resume cycle per profile.

In `androidperf`, compare p50/p95/p99 frame time, over-20/33/50ms counts,
Gen0/Gen1/Gen2 collection deltas, total managed GC pause milliseconds,
ART GC count and ART GC time, allocations, thermal status, and memory pressure.
ART runtime counters may show `n/a` on devices that do not expose them.

## Acceptance
- No crash, missing GL context, out-of-memory, or AOT startup regression.
- A candidate must improve frame-time tails across repeated runs, not merely average FPS.
- Reject any profile with unacceptable RSS growth or worse GC pauses under memory pressure.
- Validate on API 24+ and high-refresh phones; record device and Android version.
- Run `dotnet run --project tools/android-pacing-check -c Release`.

Rollback: omit `AndroidGcNurserySize` (the production/default configuration is untouched).
Do not turn a selected value on by default until device evidence supports it.
