# Android Release optimization enrollment

Project Prime Android uses the **default-enrolled** performance profile in
Release builds. Opt-in means build features are selected, not that real-device
speedups have been proved or all devices support the features.

## Build matrix

| Property | Enrolled Release | Baseline / override |
| --- | --- | --- |
| AndroidPerfOptIn | true | false disables enrolled defaults |
| AndroidFullAot | true (Mono only) | false enables SDK profiled AOT fallback |
| AndroidEnableProfiledAot | false when broader AOT selected | true uses the SDK profile |
| AndroidShaderBinaryCache | true | false disables program-binary storage |
| AndroidNativeAudioRate | true | false restores 32728 Hz output request |
| AndroidGcNurserySize | 32m | profile-disabled leaves SDK default; explicit 16m/32m/64m supported |

The project intentionally does **not** select `PublishAot` (NativeAOT).
It uses Mono's `RunAOTCompilation` when the supported Release configuration
can compile the game. Project command-line properties take precedence.

## Commands

Enrolled Release:

```sh
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release
```

Baseline:

```sh
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -p:AndroidPerfOptIn=false
```

Targeted fallbacks:

```sh
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -p:AndroidFullAot=false -p:AndroidEnableProfiledAot=true
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -p:AndroidShaderBinaryCache=false -p:AndroidNativeAudioRate=false -p:AndroidGcNurserySize=16m
```

## Release acceptance

Before promoting to a distributed APK, compare same-source baseline and
candidate on real Android 8+, Adreno and Mali, a high-refresh phone and
one memory-constrained phone. Track first map spawn and first projectile time;
30-minute match and pause/resume; audio pitch/latency; executable size and
RSS; managed/ART pauses; p50/p95/p99 frame time; shader cold/warm start,
context recreation and driver upgrade. Verify the startup screen and app
opening after an actual Release installation, not only an emulator compile.

When one experiment fails, disable **only** that property until the issue
is corrected. The enrolled default is intended to include future safely
gated optimizations, without bypassing the required capability and regression
checks. This document is not a hardware test report.
