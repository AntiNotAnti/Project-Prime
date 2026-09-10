#!/usr/bin/env bash
set -euo pipefail

# Offline-only renderer shader generation. The executable never invokes a
# compiler at runtime; published artifacts are copied from the generated
# directory by Client.csproj. Every target is produced by the pinned
# SDL_shadercross CLI, not by a backend-specific compiler fallback.
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
source_file="$repo_root/src/Client/Rendering/Shaders/scene_triangle.hlsl"
scene_source_file="$repo_root/src/Client/Rendering/Shaders/scene.hlsl"
fullscreen_source_file="$repo_root/src/Client/Rendering/Shaders/fullscreen.hlsl"
hud_source_file="$repo_root/src/Client/Rendering/Shaders/hud.hlsl"
disruption_source_file="$repo_root/src/Client/Rendering/Shaders/disruption.hlsl"
cel_source_file="$repo_root/src/Client/Rendering/Shaders/cel.hlsl"
bloom_source_file="$repo_root/src/Client/Rendering/Shaders/bloom.hlsl"
tone_map_source_file="$repo_root/src/Client/Rendering/Shaders/tone_map.hlsl"
color_grade_source_file="$repo_root/src/Client/Rendering/Shaders/color_grade.hlsl"
visor_source_file="$repo_root/src/Client/Rendering/Shaders/visor.hlsl"
sky_source_file="$repo_root/src/Client/Rendering/Shaders/sky.hlsl"
surface_source_file="$repo_root/src/Client/Rendering/Shaders/surface.hlsl"
ssao_source_file="$repo_root/src/Client/Rendering/Shaders/ssao.hlsl"
shadow_source_file="$repo_root/src/Client/Rendering/Shaders/shadow.hlsl"
distortion_vector_source_file="$repo_root/src/Client/Rendering/Shaders/distortion.hlsl"
distortion_warp_source_file="$repo_root/src/Client/Rendering/Shaders/distortion_warp.hlsl"
output_dir="$repo_root/src/Client/Rendering/Shaders/Generated"
shadercross_bin="${SHADERCROSS:-$(command -v shadercross || true)}"

# Official SDL_shadercross source used to build the CLI and the exact native
# package that supplies libSDL3_shadercross/libdxcompiler/libdxil/SPIRV-Cross
# to the local macOS build. Keep these values in the manifest as provenance.
shadercross_commit="1ff05bec573988a98ef9e0260b4da44f512b8367"
shadercross_source="https://github.com/libsdl-org/SDL_shadercross.git"
native_package="SDL3-CS.MacOS.Shadercross"
native_package_version="3.0.0.11"
native_package_repository="https://github.com/edwardgushchin/SDL3-CS.git"
native_package_commit="c1d1cb0da632cb51799da6989f6e48c52f2a539e"
native_package_sha256="6dba8dd2bac69e333dcb7db529884070a272e93b7cba431235a57cc4ab8e4072"

if [[ -z "$shadercross_bin" || ! -x "$shadercross_bin" ]]; then
  cat >&2 <<'EOF'
SDL_shadercross is required for renderer shader generation but was not found.
Install/build the pinned SDL_shadercross CLI, or set SHADERCROSS=/absolute/path/to/shadercross.
No glslc/spirv-cross fallback is permitted because it would invalidate shader provenance.
EOF
  exit 2
fi
if [[ ! -f "$source_file" ]]; then
  echo "Canonical renderer shader source is missing: $source_file" >&2
  exit 2
fi
for required_source in "$scene_source_file" "$fullscreen_source_file" "$hud_source_file" \
  "$disruption_source_file" "$cel_source_file" "$bloom_source_file" "$tone_map_source_file" \
  "$color_grade_source_file" "$surface_source_file" "$ssao_source_file" \
  "$shadow_source_file" "$distortion_vector_source_file" \
  "$distortion_warp_source_file" "$visor_source_file" "$sky_source_file"; do
  if [[ ! -f "$required_source" ]]; then
    echo "Renderer shader source is missing: $required_source" >&2
    exit 2
  fi
done

mkdir -p "$output_dir"
rm -f "$output_dir/scene_triangle.vert.spv" \
  "$output_dir/scene_triangle.frag.spv" \
  "$output_dir/scene_triangle.vert.msl" \
  "$output_dir/scene_triangle.frag.msl" \
  "$output_dir/scene_triangle.vert.dxil" \
  "$output_dir/scene_triangle.frag.dxil" \
  "$output_dir/scene.vert.spv" \
  "$output_dir/scene.frag.spv" \
  "$output_dir/scene.vert.msl" \
  "$output_dir/scene.frag.msl" \
  "$output_dir/scene.vert.dxil" \
  "$output_dir/scene.frag.dxil" \
  "$output_dir/manifest.json" \
  "$output_dir/scene_manifest.json"

