# Samus mobile textures

The installed mixed pack is `samus-materials-desktop-and-mobile-v1`. Its four
desktop GLBs retain the exact bytes from the final material milestone. Each
manifest entry optionally supplies `mobileModel`; Android selects that GLB,
and desktop selects `model`. Packs without the optional field retain their
original behavior. Both paths must pass the pack containment checks.

The generated kit, detailed evidence and rollback are in
`samus-hd-kit/android-textures/ANDROID-TEXTURES-RESULT.md`. The kit is ignored by
Git. The standalone `android-pack/` contains only mobile assets and uses their
canonical model paths, avoiding duplicate desktop files on a device.

## Texture contract

Mobile albedo is capped at 1024, normals at 512, and material/emissive maps at
256. Smaller Source domains stay smaller. Geometry, UVs, native transforms,
skin matrices, joints and weights retain their validated values. Six native
suit palettes remain available.

Embedded KTX2 uses `KHR_texture_basisu`, UASTC level 2, Zstd level 9 and authored
mips. Color/emission mips average in linear light; normal mips renormalize.
The loader accepts bounded embedded PNG/JPEG/KTX2 and verifies Basis references.
External image URIs and texture transforms remain outside this contract.

`extras.projectPrimeRuntimeMaps: true` marks offline conversion to the engine's
existing normal/specular/emissive channels. Normal scale and map factors must
be identity to prevent double conversion. Packed runtime material R is legacy
specular strength, G roughness, B zero. This does not add a metallic PBR BRDF.
The runtime can upload these maps directly as compressed blocks.

Characters prefer ASTC 4x4 when available. ETC2-only adapters use bounded RGBA:
the larger ETC2 probe failed this pack's image-quality budget. The general
world-texture ETC2 path remains available. Metal prefers ASTC when available: BC7 showed corruption in the
full Metal character shader despite isolated texel probes passing. Explicit
BC7 diagnostics remain available for investigation; those packet passes are
not visual acceptance. Other desktop backends retain their BC7 preference.

## Residency and renderer cache

Scene bindings share images by content and upload semantics. A bounded weak
intern pool shares encoded image arrays across LODs/forms without keeping them
alive. Base albedo and companion bindings remain scene residents. Unused suit
variants expire after 120 rendered pictures. Color resolution refreshes the
lease each draw; revisiting an evicted color reloads it. Texture quality/sampling
changes and scene disposal clear the scene bindings. Launcher preview variants
remain until that preview scene is disposed.

Native sampler/view handles can recycle their addresses. Bind-group caches now
also compare a texture resource revision after sampler replacement or texture
release. This fixes stale sampler reuse during filter changes and eviction.

All 60 distinct mobile images, including all six palettes and authored mips,
require 24.0 MiB of compressed block storage; bounded RGBA is about 96.0 MiB.
The desktop scene sweep with the mobile tier settles at 6.4 MiB and peaks at
17.0 MiB. These are engine allocation estimates, not Android process memory or
driver residency. Physical Android frame pacing and memory acceptance remain
pending until an authorized device is available.

## Build and diagnostics

Build the pinned native KTX decoder before packaging Android:

```sh
ANDROID_NDK_ROOT=/path/to/ndk PATH="/path/to/cmake/bin:$PATH" \
  bash tools/ktx/build-native.sh android-arm64
dotnet build src/MphRead.Android/MphRead.Android.csproj -c Debug -r android-arm64 \
  -p:AndroidSdkDirectory=/path/to/sdk -p:ApplicationId=com.projectprime.texturecheck
```

The debug-only `CharacterTextureAcceptanceActivity` checks mobile pack loading,
GPU upload, base/mip sampling and release without needing a ROM. The kit's
`device-check.py` installs the separate `com.projectprime.texturecheck` package
and writes device metadata and the GPU report. It does not replace the game app.
It currently targets one authorized ARM64 device.

```sh
ProjectPrime -charactermodelvalidate /absolute/path/to/starter -mobiletextures
ProjectPrime -charactermaterialacceptancecheck 'TEST ARENA' -mobiletextures -output /absolute/path/to/check
ProjectPrime -viewmodelacceptancecheck 'TEST ARENA' -mobiletextures -output /absolute/path/to/cannon
ProjectPrime -lod1acceptancecheck 'TEST ARENA' -mobiletextures -output /absolute/path/to/lod
ProjectPrime -charactertextureprobe /absolute/path/to/android-pack -compression Astc4x4Rgba -output /absolute/path/to/gpu.json
```

The kit's wrappers preserve saved graphics and launcher preferences. Use absolute
output paths because the game changes its working directory to user data.
Replay `build-encoder.sh`, then `build-mobile.py` with NumPy/Pillow, followed by
`audit-mobile.py`, `audit-compression.py` and `run-final-checks.py`.
Rollback: `python3 samus-hd-kit/android-textures/install-pack.py --rollback`.

## Remaining physical acceptance

Run the GPU probe on the target Android device, then profile the real game:
cold loads, six-suit revisits, body/cannon/ball/LOD transitions, background/resume,
low-memory behavior and sustained frame pacing. Texture transcoding on character
first use is still synchronous; this milestone does not establish hitch-free
streaming. The UASTC GLBs include mip chains and are slightly larger on disk than
the previous JPEG GLBs. Storage deduplication and broader Hunter rollout remain
separate work after device acceptance.
