#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
cargo build --release --locked --manifest-path tools/modern-shaders/translate/Cargo.toml --target-dir "$tmp/target"
dotnet run --project tools/modern-shaders/export/export.csproj -c Release -- "$tmp/shaders.json"
python3 tools/modern-shaders/generate.py "$tmp/shaders.json" "$tmp/target/release/prime-shader-translate"
"$tmp/target/release/prime-shader-translate" --check-postprocess-hlsl \
    src/MphRead/Mods/Render/Generated/PostProcess.fragment.wgsl "$tmp/postprocess.hlsl"
git diff --exit-code -- src/MphRead/Mods/Render/Generated
