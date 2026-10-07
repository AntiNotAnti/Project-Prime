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
usage: tools/rmlui/build-native.sh <auto|osx-arm64|osx-x64|linux-x64|win-x64|android-arm64|android-x64> [gl2|draw-list]

Builds the opt-in RmlUi 6.3 bridge with pinned static RmlUi and FreeType.
Desktop defaults to gl2; Android requires draw-list and ANDROID_NDK_ROOT.
Windows requires Git Bash, CMake and Visual Studio C++ build tools.
PRIME_RMLUI_BUILD_JOBS controls build workers (1..32; default 2).
PRIME_RMLUI_OFFLINE=1 reuses the exact pinned dependency checkouts.
USAGE
    exit 2
}

[[ $# -ge 1 && $# -le 2 ]] || usage
target=$1
host=$(uname -s)
if [[ "$target" == auto ]]; then
    case "$host-$(uname -m)" in
        Darwin-arm64) target=osx-arm64 ;;
        Darwin-x86_64) target=osx-x64 ;;
        Linux-x86_64) target=linux-x64 ;;
        MINGW*-x86_64|MSYS*-x86_64) target=win-x64 ;;
        *) echo "error: cannot infer an RmlUi target on $host $(uname -m)" >&2; exit 2 ;;
    esac
fi
case "$target" in
    osx-arm64|osx-x64) [[ "$host" == Darwin ]] || { echo "error: macOS targets require macOS" >&2; exit 2; } ;;
    linux-x64) [[ "$host" == Linux ]] || { echo "error: linux-x64 requires Linux" >&2; exit 2; } ;;
    win-x64) [[ "$host" == MINGW* || "$host" == MSYS* ]] || { echo "error: win-x64 requires Git Bash on Windows" >&2; exit 2; } ;;
    android-arm64|android-x64) ;;
    *) usage ;;
esac
renderer=${2:-gl2}
if [[ "$target" == android-* && $# == 1 ]]; then renderer=draw-list; fi
case "$renderer" in
    gl2) gl2=ON ;;
    draw-list) gl2=OFF ;;
    *) usage ;;
esac
[[ "$target" != android-* || "$renderer" == draw-list ]] || { echo "error: Android requires the draw-list adapter" >&2; exit 2; }

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
    printf 'DIAGNOSTIC RMLUI START phase=%s target=%s renderer=%s jobs=%s\n' "$phase" "$target" "$renderer" "$build_jobs"
    "$@" || status=$?
    if (( status != 0 )); then
        printf 'DIAGNOSTIC RMLUI FAIL phase=%s target=%s elapsedSeconds=%s exit=%s\n' "$phase" "$target" "$((SECONDS - started))" "$status" >&2
        return "$status"
    fi
    printf 'DIAGNOSTIC RMLUI DONE phase=%s target=%s elapsedSeconds=%s\n' "$phase" "$target" "$((SECONDS - started))"
}
for command in git cmake; do
    command -v "$command" >/dev/null || { echo "error: $command is required" >&2; exit 1; }
done
python=python3
[[ "$target" != win-x64 ]] || python=python
command -v "$python" >/dev/null || { echo "error: $python is required for runtime manifest verification" >&2; exit 1; }
retry_git() {
    local attempt=1 delay=4 status=0
    while ! "$@"; do
        # A failed command under `!` loses its status; stop with a real failure.
        status=1
        if (( attempt >= 4 )); then return "$status"; fi
        echo "warning: dependency fetch failed (attempt $attempt/4), retrying in ${delay}s" >&2
        sleep "$delay"
        attempt=$((attempt + 1))
        delay=$((delay * 2))
    done
}
checkout_dependency() {
    local name=$1 url=$2 source=$3 revision=$4
    if [[ "${PRIME_RMLUI_OFFLINE:-0}" == 1 ]]; then
        [[ -d "$source/.git" && "$(git -C "$source" rev-parse HEAD)" == "$revision" ]] || {
            echo "error: offline $name checkout is missing or does not match $revision" >&2; return 1;
        }
        [[ -z "$(git -C "$source" status --porcelain --untracked-files=no)" ]] || {
            echo "error: offline $name checkout has modified tracked files" >&2; return 1;
        }
        return
    fi
    if [[ ! -d "$source/.git" ]]; then
        run_phase "$name-clone" retry_git git clone --filter=blob:none --no-checkout "$url" "$source"
    fi
    run_phase "$name-fetch" retry_git git -C "$source" fetch --depth 1 origin "$revision"
    run_phase "$name-checkout" git -C "$source" checkout --detach "$revision"
    [[ -z "$(git -C "$source" status --porcelain --untracked-files=no)" ]] || {
        echo "error: $name checkout has modified tracked files; use a clean pinned dependency checkout" >&2; return 1;
    }
}
mkdir -p "$source_root"
checkout_dependency rmlui https://github.com/mikke89/RmlUi.git "$rml_src" "$rml_revision"
checkout_dependency freetype https://github.com/freetype/freetype.git "$ft_src" "$freetype_revision"

