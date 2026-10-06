# Render / Android / music implementation status

The implementation is on `codex/engineering-roadmap`. The original working checkout was inspected read-only. Native evidence covers Apple M4 Pro Metal and desktop OpenGL; Windows DX12/Vulkan, Linux Vulkan and physical Android Vulkan/GLES remain hardware validation requirements.

| Finding | Implementation | Evidence / limits |
|---|---|---|
| B21 Android scene leases | Normal/error/shutdown paths release CPU and GPU scene ownership before context destruction | Client/Android compiled; deterministic cleanup failure tests pass; physical match cycles pending |
| B22 Android surface ownership | Persistent loading EGL pbuffer; validated holder binding under callback monitor; destruction waits for actual native detach | Android compiled; physical rotation/background/load fault checks pending |
| B23 atlas reclaim | Coalesced page ranges, release/reuse, queued-command flush before return, empty-page removal and failed-upload rollback | 20,000 seeded bounded/shared churn operations pass; native DX12/Vulkan atlas exercise pending |
| B24 odd HiZ | Proportional overlapping footprint preserves odd far edges with native floor mip extents | Edge/depth propagation tests pass; all visibility/HiZ WGSL compiles on Metal |
| B25 raw mouse | Supported GLFW raw motion follows grabbed relative input; UI/absolute modes disable it | Desktop compiled; physical mouse/stylus transition checks pending |
| B26 authored RGBA mips | Stable narrow external runtime snapshot integrated through targeted hunks; explicit modern/GL mip upload, capped authored suffixes, complete-chain sampling | Decoder fixtures pass; 93 exact uploaded levels on each of Metal/OpenGL; normal mip identity survives actual device recovery |
| B27 telemetry | Logical native texture/buffer capacity includes PBR/indirect/atlas/visibility/HiZ/pending owners; live atlas units and coverage labels exposed | Exact capacity/delete/pending assertions and 120-cycle native lifetime checks pass; figures are logical tracked capacity, not physical VRAM |
| B28 Android failed load | Partial factories and loop finalization retain idempotent scene ownership | Deterministic CPU/GPU cleanup-error tests pass; physical fault injection pending |
| B29 stale temporal occlusion | History rejection disabled until trustworthy static occluder provenance exists; current frustum/indirect path retained | Safety gate passes; motion visibility requires DX12/Vulkan hardware |
| B30 failed model textures | Failed compilation rolls back only unique new admissions/pins; missing required albedo aborts | Failed/committed admission, pin and alias uniqueness tests pass |
| B44 music | Serialized latest-request constructors; private bundles, rollback, newest-only publication, cancellation and complete shutdown drain; Load removes previous track before decode | Controlled stale/cancel/fault/shutdown tests pass; final client/Android builds include the Load refinement |
| P10 upload/packet overhead | Upload accounting measured by owner category; no unmeasured packet rewrite | Two short Metal diagnostics recorded; no before/after speedup claim |
| P12 callback pump | Iteration, elapsed and completion diagnostics added; established behavior retained | Tested Metal callbacks were inline; delayed behavior on other drivers remains unverified |

Simulation `Scene.FrameTime` remains fixed at 60 Hz. Root owns B50 frame/benchmark labels and production phase timestamps.

## B26 source provenance and integration

The read-only original source snapshot was fingerprinted at **2026-10-06 05:19:40 UTC** and remained identical across three reads. The external task continued HD geometry/asset regression work, while the narrow mip runtime files were stable. Subsequent authorization permitted integration of this complete runtime snapshot.

Only five tracked diffs were applied as reviewed hunks (`Ktx2TextureAsset`, `TextureAssetManager`, `ModernGraphicsResourceState`, `ModernGraphicsCompat` and `TextureReplacementPack`). The two new authored-mip implementation files were imported in full. No unrelated HD geometry, UI, live pack or acceptance edits were copied. Existing scene texture transactions, native accounting, atlas reuse, Android lifetime and root frame trace hunks were retained.

| Original snapshot file (under `src/MphRead/Mods/Render/`) | SHA-256 |
|---|---|
| `RgbaMipTextureAsset.cs` | `c2cb3e2612784228b07b717023ffd28c06b1b6b86a2dfd4088c3a62ef12f9936` |
| `ModernGraphicsCompat.RgbaMipTextures.cs` | `e60e8d861f4d7b4ebc9b99f79fb4eaf1d2574421d4535267e19ae008194eaaca` |
| `Ktx2TextureAsset.cs` | `30810d9746b9aaf069f89a685f2b6fa54b4a255a1d967cff93336055c492bf8e` |
| `TextureAssetManager.cs` | `dd5c8e0eb01d38935d76550de50e8f48aa37b36903836a5ca46fc880209f56ba` |
| `ModernGraphicsResourceState.cs` | `8e01db6937e485d43d3fb7fbdbfd497f77a30a83b8444f466fa847e36ab4f4ad` |
| `ModernGraphicsCompat.cs` | `34aa9d50ffdfc1c4a0bdff241da2329c7dede7f73e271f02dc9d8bad585fcf9f` |
| `TextureReplacementPack.cs` | `d7003b2cfd8cbdb76f19b7dabd9e1c237f44ff21c9b6829ed9566cb09834fb1f` |

The encoded character albedo and RuntimeEncoded normal/material/emissive paths reach the existing scene-owned TextureAssetManager. Class, channel, quality and sampler remain part of cache identity. CharacterModelRuntime currently classifies alt/turret parts as Hunter and viewmodels as Weapon; the preservation policy includes Hunter, Weapon, AlternateForm and Turret, so existing callers and explicit class callers preserve authored levels.

