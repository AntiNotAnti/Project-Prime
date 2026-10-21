# Android renderer implementation disposition

2026-10-21; source baseline `9885b151cd99e598b48871d8c7cd615d7979130b`.
All executed checks in this lane use native Windows PowerShell, Windows Python
and the Windows .NET SDK. No Android device/emulator test was executed.

| Package | Implementation | Evidence disposition |
|---|---|---|
| R03 | Explicit unset/success/retry request state; active display identity/rate callbacks; successful swap interval required before surrendering software cap; surface generations invalidate requests | Windows deterministic policy PASS; physical cadence NOT_RUN |
| R04 | Generation-aware interruptible 50–1600 ms EGL retry with a fresh delay budget on replacement generation; two-second diagnostic suppression; failed presents cancel profiling/latency completion and increment separate failure counters; context loss terminates old graphics owner with restart explanation | Retry policy PASS; rotation/background/context-loss hardware NOT_RUN |
| R06 | One production `PrepareSource` used by compile and binary identity; translated variant text, schema, translator, link ABI and driver identity; bounded binary reads and 64-entry/64-MiB disk pruning | Windows actual production translation/key PASS; corrupt driver binary recovery and image parity NOT_RUN |
| R07 | Unit zero selected before initialization/resize; independent front/back stencil restoration; temporary program/quad, framebuffer, geometry and texture owners; failure injection stages | Source implementation complete; native state/failure-injection acceptance NOT_RUN |
| R18 | One preallocated detached report slot; single-flight worker owns sorting/formatting/Binder/runtime probes; skips counted when busy; stale match reports discarded; v2 completion-to-completion intervals align current stages | Windows long/short paired completion regressions PASS; sustained native overhead/allocation comparison UNVERIFIED |

The source-confirmed findings remain applicable at the working baseline.
A focused Windows shader regression additionally found that CRLF verbatim shader
literals crashed `Shaders` initialization in the depth specialization transform.
The first production shader harness run failed with
`TypeInitializationException` / `Depth vertex body boundary changed` before
this repair. The transform now normalizes line endings; the harness explicitly supplies CRLF
input as a negative-control regression. `EsShaders.CheckInSync` publishes its
ready flag only after every hash check passes, so an optional binary-cache miss
cannot accidentally suppress a subsequent source synchronization failure.

No enrollment/default, render scale, user cap, fixed simulation, gameplay,
networking, replay or automatic quality policy was changed. A successful interval
request and observed refresh establish the presentation policy assumption; they
do not establish physical display cadence or input-to-photon acceptance.

## Commands executed

Pinned Windows SDK path:
`C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401\dotnet.exe`.
Each .NET command used process-local `DOTNET_ROOT` and prepended that directory to
`PATH`; the repository `global.json` pin remains unchanged.

- `dotnet run --project tools/android-pacing-check -c Release`: PASS, 70 checks,
  including actual production GLES preparation, variant/key invalidation,
  CRLF/LF parity, first/resumed Unlimited request, failed request retry,
  denied/ignored/unknown refresh fallback and successful interval ownership.
  Display with a failed swap interval uses observed refresh; absent a valid
  observation it uses a conservative 60 Hz deadline. Surface resume reset is
  consumed after binding so the first replacement frame excludes rebind time.
  Receipt: [android-pacing-final.log](android-pacing-final.log).
  Earlier focused 68-check receipt remains [android-pacing.log](android-pacing.log);
  final integration also exercises the new optional shadow GLES source identity.
- `python tools/android-perf-compare.py --self-test`: PASS.
  Receipt: [android-compare-self-test-final.log](android-compare-self-test-final.log).
- Initial `dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release`:
  FAIL `NETSDK1147` (Android workload absent). User-local prerequisite repaired
  with `dotnet workload install android --skip-manifest-update --disable-parallel`:
  PASS; no device commands executed.
