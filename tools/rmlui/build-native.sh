#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
rml_version=6.3
rml_revision=ba95ffe8bfb6370efb2cdcca927eaad4710c5413
freetype_version=VER-2-13-3
freetype_revision=42608f77f20749dd6ddc9e0536788eaad70ea4b5
source_root="$root/artifacts/rmlui-poc-src"
rml_src="$source_root/RmlUi-$rml_version"
ft_src="$source_root/freetype-$freetype_version"

usage() {
    cat >&2 <<'USAGE'
usage: tools/rmlui/build-native.sh <auto|osx-arm64|osx-x64|linux-x64>

Builds the optional RmlUi 6.3 proof-of-concept bridge. Nothing is installed
system-wide; RmlUi and a minimal static FreeType are pinned under artifacts/.
PRIME_RMLUI_BUILD_JOBS controls build workers (1..32; default 2).
USAGE
    exit 2
}

[[ $# == 1 ]] || usage
target=$1

if [[ "$target" == auto ]]; then
    case "$(uname -s)-$(uname -m)" in
        Darwin-arm64) target=osx-arm64 ;;
        Darwin-x86_64) target=osx-x64 ;;
        Linux-x86_64) target=linux-x64 ;;
        *) echo "error: cannot infer an RmlUi POC target on $(uname -s) $(uname -m)" >&2; exit 2 ;;
    esac
fi

case "$target" in
    osx-arm64|osx-x64|linux-x64) ;;
    *) usage ;;
esac

# Pass an explicit count: bare --parallel can select unlimited GNU Make jobs.
build_jobs=${PRIME_RMLUI_BUILD_JOBS-2}
case "$build_jobs" in
    [1-9]|[12][0-9]|3[0-2]) ;;
    *) echo "error: PRIME_RMLUI_BUILD_JOBS must be an integer from 1 to 32" >&2; exit 2 ;;
esac

run_phase() {
    local phase=$1
    shift
    local started=$SECONDS status=0
    printf 'DIAGNOSTIC RMLUI START phase=%s target=%s jobs=%s\n' "$phase" "$target" "$build_jobs"
    "$@" || status=$?
    if (( status != 0 )); then
        printf 'DIAGNOSTIC RMLUI FAIL phase=%s target=%s jobs=%s elapsedSeconds=%s exit=%s\n' "$phase" "$target" "$build_jobs" "$((SECONDS - started))" "$status" >&2
        return "$status"
    fi
    printf 'DIAGNOSTIC RMLUI DONE phase=%s target=%s jobs=%s elapsedSeconds=%s\n' "$phase" "$target" "$build_jobs" "$((SECONDS - started))"
}

for command in git cmake; do
    command -v "$command" >/dev/null || { echo "error: $command is required" >&2; exit 1; }
done

retry_git() {
    local attempt=1 delay=4
    until "$@"; do
        local status=$?
        if (( attempt >= 4 )); then return "$status"; fi
        echo "warning: git command failed (attempt $attempt/4), retrying in ${delay}s" >&2
        sleep "$delay"
        attempt=$((attempt + 1))
        delay=$((delay * 2))
    done
}

mkdir -p "$source_root"
if [[ ! -d "$rml_src/.git" ]]; then
    run_phase rmlui-clone retry_git git clone --filter=blob:none --no-checkout https://github.com/mikke89/RmlUi.git "$rml_src"
fi
run_phase rmlui-fetch retry_git git -C "$rml_src" fetch --depth 1 origin "$rml_revision"
run_phase rmlui-checkout git -C "$rml_src" checkout --detach "$rml_revision"

if [[ ! -d "$ft_src/.git" ]]; then
    run_phase freetype-clone retry_git git clone --filter=blob:none --no-checkout https://github.com/freetype/freetype.git "$ft_src"
fi
run_phase freetype-fetch retry_git git -C "$ft_src" fetch --depth 1 origin "$freetype_revision"
run_phase freetype-checkout git -C "$ft_src" checkout --detach "$freetype_revision"

ft_build="$root/artifacts/rmlui-poc-build/freetype-$target"
ft_install="$root/artifacts/rmlui-poc-deps/freetype-$target"
bridge_build="$root/artifacts/rmlui-poc-build/bridge-$target"
out="$root/artifacts/rmlui-native/$target"
rm -rf "$ft_build" "$ft_install" "$bridge_build"
mkdir -p "$ft_build" "$ft_install" "$bridge_build" "$out"

arch_args=()
if [[ "$target" == osx-arm64 ]]; then
    [[ $(uname -s) == Darwin ]] || { echo "error: macOS targets must be built on macOS" >&2; exit 1; }
    arch_args=(-DCMAKE_OSX_ARCHITECTURES=arm64)
elif [[ "$target" == osx-x64 ]]; then
    [[ $(uname -s) == Darwin ]] || { echo "error: macOS targets must be built on macOS" >&2; exit 1; }
    arch_args=(-DCMAKE_OSX_ARCHITECTURES=x86_64)
fi

run_phase freetype-configure cmake -S "$ft_src" -B "$ft_build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$ft_install" \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DBUILD_SHARED_LIBS=OFF \
    -DFT_DISABLE_ZLIB=TRUE \
    -DFT_DISABLE_BZIP2=TRUE \
    -DFT_DISABLE_PNG=TRUE \
    -DFT_DISABLE_HARFBUZZ=TRUE \
    -DFT_DISABLE_BROTLI=TRUE \
    "${arch_args[@]}"
run_phase freetype-build cmake --build "$ft_build" --config Release --parallel "$build_jobs"
run_phase freetype-install cmake --install "$ft_build" --config Release

run_phase bridge-configure cmake -S "$root/native/rmlui-poc" -B "$bridge_build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DRMLUI_SOURCE_DIR="$rml_src" \
    -DCMAKE_PREFIX_PATH="$ft_install" \
    "${arch_args[@]}"
run_phase bridge-build cmake --build "$bridge_build" --config Release --target ProjectPrimeRmlUiNative --parallel "$build_jobs"

case "$target" in
    osx-*)
        library=$(find "$bridge_build" -type f -name 'libProjectPrime.RmlUi.Native.dylib' | head -1)
        [[ -n "$library" ]] || { echo "error: RmlUi bridge dylib was not produced" >&2; exit 1; }
        cp "$library" "$out/libProjectPrime.RmlUi.Native.dylib"
        run_phase bridge-install-name install_name_tool -id @rpath/libProjectPrime.RmlUi.Native.dylib "$out/libProjectPrime.RmlUi.Native.dylib"
        run_phase bridge-sign codesign --force --sign - "$out/libProjectPrime.RmlUi.Native.dylib"
        ;;
    linux-x64)
        library=$(find "$bridge_build" -type f -name 'libProjectPrime.RmlUi.Native.so' | head -1)
        [[ -n "$library" ]] || { echo "error: RmlUi bridge .so was not produced" >&2; exit 1; }
        cp "$library" "$out/libProjectPrime.RmlUi.Native.so"
        ;;
esac

cat > "$out/RMLUI-POC.txt" <<INFO
Project Prime RmlUi proof of concept
rmlui=$rml_version ($rml_revision)
freetype=$freetype_version ($freetype_revision)
target=$target
renderer=OpenGL 2 compatibility backend
INFO

echo "RmlUi POC native runtime -> $out"