The content-free `tools/authored-mip-check` links the production decoder/types. It checks five shapes (odd, tall, one-wide, square, one texel), all four character classes, all four material channels and caps 8192/3/1. Distinct authored normal/channel bytes, capped suffix selection, complete/partial sampling, invalid level layouts and admitted byte sums are verified. Native desktop checks upload through the production manager and verify exact per-level GPU bytes, base-only and partial sampling, cache admission/release, upload counters, native capacity and normal-chain restoration after device recovery. PNG/generated and compressed paths keep their established routes.

Linux/Windows native KTX is supplied by Ktx2.NET 1.0.5. The tool's output/deps include `runtimes/linux-x64/native/libktx.so` with native RID selection and the required encoder/decoder entry points. Its ELF dependencies are standard libc/libm/libgcc/libstdc++, requiring GLIBC 2.38 and GLIBCXX 3.4.32. The current [GitHub runner image matrix](https://github.com/actions/runner-images) maps ubuntu-latest to Ubuntu 24.04, whose [toolchain inventory](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md) includes GCC 13/14; no extra KTX install is required there. This is dependency/ABI review evidence, not a local Linux execution PASS. macOS copies the repository's built pinned libktx to the tool output. The decoder check is included in the fast contract script.

## Validation and measured diagnostics

- Render/audio ownership tool: PASS, including cleanup/report faults and teardown cancellation.
- B26 decoder tool: PASS (`/tmp/prime-b26-decoder-check.log`).
- B26 Release client build: PASS, 98 warnings / 0 errors, 44.03 s (`/tmp/prime-b26-client-build.log`).
- Later combined Release client build including accepted-fire seams and P04 replay changes: PASS, 0 errors (`/tmp/prime-p04-client-build.log`).
- B26 native Metal + fresh OpenGL: PASS (`/tmp/prime-b26-metal-gl-parity.log`), 93 exact uploaded levels per API, normal-chain device recovery, capacity/upload/cache/delete accounting, full existing shader suite, staged large texture promotion, 120-cycle resize/resource lifetime and restart checks.
- Final combined Android explicit android-arm64 RID restore + Compile: PASS, 114 warnings / 0 errors, 25.24 s (`/tmp/prime-b26-android-build.log`), including B26, accepted-fire phase/travel/component hooks and the preceding P04 client-source snapshot. Java 27 version detection remains an environment warning; this Compile target does not execute a physical Android device or produce release-signing evidence.
- Native test runtime binaries were reused from existing ignored artifacts only. KTX runtime SHA-256: `b51cabe670cfe679d09c25e2eca4e1a0a7d4e379496cd38c34ae6aa4e3ead837`. No proprietary assets or native binaries were committed.

The short Metal UNIT1_CX diagnostic recorded 9 retained packets, 2 uniform templates and 295,932 buffer-write bytes/frame. Combat Hall recorded 149 packets, 2 uniform templates and 1,897,188 buffer-write bytes/frame. The final Combat Hall run with corrected throughput/interval labels reconciles owner-category sums exactly across six groups: transient geometry 37,764 B/frame, uniform 701,696 B/frame, retained world uniform 1,124,960 B/frame and texture staging 32,768 B/frame. Other/PBR/indirect/GPU-visibility/atlas were zero on this direct Metal path. Buffer-write submission measured 0.379–0.440 ms/frame. These 30-sample groups are smoke diagnostics, not a controlled before/after comparison or a credible presented 1% low measurement.

The Metal adapter/device callbacks were inline in one offscreen probe (zero pump iterations; 0.032/0.019 ms). This does not establish delayed-callback correctness or latency on other drivers.

## Production frame trace evidence and limits

The existing `-shellshot` CLI exercised real RenderWindow.Run/LowLatencyController production frames with isolated user data and opt-in PROJECT_PRIME_FRAME_TRACE, without a diagnostic engine rewrite. The trace recorded 562 complete frames, zero cancelled frames, nonzero input/simulation timestamps on 401 frames, network on 377 frames and preparation/render/acquire/present on all 562 frames.

| Measured CPU/present-return metric | p50 | p95 | p99 |
|---|---:|---:|---:|
| Consecutive present-return interval (561 intervals) | 11.959834 ms | 28.884417 ms | 396.570042 ms |
| Production CPU frame span | 10.812416 ms | 25.022458 ms | 163.126416 ms |

The scripted run includes screenshots, fullscreen transitions, pause/rematch loads and cold shader work; one shellshot UI step missed its expected state and the script exited with failure. Phase capture itself succeeded (`/tmp/prime-production-frame-trace.json`). These observations are not steady-state gameplay performance, GPU time, scanout intervals, input-to-photon latency or presented 1% lows. They demonstrate that production timestamps record real stalls instead of substituting fixed simulation time.

## Independent network follow-up

Read-only review identified recovered continuous events borrowing the newest carrier clock, missing non-Imperialist 15-unit headshot range, Imperialist declared-body/turret mismatch and direct-victim splash exclusion. The network owner is implementing its geometry/component-outcome corrections and regressions. The render agent added only NetFireEvents' accepted-emission scope and BeamProjectileEntity's exact scoped phase/travel/component hooks: temporary Active/selection/homing state restores on failure; consumed IDs remain monotonic; each admitted native root pulse uses its source phase once; real active Process steps accumulate travel and native pellets/children get detached component IDs. These seams compiled in the isolated server, combined client and final Android sources. Final combat regression results remain with the network owner and root.
