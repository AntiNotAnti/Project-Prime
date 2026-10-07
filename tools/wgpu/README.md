# Project Prime native graphics runtime

Silk.NET 2.23.0 remains paired with wgpu-native `33133da4ec5a0174cb21539ef2d3346f75200411` and wgpu/core/HAL `87576b72b37c6b78b41104eb25fc31893af94092`. This is an additive bridge, not an upstream version upgrade. The existing AppKit NSView surface export remains available for MoltenVK; native Metal still uses the Metal-layer path.

`prepare-native.py` reads exact Git objects, archives private build copies, applies the checked-in patches, and patches Cargo source resolution to the private HAL/core copies. It never edits Cargo's shared source checkouts. The upstream lockfile's registry versions/checksums stay pinned. Source caches can be populated online; `--offline` fails when an exact source object, registry dependency, or Rust target is absent.

Build before publishing:

```sh
tools/wgpu/build-native.sh osx-arm64 --offline
# On a Windows MSVC host using Git Bash:
PRIME_PYTHON=python tools/wgpu/build-native.sh win-x64
# On Linux:
tools/wgpu/build-native.sh linux-x64
# Requires an installed Android NDK:
ANDROID_NDK_ROOT=/path/to/ndk tools/wgpu/build-native.sh android-arm64
```

Targets: `win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, `android-arm64`, `android-x64`. Linux arm64 cross-builds require `gcc-aarch64-linux-gnu` or an explicit linker; the current arm64 Linux release is a dedicated server and does not package graphics. Mac targets require macOS. Android builds retain the 16 KiB page-size flags. Windows requires MSVC/SDK plus libclang (hosted CI provides LLVM); Linux requires clang/libclang development packages. Python 3.9 or later and Rust/Cargo are required.

Every client publish requires `PRIME-WGPU.json` beside its native library. The verifier checks the actual SHA-256, ABI version, native/core pins, selected RID/ABI, current checked-in patch fingerprint, production feature selection, and presence/absence of bridge/injection export names in the binary. Upstream NuGet native binaries cannot substitute silently. The managed loader resolves all exports from the exact native context Silk loaded and rejects runtimes without bridge ABI 1.

Bridge outcomes are `Success=0`, `Timeout=1`, `Outdated=2`, `SurfaceLost=3`, `DeviceLost=4`, `OutOfMemory=5`, `ValidationError=6`, `InternalError=7`. Configure/acquire/present/discard and queue submission provide bounded UTF-8 diagnostics. Managed code skips timed-out frames, defers bounded WSI recovery until acquire, and uses the existing bounded device reconstruction. OOM/validation errors remain visible failures. Texture drop logs unexpected discard cleanup errors instead of panicking across C. The timestamp-period export returns nanoseconds per native tick; query use remains optional.

Test-only injection is built separately:

```sh
tools/wgpu/build-native.sh osx-arm64 --offline --fault-test
```

This runs 35 independent subprocess fault cases across the five bridge operations, checking status propagation, one-shot injection, bounded diagnostics, and absence of native aborts. Production artifacts must not export the injection entry point; manifest verification rejects test artifacts. Add `--unit-tests` to run the native classifiers plus the exact DX12 policy tests (and the real Win32 acquire-timeout fixture on Windows). The native classifier unit tests can also run with `cargo test --manifest-path <prepared>/Cargo.toml --locked --offline --release --no-default-features --features wgsl,glsl,metal,vulkan-portability --lib prime_surface_tests` on macOS (use the target's production features elsewhere).

The DX12 FXC patch gives shader source names an owned, NUL-terminated C string before `D3DCompile`. The pinned HAL previously passed an empty Rust slice for an unlabeled module, which can have the invalid address `0x1`. Windows `--unit-tests` also runs the real compiler for empty and named labels, and requires invalid unlabeled HLSL to return an ordinary compiler error. The original unlabeled geometry draw check remains part of `-renderfullcheck`.

Windows renderer CI waits for the GUI executable's process and checks its actual exit code plus native draw/window pass records. Its software Vulkan prerequisite pins Mesa lavapipe and LunarG's loader; that result proves Vulkan through a CPU adapter. Mesa OpenGL also supplies the unchanged forced compatibility recovery check. Neither prerequisite changes the requested DirectX backend or permits a missing adapter to skip native assertions.

Windows/Linux CI builds the owned runtime on its matching host, tests production/fault ABI boundaries, and downloads only the production artifact into client publishes. Existing macOS and Android jobs build the same patch inputs locally. Hardware acceptance still requires `-renderfullcheck`, repeated resize/minimize/restore, actual device loss, fullscreen, and Android pause/resume/surface recovery. Null-pointer fault injection validates the error seam; it does not prove physical WSI/device recovery or frame pacing on any GPU.

`test-runtime-packaging.py <production-library> <fault-library>` runs ten cases against the actual packaging validator, including falsified feature metadata around a real injection export. Windows/Linux CI runs this before uploading the production artifact.
