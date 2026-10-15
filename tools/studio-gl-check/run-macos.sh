#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ "$(uname -s)" != Darwin ]]; then
  echo 'This check requires macOS with an accelerated OpenGL renderer.' >&2
  exit 1
fi
output="$(mktemp -d "${TMPDIR:-/tmp}/studio-gl-check.XXXXXX")"
trap 'rm -rf "$output"' EXIT
clang native-iosurface-proof.c -framework OpenGL -framework IOSurface -framework CoreFoundation -o "$output/native-proof"
"$output/native-proof"
"${DOTNET:-dotnet}" run --project studio-gl-check.csproj