for shader_stem in fullscreen hud disruption cel bloom tone_map color_grade surface ssao shadow distortion distortion_warp visor sky; do
  rm -f "$output_dir/$shader_stem.vert.spv" \
    "$output_dir/$shader_stem.frag.spv" \
    "$output_dir/$shader_stem.vert.msl" \
    "$output_dir/$shader_stem.frag.msl" \
    "$output_dir/$shader_stem.vert.dxil" \
    "$output_dir/$shader_stem.frag.dxil" \
    "$output_dir/${shader_stem}_manifest.json"
done

generate() {
  local source="$1"
  local define="$2"
  local stage="$3"
  local entrypoint="$4"
  local destination="$5"
  local output="$6"
  if [[ -n "$define" ]]; then
    "$shadercross_bin" "$source" "-D$define=1" \
      --source HLSL --dest "$destination" --stage "$stage" \
      --entrypoint "$entrypoint" --output "$output"
  else
    "$shadercross_bin" "$source" \
      --source HLSL --dest "$destination" --stage "$stage" \
      --entrypoint "$entrypoint" --output "$output"
  fi
  if [[ "$destination" == "MSL" ]]; then
    # SDL_shadercross may emit whitespace-only indentation on otherwise empty
    # lines and extra blank lines at EOF. Normalize to one terminating newline
    # before hashing so artifacts pass git diff --check without semantic changes.
    perl -0777 -pi -e 's/[ \t]+$//mg; s/\n*\z/\n/' "$output"
  fi
}

generate "$source_file" "" vertex main_vs SPIRV "$output_dir/scene_triangle.vert.spv"
generate "$source_file" "" fragment main_ps SPIRV "$output_dir/scene_triangle.frag.spv"
generate "$source_file" "" vertex main_vs MSL "$output_dir/scene_triangle.vert.msl"
generate "$source_file" "" fragment main_ps MSL "$output_dir/scene_triangle.frag.msl"
generate "$source_file" "" vertex main_vs DXIL "$output_dir/scene_triangle.vert.dxil"
generate "$source_file" "" fragment main_ps DXIL "$output_dir/scene_triangle.frag.dxil"
generate "$scene_source_file" VERTEX_STAGE vertex main_vs SPIRV "$output_dir/scene.vert.spv"
generate "$scene_source_file" "" fragment main_ps SPIRV "$output_dir/scene.frag.spv"
generate "$scene_source_file" VERTEX_STAGE vertex main_vs MSL "$output_dir/scene.vert.msl"
generate "$scene_source_file" "" fragment main_ps MSL "$output_dir/scene.frag.msl"
generate "$scene_source_file" VERTEX_STAGE vertex main_vs DXIL "$output_dir/scene.vert.dxil"
generate "$scene_source_file" "" fragment main_ps DXIL "$output_dir/scene.frag.dxil"

generate_family() {
  local source="$1"
  local stem="$2"
  generate "$source" VERTEX_STAGE vertex main_vs SPIRV "$output_dir/$stem.vert.spv"
  generate "$source" "" fragment main_ps SPIRV "$output_dir/$stem.frag.spv"
  generate "$source" VERTEX_STAGE vertex main_vs MSL "$output_dir/$stem.vert.msl"
  generate "$source" "" fragment main_ps MSL "$output_dir/$stem.frag.msl"
  generate "$source" VERTEX_STAGE vertex main_vs DXIL "$output_dir/$stem.vert.dxil"
  generate "$source" "" fragment main_ps DXIL "$output_dir/$stem.frag.dxil"
}

generate_family "$fullscreen_source_file" fullscreen
generate_family "$hud_source_file" hud
generate_family "$disruption_source_file" disruption
generate_family "$cel_source_file" cel
generate_family "$bloom_source_file" bloom
generate_family "$tone_map_source_file" tone_map
generate_family "$color_grade_source_file" color_grade
generate_family "$surface_source_file" surface
generate_family "$ssao_source_file" ssao
generate_family "$shadow_source_file" shadow
generate_family "$distortion_vector_source_file" distortion
generate_family "$distortion_warp_source_file" distortion_warp
generate_family "$visor_source_file" visor
generate_family "$sky_source_file" sky