- Android build with SDK/JDK paths but no runtime override: FAIL `NU1102`,
  attempting to restore `Microsoft.NETCore.App.Runtime.Mono.win-x64` 10.0.12.
  Receipt: [android-build.log](android-build.log). Explicit Android runtime retry
  is recorded separately below.

- `dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 -p:AndroidSdkDirectory=C:\Users\Jarrett\AppData\Local\Android\Sdk -p:JavaSdkDirectory=C:\Users\Jarrett\AppData\Local\Android\java17 -v minimal`:
  PASS, full Windows-hosted ARM64 AOT/package build, 26 warnings / zero errors,
  2m03.60s. Receipt: [android-build-arm64.log](android-build-arm64.log).
- Same command plus `-p:MphReadRmlUiAndroidExperiment=true -p:RunAOTCompilation=false`:
  PASS, optional UI sources compiled and packaged, 27 warnings / zero errors,
  1m14.83s. Receipt: [android-build-ui-experiment.log](android-build-ui-experiment.log).
  The additional nullable `ByteOrder` warning was then corrected.
- Same experiment command plus `-t:Compile`: final integrated source compile
  check after effective-refresh/resume/cleanup review fixes, recorded in
  [android-compile-ui-final.log](android-compile-ui-final.log). This compile scope
  includes R07; it is not a device, image or performance qualification.
  The generated intermediate Android DLL was deliberately removed first so the
  optional C# source could not be skipped by incremental target checks. Final
  optional Compile PASS: 26 existing warnings / zero errors, 24.97s.
  [android-ui-compile-proof.json](android-ui-compile-proof.json) records its SHA-256
  and presence of the optional compositor type. The intermediate DLL was then
  regenerated under the normal enrolled configuration for the final full build.
  A subsequent integration compile caught ambiguous shared `ErrorCode` names
  while the root lane was editing readback checks; that negative receipt is
  retained in [android-compile-errorcode-negative.log](android-compile-errorcode-negative.log).
  The root lane qualified those enums before final integration.

Final frozen-source normal enrolled ARM64 command (same SDK/JDK/runtime arguments,
no enrollment/AOT/policy opt-outs) **PASS**, 26 existing warnings / zero errors,
1m30.50s. Receipt: [android-build-arm64-final.log](android-build-arm64-final.log),
[assembly proof](android-build-proof.json). This includes the final pause-map,
program-bundle allocation, promotion accounting, GC baseline and new-generation
retry review repairs. The normal artifact excludes the optional UI compositor;
its separate final Compile artifact includes it, as the proof receipts show.
The prior successful pre-review AOT builds remain separate historical receipts.

## Telemetry v2 semantics

Frame intervals now span consecutive successful swap-return timestamps. The
limiter/simulation/render/UI/swap stages stored alongside each interval belong
to the current completing frame. The first completion after match/surface
rebasing establishes an anchor and contributes no fabricated zero sample.
Failed presents use a separate error counter and do not complete profiler or
latency frames. Optional native launcher/loading swaps use the same failure
classifier as gameplay. `androidperf` lines declare `telemetry v2
completion-interval`; the comparator rejects mixed v1/v2 runs and cross-version
comparisons instead of combining incompatible populations. Window percentile
labels and the comparator's median-window-p99 meaning remain unchanged. GC
counter deltas and subsequent baselines use each same captured count, avoiding
a missed collection between diagnostic reads.

## Qualification still required

Physical API 24–29 and API 30+ devices: observed 60/90/120 modes, ignored/denied
requests, stable selected cap across display changes, Unlimited first/resume,
rapid surface replacement, background/foreground, stop while retrying and context
loss. Unit-zero UI initialization/resize must enter with unrelated nonzero unit
texture, sampler, front/back stencil and separate framebuffer state; compare
exact restoration and injected vertex/fragment/link/quad/texture/surface failure
ownership. Compare source/binary cache images and corrupted/truncated/old-driver
fallback. Verify telemetry report distribution, skipped reports, allocations and
sustained overhead under unchanged enrollment/cap/scale. These remain NOT_RUN or
UNVERIFIED, never platform/performance PASS.
