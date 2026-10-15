#!/usr/bin/env bash
# Check the OpenGL/GLES-only architectural contract before compilation/publishing.
set -euo pipefail
fail() { echo "OpenGL-only contract: $*" >&2; exit 1; }

if grep -Eq 'Silk.NET.(WebGPU|MoltenVK)|libwgpu_native|libMoltenVK|MoltenVK_icd|[.]wgsl' src/MphRead/MphRead.csproj src/MphRead/MphRead.StudioEngine.csproj \
  src/ProjectPrime.Studio/ProjectPrime.Studio.csproj \
  src/ProjectPrime.Studio.Protocol/ProjectPrime.Studio.Protocol.csproj \
  src/MphRead.Android/MphRead.Android.csproj; then
  fail 'retired WebGPU/MoltenVK dependency in project'
fi
if find src/MphRead/Mods/Render -maxdepth 1 -name 'ModernGraphics*.cs' -print | grep -q .; then
  fail 'retired WebGPU implementation source present'
fi
if find src/MphRead/Mods/Render -maxdepth 1 -name 'ModernRender*.cs' -print | grep -q .; then
  fail 'retired alternate renderer diagnostics present'
fi
if [[ -d src/MphRead/Mods/Render/Generated ]] && find src/MphRead/Mods/Render/Generated -type f -print | grep -q .; then
  fail 'generated WebGPU shader artifacts present'
fi
[[ ! -d tools/wgpu && ! -d tools/modern-shaders ]] || fail 'retired renderer build tools present'
[[ ! -f src/MphRead/Mods/Render/DeferredPbr.cs ]] ||
  fail 'retired deferred PBR implementation present'
if grep -Eq 'history_tex|pbr_albedo|hdr_tex|bloom_enable|dynamic_light_count|aa_mode|reflections' \
  src/MphRead/Mods/Render/GraphicsPipeline.cs; then
  fail 'retired postprocessing shader resource or uniform present'
fi
if grep -Eq 'libwgpu_native|libMoltenVK|MoltenVK_icd|tools/wgpu/build-native' \
  .github/workflows/build.yml .github/workflows/release.yml \
  tools/check-macos-build.sh tools/test-macos-tools.sh tools/package-macos.sh; then
  fail 'retired native renderer referenced by packaging'
fi
# Creator hosts must use the current OpenTK pass. Exclude generated build
# directories so a stale local binary cannot mask a source dependency change.
if find src/ProjectPrime.Studio src/ProjectPrime.Studio.Protocol \
  src/MphRead/Mods/StudioRendering src/MphRead/Mods/StudioReplay \
  -type d \( -name bin -o -name obj \) -prune -o \
  -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.wgsl' \) -print0 | \
  xargs -0 grep -En 'Silk[.]NET[.](WebGPU|MoltenVK)|libwgpu_native|libMoltenVK|MoltenVK_icd|ModernGraphicsDevice|WGPU[A-Za-z]|[.]wgsl'; then
  fail 'retired renderer dependency in Studio source'
fi
if find src/ProjectPrime.Studio src/MphRead/Mods/StudioRendering \
  -type d \( -name bin -o -name obj \) -prune -o -type f -name '*.wgsl' -print | grep -q .; then
  fail 'retired shader artifact in Studio source'
fi
# CGL and IOSurface are public OpenGL transport APIs. The live compatibility
# bridge must keep shared pixels on the GPU; explicit export readback is separate.
if grep -Eq 'glReadPixels|GetTexImage|SKBitmap|RenderTargetBitmap' \
  src/MphRead/Mods/StudioRendering/MacLegacyGlSurface.cs; then
  fail 'CPU readback or bitmap transport in the live macOS OpenGL bridge'
fi
echo 'OpenGL/GLES-only source and packaging contract passed.'