source_sha256="$(shasum -a 256 "$source_file" | awk '{print $1}')"
vert_spv_sha256="$(shasum -a 256 "$output_dir/scene_triangle.vert.spv" | awk '{print $1}')"
frag_spv_sha256="$(shasum -a 256 "$output_dir/scene_triangle.frag.spv" | awk '{print $1}')"
vert_msl_sha256="$(shasum -a 256 "$output_dir/scene_triangle.vert.msl" | awk '{print $1}')"
frag_msl_sha256="$(shasum -a 256 "$output_dir/scene_triangle.frag.msl" | awk '{print $1}')"
vert_dxil_sha256="$(shasum -a 256 "$output_dir/scene_triangle.vert.dxil" | awk '{print $1}')"
frag_dxil_sha256="$(shasum -a 256 "$output_dir/scene_triangle.frag.dxil" | awk '{print $1}')"
shadercross_cli_sha256="$(shasum -a 256 "$shadercross_bin" | awk '{print $1}')"
scene_source_sha256="$(shasum -a 256 "$scene_source_file" | awk '{print $1}')"
scene_vert_spv_sha256="$(shasum -a 256 "$output_dir/scene.vert.spv" | awk '{print $1}')"
scene_frag_spv_sha256="$(shasum -a 256 "$output_dir/scene.frag.spv" | awk '{print $1}')"
scene_vert_msl_sha256="$(shasum -a 256 "$output_dir/scene.vert.msl" | awk '{print $1}')"
scene_frag_msl_sha256="$(shasum -a 256 "$output_dir/scene.frag.msl" | awk '{print $1}')"
scene_vert_dxil_sha256="$(shasum -a 256 "$output_dir/scene.vert.dxil" | awk '{print $1}')"
scene_frag_dxil_sha256="$(shasum -a 256 "$output_dir/scene.frag.dxil" | awk '{print $1}')"

SOURCE_SHA256="$source_sha256" \
VERT_SPV_SHA256="$vert_spv_sha256" \
FRAG_SPV_SHA256="$frag_spv_sha256" \
VERT_MSL_SHA256="$vert_msl_sha256" \
FRAG_MSL_SHA256="$frag_msl_sha256" \
VERT_DXIL_SHA256="$vert_dxil_sha256" \
FRAG_DXIL_SHA256="$frag_dxil_sha256" \
SHADERCROSS_COMMIT="$shadercross_commit" \
SHADERCROSS_SOURCE="$shadercross_source" \
NATIVE_PACKAGE="$native_package" \
NATIVE_PACKAGE_VERSION="$native_package_version" \
NATIVE_PACKAGE_REPOSITORY="$native_package_repository" \
NATIVE_PACKAGE_COMMIT="$native_package_commit" \
NATIVE_PACKAGE_SHA256="$native_package_sha256" \
SHADERCROSS_CLI_SHA256="$shadercross_cli_sha256" \
SCENE_SOURCE_SHA256="$scene_source_sha256" \
SCENE_VERT_SPV_SHA256="$scene_vert_spv_sha256" \
SCENE_FRAG_SPV_SHA256="$scene_frag_spv_sha256" \
SCENE_VERT_MSL_SHA256="$scene_vert_msl_sha256" \
SCENE_FRAG_MSL_SHA256="$scene_frag_msl_sha256" \
SCENE_VERT_DXIL_SHA256="$scene_vert_dxil_sha256" \
SCENE_FRAG_DXIL_SHA256="$scene_frag_dxil_sha256" \
python3 - "$source_file" "$output_dir/manifest.json" <<'PY'
import json
import os
import sys

