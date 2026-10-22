# Standalone Android corpse physics pilot

Test-only APK **com.projectprime.corpsepilot**, separate from the production game. No game reference, renderer, extracted assets, network or storage permissions. Only `../../src/MphRead.CorpsePhysics` is referenced. Its original fifteen-box reference mannequin uses synthetic static geometry.

Both APK configurations resolve the same audited **Release** core library. Debug disables the SDK's network-capable managed debugger/profiler transport; explicit manifest debuggability exists only for private result extraction. The core's `THIRD-PARTY-NOTICES.md` is linked into APK assets.

The Activity runs the synchronous shared `CorpsePilot.PilotBenchmarks.Run(benchmarkDirectory, steps:600)` on one background owner thread. The source is linked from `../corpse-physics-pilot/PilotBenchmarks.cs`. Until that file exists, an explicitly labelled twenty-step smoke is compiled instead. No async operation enters a live backend/arena; construction, stepping, sampling and disposal stay on the worker. One invocation per app process prevents duplicate runs during Activity recreation.

Before that benchmark, linked `AffineBindingChecks.Run` and `HumanoidMapChecks.Run` execute their synthetic standalone contracts with a counted, thread-safe check delegate. A false check fails the run before benchmarking. Counts/status are persisted to `FilesDir/bridge-check-results.json` and logged as `BRIDGE`; this qualifies Android affine/map math only, not native rig assets or rendered skinning.

The shared runner writes `FilesDir/benchmark/benchmark.json`; the Activity copies that completed result to app-private `FilesDir/pilot-results.json`; logcat tag is `CorpsePilot`. Exceptions produce failed JSON where possible. Inspect the JSON's own assertions/status in addition to `DONE`. Force-stop/relaunch to repeat. This test APK is **explicitly debuggable even in Release**, solely to allow `adb -s <owned-target> exec-out run-as com.projectprime.corpsepilot cat files/pilot-results.json`; it is not a shipping application.

Release requests Mono broad AOT (`RunAOTCompilation=true`, `AndroidEnableProfiledAot=false`), ARM64 and embedded assemblies. This is not NativeAOT and does not assert that every method avoided JIT fallback. Retain AOT build output, `obj/Release/net10.0-android36.0/android-arm64/aot/` products and APK `lib/arm64-v8a/libaot-*.so` inventory to establish what compiled. Debug is a separate compatibility baseline.

Example build from repository root (use the installed JDK21 path rather than an unqualified shell JDK):

```sh
DOTNET_PROCESSOR_COUNT=2 /Users/jarrett/.dotnet/dotnet build \
  tools/corpse-physics-pilot-android/CorpsePilot.Android.csproj \
  -c Release -m:1 -p:UseSharedCompilation=false \
  -p:AndroidSdkDirectory=/Users/jarrett/Library/Android/sdk \
  -p:JavaSdkDirectory="$PWD/artifacts/custom-weapons/CW-05i/tooling/jdk21/jdk-21.0.12.1+1/Contents/Home"
```

Build/install/run evidence belongs under ignored `artifacts/custom-weapons/CW-09/android-pilot/`, including exact command/environment, hashes, elapsed times, AOT archive/manifest listing, target API/ABI/emulator status and extracted JSON/logs/screens. An emulator demonstrates packaging/execution correctness; it cannot qualify physical mobile CPU, heat, power, display cadence, native game integration or corpse visual quality. Use only the explicitly owned emulator port; do not operate another user's target.