ft_build="$root/artifacts/rmlui-poc-build/freetype-$target"
ft_install="$root/artifacts/rmlui-poc-deps/freetype-$target"
bridge_build="$root/artifacts/rmlui-poc-build/bridge-$target-$renderer"
out="$root/artifacts/rmlui-native/$target"
if [[ "$target" == android-* ]]; then
    abi=arm64-v8a
    [[ "$target" != android-x64 ]] || abi=x86_64
    out="$root/artifacts/rmlui-native-android/lib/$abi"
fi
mkdir -p "$ft_build" "$ft_install" "$bridge_build" "$out"
arch_args=()
case "$target" in
    osx-arm64) arch_args=(-DCMAKE_OSX_ARCHITECTURES=arm64) ;;
    osx-x64) arch_args=(-DCMAKE_OSX_ARCHITECTURES=x86_64) ;;
    # FreeType declares old CMake compatibility; explicitly opt it into the
    # runtime-selection policy or it keeps /MD and fails to link against /MT.
    win-x64) arch_args=(-A x64 -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded) ;;
    android-*)
        ndk=${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}
        [[ -n "$ndk" && -f "$ndk/build/cmake/android.toolchain.cmake" ]] || {
            echo "error: set ANDROID_NDK_ROOT to an installed Android NDK" >&2; exit 2;
        }
        arch_args=(-DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake"
            -DANDROID_ABI="$abi" -DANDROID_PLATFORM=android-24 -DANDROID_STL=c++_static)
        ;;
esac
# Bash 3.2's nounset rejects an empty array. Populate a harmless shared option.
arch_args+=(-DCMAKE_POSITION_INDEPENDENT_CODE=ON)
run_phase freetype-configure cmake -S "$ft_src" -B "$ft_build" \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ft_install" \
    -DBUILD_SHARED_LIBS=OFF -DFT_DISABLE_ZLIB=TRUE -DFT_DISABLE_BZIP2=TRUE \
    -DFT_DISABLE_PNG=TRUE -DFT_DISABLE_HARFBUZZ=TRUE -DFT_DISABLE_BROTLI=TRUE \
    "${arch_args[@]}"
run_phase freetype-build cmake --build "$ft_build" --config Release --parallel "$build_jobs"
run_phase freetype-install cmake --install "$ft_build" --config Release
if [[ "$target" == android-* ]]; then
    # The NDK roots find_package searches in its sysroot. Our cross-built
    # static dependency lives outside it; pass exact paths to avoid selecting
    # a host FreeType or failing a rooted prefix search.
    arch_args+=(-DFREETYPE_LIBRARY="$ft_install/lib/libfreetype.a"
        -DFREETYPE_INCLUDE_DIR_ft2build="$ft_install/include/freetype2"
        -DFREETYPE_INCLUDE_DIR_freetype2="$ft_install/include/freetype2")
fi
source_fingerprint=$("$python" "$root/tools/rmlui/verify-runtime.py" --fingerprint)
run_phase bridge-configure cmake -S "$root/native/rmlui-poc" -B "$bridge_build" \
    -DCMAKE_BUILD_TYPE=Release -DRMLUI_SOURCE_DIR="$rml_src" \
    -DCMAKE_PREFIX_PATH="$ft_install" -DPROJECT_PRIME_RMLUI_GL2="$gl2" \
    "${arch_args[@]}"
run_phase bridge-build cmake --build "$bridge_build" --config Release --target ProjectPrimeRmlUiNative --parallel "$build_jobs"
run_phase bridge-install cmake --install "$bridge_build" --config Release --prefix "$out"
case "$target" in
    osx-*)
        library="$out/libProjectPrime.RmlUi.Native.dylib"
        run_phase bridge-install-name install_name_tool -id @rpath/libProjectPrime.RmlUi.Native.dylib "$library"
        run_phase bridge-sign codesign --force --sign - "$library"
        ;;
    win-*) library="$out/ProjectPrime.RmlUi.Native.dll" ;;
    *) library="$out/libProjectPrime.RmlUi.Native.so" ;;
esac
[[ -f "$library" ]] || { echo "error: bridge library was not installed: $library" >&2; exit 1; }
mkdir -p "$out/licenses"
cp "$rml_src/LICENSE.txt" "$out/licenses/RmlUi-LICENSE.txt"
cp "$ft_src/LICENSE.TXT" "$out/licenses/FreeType-LICENSE.txt"
cp "$ft_src/docs/FTL.TXT" "$out/licenses/FreeType-FTL.txt"
cp "$ft_src/docs/GPLv2.TXT" "$out/licenses/FreeType-GPLv2.txt"
"$python" "$root/tools/rmlui/verify-runtime.py" --write "$library" "$target" "$renderer" "$source_fingerprint"
# Preserve the prototype's human-readable manifest name for existing tooling.
cat > "$out/RMLUI-POC.txt" <<INFO
Project Prime opt-in RmlUi native runtime
rmlui=$rml_version ($rml_revision)
freetype=$freetype_version ($freetype_revision)
target=$target
renderer=$renderer
INFO
"$python" "$root/tools/rmlui/verify-runtime.py" "$library" "$target"
echo "RmlUi native runtime -> $out"