source = os.path.basename(sys.argv[1])
manifest = {
    "schema": 2,
    "source": source,
    "source_sha256": os.environ["SOURCE_SHA256"],
    "provenance": {
        "mode": "offline-build-only",
        "sdl_shadercross_repository": os.environ["SHADERCROSS_SOURCE"],
        "sdl_shadercross_commit": os.environ["SHADERCROSS_COMMIT"],
        "cli": "SDL_shadercross official cli.c",
        "cli_sha256": os.environ["SHADERCROSS_CLI_SHA256"],
        "native_package": os.environ["NATIVE_PACKAGE"],
        "native_package_version": os.environ["NATIVE_PACKAGE_VERSION"],
        "native_package_repository": os.environ["NATIVE_PACKAGE_REPOSITORY"],
        "native_package_commit": os.environ["NATIVE_PACKAGE_COMMIT"],
        "native_package_sha256": os.environ["NATIVE_PACKAGE_SHA256"],
        "msl_version": "1.2.0 (SDL_shadercross default)",
    },
    "artifacts": {
        "vertex_spirv": {"path": "scene_triangle.vert.spv", "format": "SPIRV", "sha256": os.environ["VERT_SPV_SHA256"]},
        "fragment_spirv": {"path": "scene_triangle.frag.spv", "format": "SPIRV", "sha256": os.environ["FRAG_SPV_SHA256"]},
        "vertex_msl": {"path": "scene_triangle.vert.msl", "format": "MSL", "sha256": os.environ["VERT_MSL_SHA256"]},
        "fragment_msl": {"path": "scene_triangle.frag.msl", "format": "MSL", "sha256": os.environ["FRAG_MSL_SHA256"]},
        "vertex_dxil": {"path": "scene_triangle.vert.dxil", "format": "DXIL", "sha256": os.environ["VERT_DXIL_SHA256"]},
        "fragment_dxil": {"path": "scene_triangle.frag.dxil", "format": "DXIL", "sha256": os.environ["FRAG_DXIL_SHA256"]},
    },
    "blocked_artifacts": {
        "metallib": "Optional: the SDL_shadercross CLI emits MSL source, while this host has no Xcode xcrun metal compiler to produce a metallib.",
    },
}
with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

SCENE_SOURCE_SHA256="$scene_source_sha256" \
SCENE_VERT_SPV_SHA256="$scene_vert_spv_sha256" \
SCENE_FRAG_SPV_SHA256="$scene_frag_spv_sha256" \
SCENE_VERT_MSL_SHA256="$scene_vert_msl_sha256" \
SCENE_FRAG_MSL_SHA256="$scene_frag_msl_sha256" \
SCENE_VERT_DXIL_SHA256="$scene_vert_dxil_sha256" \
SCENE_FRAG_DXIL_SHA256="$scene_frag_dxil_sha256" \
SHADERCROSS_COMMIT="$shadercross_commit" \
SHADERCROSS_SOURCE="$shadercross_source" \
NATIVE_PACKAGE="$native_package" \
NATIVE_PACKAGE_VERSION="$native_package_version" \
NATIVE_PACKAGE_REPOSITORY="$native_package_repository" \
NATIVE_PACKAGE_COMMIT="$native_package_commit" \
NATIVE_PACKAGE_SHA256="$native_package_sha256" \
SHADERCROSS_CLI_SHA256="$shadercross_cli_sha256" \
python3 - "$scene_source_file" "$output_dir/scene_manifest.json" <<'PY'
import json
import os
import sys

source = os.path.basename(sys.argv[1])
manifest = {
    "schema": 2,
    "source": source,
    "source_sha256": os.environ["SCENE_SOURCE_SHA256"],
    "provenance": {
        "mode": "offline-build-only",
        "sdl_shadercross_repository": os.environ["SHADERCROSS_SOURCE"],
        "sdl_shadercross_commit": os.environ["SHADERCROSS_COMMIT"],
        "cli": "SDL_shadercross official cli.c",
        "cli_sha256": os.environ["SHADERCROSS_CLI_SHA256"],
        "native_package": os.environ["NATIVE_PACKAGE"],
        "native_package_version": os.environ["NATIVE_PACKAGE_VERSION"],
        "native_package_repository": os.environ["NATIVE_PACKAGE_REPOSITORY"],
        "native_package_commit": os.environ["NATIVE_PACKAGE_COMMIT"],
        "native_package_sha256": os.environ["NATIVE_PACKAGE_SHA256"],
        "msl_version": "1.2.0 (SDL_shadercross default)",
    },
    "artifacts": {
        "vertex_spirv": {"path": "scene.vert.spv", "format": "SPIRV", "sha256": os.environ["SCENE_VERT_SPV_SHA256"]},
        "fragment_spirv": {"path": "scene.frag.spv", "format": "SPIRV", "sha256": os.environ["SCENE_FRAG_SPV_SHA256"]},
        "vertex_msl": {"path": "scene.vert.msl", "format": "MSL", "sha256": os.environ["SCENE_VERT_MSL_SHA256"]},
        "fragment_msl": {"path": "scene.frag.msl", "format": "MSL", "sha256": os.environ["SCENE_FRAG_MSL_SHA256"]},
        "vertex_dxil": {"path": "scene.vert.dxil", "format": "DXIL", "sha256": os.environ["SCENE_VERT_DXIL_SHA256"]},
        "fragment_dxil": {"path": "scene.frag.dxil", "format": "DXIL", "sha256": os.environ["SCENE_FRAG_DXIL_SHA256"]},
    },
    "blocked_artifacts": {
        "metallib": "Optional: the SDL_shadercross CLI emits MSL source, while this host has no Xcode xcrun metal compiler to produce a metallib.",
    },
}
with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

