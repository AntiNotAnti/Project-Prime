#!/usr/bin/env bash
# Check the OpenGL/GLES-only architectural contract before compilation/publishing.
set -euo pipefail
fail() { echo "OpenGL-only contract: $*" >&2; exit 1; }

if grep -Eq 'Silk.NET.(WebGPU|MoltenVK)|libwgpu_native|libMoltenVK|MoltenVK_icd|[.]wgsl' src/MphRead/MphRead.csproj src/MphRead.Android/MphRead.Android.csproj; then
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
echo 'OpenGL/GLES-only source and packaging contract passed.'
