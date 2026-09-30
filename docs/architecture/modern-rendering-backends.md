# Modern rendering backends

Project Prime's modern renderer is based on the WebGPU C API through Silk.NET 2.23 and
wgpu-native. The backend policy is deliberately explicit:

| Platform | Modern backends | Default once the renderer migration is enabled |
| --- | --- | --- |
| Windows | DirectX 12, Vulkan | DirectX 12 |
| macOS | Metal, Vulkan through MoltenVK | Metal |
| Linux | Vulkan | Vulkan |
| Android | Vulkan | Vulkan |

OpenGL/OpenGL ES remains the compatibility fallback while the shared renderer is moved
off legacy GL state.

## Why one WebGPU renderer

The current renderer is heavily OpenGL-oriented and Android already has a compatibility
facade that converts legacy immediate-mode/display-list calls into buffers. Reusing that
shape gives Project Prime one renderer-facing API while wgpu-native handles DirectX 12,
Metal and Vulkan underneath. It avoids four independent material, post-process, replay,
map-editor and UI renderers drifting apart.

Silk.NET.WebGPU 2.23.0 is generated against the WebGPU ABI used by wgpu-native commit:

`33133da4ec5a0174cb21539ef2d3346f75200411`

Do not update one side without the other. Newer wgpu-native releases changed callback,
surface and device layouts.

## Native builds

Windows and Linux use `Silk.NET.WebGPU.Native.WGPU 2.23.0`.

macOS uses a repository-built wgpu-native library so the same binary contains Metal and
the `vulkan-portability` backend:

```bash
tools/wgpu/build-native.sh osx-arm64
# or
tools/wgpu/build-native.sh osx-x64
```

The macOS package also carries the Vulkan Loader and MoltenVK. At runtime the Vulkan
probe points the loader at `MoltenVK_icd.json`.

Android uses the same pinned ABI and builds an NDK shared library. API 24 is the floor,
matching the Android project:

```bash
export ANDROID_NDK_ROOT=/path/to/android-sdk/ndk/<version>
tools/wgpu/build-native.sh android-arm64
# optional emulator:
tools/wgpu/build-native.sh android-x64
```

The Android project includes a built `libwgpu_native.so` automatically when it exists
under `artifacts/wgpu-native-android/lib/<abi>/`.

## Diagnostics

The policy check needs no display or GPU:

```bash
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -renderbackendcheck
```

The native probe creates a real wgpu-native instance, adapter and device:

```bash
ProjectPrime -renderbackendprobe -renderer dx12
ProjectPrime -renderbackendprobe -renderer vulkan
ProjectPrime -renderbackendprobe -renderer metal
```

Aliases include `d3d12`, `directx12`, `vk` and `moltenvk`.

## Renderer migration gates

The modern backend is intentionally not made the normal gameplay renderer until these
gates are complete:

1. Generalize Android's `GlEs` facade into a platform-neutral legacy-render command
   layer and retain its immediate-mode/display-list batching.
2. Implement WebGPU textures, samplers, buffers, uniform state and shader modules.
3. Map framebuffer changes and clears to render-pass boundaries, with a pipeline cache
   keyed by shader/topology/blend/depth/stencil/cull/write-mask/target format.
4. Port shadow maps, deferred PBR, outlines, replay targets, editor previews, UI overlay,
   screenshots/readback and movie textures.
5. Create native swapchain surfaces from the existing OpenTK window on desktop and the
   existing Android SurfaceView/ANativeWindow lifecycle.
6. Run rendered parity captures and resource-lifetime checks on DX12, Vulkan, Metal and
   Android Vulkan before changing the default away from OpenGL.

Until gate 6, `-renderer` is used by the modern backend probe and future renderer
activation code. The ordinary game path continues to use the known-good GL renderer.