write_manifest() {
  local source="$1"
  local stem="$2"
  local manifest="$output_dir/${stem}_manifest.json"
  SHADERCROSS_COMMIT="$shadercross_commit" \
  SHADERCROSS_SOURCE="$shadercross_source" \
  NATIVE_PACKAGE="$native_package" \
  NATIVE_PACKAGE_VERSION="$native_package_version" \
  NATIVE_PACKAGE_REPOSITORY="$native_package_repository" \
  NATIVE_PACKAGE_COMMIT="$native_package_commit" \
  NATIVE_PACKAGE_SHA256="$native_package_sha256" \
  SHADERCROSS_CLI_SHA256="$shadercross_cli_sha256" \
  python3 - "$source" "$stem" "$manifest" "$output_dir" <<'PY'
import hashlib
import json
import os
import sys
from pathlib import Path

source = Path(sys.argv[1])
stem = sys.argv[2]
manifest_path = Path(sys.argv[3])
output_dir = Path(sys.argv[4])

def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()

formats = (("vertex_spirv", "vertex", "spv", "SPIRV"),
           ("fragment_spirv", "fragment", "spv", "SPIRV"),
           ("vertex_msl", "vertex", "msl", "MSL"),
           ("fragment_msl", "fragment", "msl", "MSL"),
           ("vertex_dxil", "vertex", "dxil", "DXIL"),
           ("fragment_dxil", "fragment", "dxil", "DXIL"))
artifacts = {}
for name, stage, extension, fmt in formats:
    file_stage = "vert" if stage == "vertex" else "frag"
    artifact_path = output_dir / f"{stem}.{file_stage}.{extension}"
    artifacts[name] = {
        "entrypoint": "main_vs" if stage == "vertex" else "main_ps",
        "format": fmt,
        "path": artifact_path.name,
        "sha256": sha256(artifact_path),
        "stage": stage,
    }

manifest = {
    "schema": 2,
    "source": source.name,
    "source_sha256": sha256(source),
    "provenance": {
        "mode": "offline-build-only",
        "sdl_shadercross_repository": os.environ["SHADERCROSS_SOURCE"],
        "sdl_shadercross_commit": os.environ["SHADERCROSS_COMMIT"],
        "cli": "SDL_shadercross official cli.c",
        "cli_sha256": os.environ["SHADERCROSS_CLI_SHA256"],
        "native_package": os.environ["NATIVE_PACKAGE"],
        "native_package_version": os.environ["NATIVE_PACKAGE_VERSION"],
        "native_package_repository": os.environ["NATIVE_PACKAGE_REPOSITORY"],
        "native_package_commit": os.environ["NATIVE_PACKAGE_COMMIT"],
        "native_package_sha256": os.environ["NATIVE_PACKAGE_SHA256"],
        "msl_version": "1.2.0 (SDL_shadercross default)",
    },
    "artifacts": artifacts,
    "blocked_artifacts": {
        "metallib": "Optional: the SDL_shadercross CLI emits MSL source, while this host has no Xcode xcrun metal compiler to produce a metallib.",
    },
}
with manifest_path.open("w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY
}

write_manifest "$fullscreen_source_file" fullscreen
write_manifest "$hud_source_file" hud
write_manifest "$disruption_source_file" disruption
write_manifest "$cel_source_file" cel
write_manifest "$bloom_source_file" bloom
write_manifest "$tone_map_source_file" tone_map
write_manifest "$color_grade_source_file" color_grade
write_manifest "$surface_source_file" surface
write_manifest "$ssao_source_file" ssao
write_manifest "$shadow_source_file" shadow
write_manifest "$distortion_vector_source_file" distortion
write_manifest "$distortion_warp_source_file" distortion_warp
write_manifest "$visor_source_file" visor
write_manifest "$sky_source_file" sky

echo "Generated SDL_shadercross SPIR-V, MSL, and DXIL artifacts in $output_dir"
echo "metallib remains optional: xcrun metal is unavailable on this host."
