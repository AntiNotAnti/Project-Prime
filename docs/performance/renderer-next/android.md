# Android qualification, 2026-10-22

Windows-hosted validation of the isolated checkout based on
`46299080ba441970a6f32ce3acf74a8a65f1b7a6`, including the final R21/Parkour
correction and current upstream HUD, input, launcher, map and protocol changes.
No source repairs were needed in this lane. Main checkout was not built or edited.
The final integration requalification below supersedes the first pass's source
binding; earlier successful receipts remain snapshot-scoped evidence.

The linked `tools/android-pacing-check` PASS: 70 checks covering effective
cadence, failed/ignored requests, resume policy, completed-present intervals,
current production GLES source/hash synchronization, CRLF variants and binary
cache identity. [Receipt](android-pacing-current.log).
`python tools/android-perf-compare.py --self-test` PASS, including mixed v1/v2
negative controls. [Receipt](android-compare-self-test-current.log).

The Windows SDK is pinned to 10.0.401 through process-local `DOTNET_ROOT` and
`PATH`: `C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401`.
The installed Android SDK and actual Temurin JDK leaf are explicit:

```powershell
dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 `
  -p:AndroidSdkDirectory=C:\Users\Jarrett\AppData\Local\Android\Sdk `
  -p:JavaSdkDirectory=C:\Users\Jarrett\AppData\Local\Android\java17\jdk-17.0.20.1+1 `
  -v minimal
```

First, the same command with `-t:Compile
-p:MphReadRmlUiAndroidExperiment=true -p:RunAOTCompilation=false` PASS:
27 warnings / zero errors, 25.86 seconds. The generated intermediate DLL was
removed before compilation to force the optional source to compile. Its
compositor type is present, and its SHA-256 is captured in the
[UI proof](android-ui-proof.json); [compile log](android-ui-compile.log).
That compile is not optional native-UI packaging or device execution.

Next, the intermediate DLL was removed again and the normal command above ran
last with no enrollment, AOT or policy opt-outs. Full ARM64 AOT/package build
PASS: 27 warnings / zero errors, 106.41 seconds, 123 AOT libraries. The normal
assembly excludes the optional compositor type. Signed and unsigned APK sizes
and SHA-256 values are in the [build proof](android-build-proof.json), alongside
the normal assembly hash; [build log](android-arm64-build.log).
[Evaluated Release properties](android-enrollment.json) record the unchanged
enrollment, native audio rate, shader binary cache, 32m nursery and full AOT.
Warnings include existing XML documentation, Android overlay nullable
`ByteOrder`, upstream HUD obsolete `Watermark` and unused-field diagnostics.

Before and after both builds, 2,504 non-ignored source/project/resource inputs
under `src/` and `maps/`, plus root build configuration, had the same aggregate
SHA-256: `B8323DE077BA20487A8FCC6DEB676538E8BF33F9E1A2380D8E37FC83321C6F1A`.
The digest also matched between the optional and normal builds. Compact
[before](android-source-before.json) and [after](android-source-after.json)
receipts point to the full manifests retained outside the repository.

`artifacts/ktx-native-android` and `artifacts/rmlui-native-android` are absent
from this checkout. Their conditional packaging and device runtime remain
**NOT_QUALIFIED**; a successful APK build does not establish them. No ignored
native binary was assumed or added.

All executed tests ran on Windows. Android device/emulator execution was
**NOT_RUN**. Physical-device display cadence, ignored/denied frame-rate
requests, lifecycle/surface/context loss, UI state/fault restoration, shader
binary/source pixel parity, telemetry overhead and sustained thermal behavior
remain **UNVERIFIED**. Release enrollment and explicit experiment defaults are
unchanged; these receipts do not establish Android image/performance acceptance.

## Final integration requalification

The shared decode reservation/cancellation lifetime, checked capture regions,
OPAQUE KTX character refusal and foreground cleanup-latch removal were included
in a fresh serial pass. The first requalification UI compile PASS (27 warnings /
zero errors, 23.29 seconds), but the source guard stopped before the normal
build because Android restore added only an empty `net10.0/android-arm64` target
to `src/MphRead.CorpsePhysics/packages.lock.json`. No C# input changed.
The [restore guard receipt](android-restore-guard-requal.json), first optional
[compile log](android-ui-compile-requal.log) and [proof](android-ui-proof-requal.json)
are preserved. Dependency versions and resolved hashes were unchanged.

Using that post-restore baseline, the forced optional UI Compile PASS again:
27 warnings / zero errors, 23.55 seconds; compositor type present.
[Final optional log](android-ui-compile-requal2.log),
[final optional proof](android-ui-proof-requal2.json).
The normal enrolled ARM64 full AOT/APK command then ran last and PASS:
27 warnings / zero errors, 91.47 seconds. All 123 AOT libraries are present
in the signed APK; the normal assembly excludes the optional compositor.
[Final normal log](android-arm64-build-requal2.log),
[final normal/APK proof](android-build-proof-requal2.json),
[final evaluated enrollment](android-enrollment-requal2.json).

The full 2,505-input inventory matched before, between and after both final
builds: `EDA7FA76C3C9FE672D0EEDF37C6192D79587012AB552444BB87254A30B1873FC`.
[Final before](android-source-before-requal2.json) and
[final after](android-source-after-requal2.json) receipts identify the complete
external manifests. This inventory includes the temporary restore-normalized
lockfile. Root reconciliation may remove that empty generated target without
changing dependencies or C#; the separate
[C# identity proof](android-csharp-source-requal2.json) makes that distinction
explicit. The native Android artifact and device/performance gates above remain
unchanged. No app or tool source was edited by this requalification lane.
