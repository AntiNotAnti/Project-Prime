#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
[[ $# -ge 1 ]] || { echo 'usage: tools/wgpu/build-native.sh <win-x64|linux-x64|linux-arm64|osx-arm64|osx-x64|android-arm64|android-x64> [--offline] [--fault-test] [--unit-tests]' >&2; exit 2; }
requested=$1
shift
offline_option=""
fault=false
unit_tests=false
for option in "$@"; do
    case "$option" in
        --offline) offline_option=--offline ;;
        --fault-test) fault=true ;;
        --unit-tests) unit_tests=true ;;
        *) echo "unknown option: $option" >&2; exit 2 ;;
    esac
done
# Developer installations commonly leave Cargo out of the GUI app's PATH.
if ! command -v cargo >/dev/null && [[ -x "${CARGO_HOME:-$HOME/.cargo}/bin/cargo" ]]; then
    export PATH="${CARGO_HOME:-$HOME/.cargo}/bin:$PATH"
fi
command -v cargo >/dev/null || { echo 'error: install Rust/cargo' >&2; exit 1; }
python_bin="${PRIME_PYTHON:-python3}"
src=$("$python_bin" "$repo/tools/wgpu/prepare-native.py" ${offline_option:+"$offline_option"})
features=wgsl,glsl
library=libwgpu_native.so
out="$repo/artifacts/wgpu-native-desktop/$requested"
case "$requested" in
    win-x64)
        [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* || "$(uname -s)" == CYGWIN* ]] || { echo 'error: build win-x64 on a Windows MSVC host' >&2; exit 1; }
        triple=x86_64-pc-windows-msvc; features="$features,dx12"; library=wgpu_native.dll ;;
    linux-x64)
        [[ "$(uname -s)" == Linux ]] || { echo 'error: build Linux targets on Linux' >&2; exit 1; }
        triple=x86_64-unknown-linux-gnu ;;

    linux-arm64)
        [[ "$(uname -s)" == Linux ]] || { echo 'error: build Linux targets on Linux' >&2; exit 1; }
        triple=aarch64-unknown-linux-gnu
        if [[ "$(uname -m)" != aarch64 && "$(uname -m)" != arm64 ]]; then
            linker="${CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER:-aarch64-linux-gnu-gcc}"
            command -v "$linker" >/dev/null || { echo 'error: install gcc-aarch64-linux-gnu or set CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER' >&2; exit 1; }
            export CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER="$linker"
            export CC_aarch64_unknown_linux_gnu="$linker"
        fi
        ;;

    osx-arm64) [[ "$(uname -s)" == Darwin ]] || { echo "error: build macOS targets on macOS" >&2; exit 1; }; triple=aarch64-apple-darwin; features="$features,metal,vulkan-portability"; library=libwgpu_native.dylib; out="$repo/artifacts/wgpu-native-macos/$requested" ;;
    osx-x64) [[ "$(uname -s)" == Darwin ]] || { echo "error: build macOS targets on macOS" >&2; exit 1; }; triple=x86_64-apple-darwin; features="$features,metal,vulkan-portability"; library=libwgpu_native.dylib; out="$repo/artifacts/wgpu-native-macos/$requested" ;;
    android-arm64|android-x64)
        if [[ "$requested" == android-arm64 ]]; then triple=aarch64-linux-android; abi=arm64-v8a; else triple=x86_64-linux-android; abi=x86_64; fi
        out="$repo/artifacts/wgpu-native-android/lib/$abi"
        ndk="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}"
        [[ -d "$ndk/toolchains/llvm/prebuilt" ]] || { echo 'error: set ANDROID_NDK_ROOT to an installed NDK' >&2; exit 1; }
        host=$(find "$ndk/toolchains/llvm/prebuilt" -mindepth 1 -maxdepth 1 -type d | head -1)
        linker="$host/bin/${triple}24-clang"
        [[ -x "$linker" ]] || { echo "error: linker unavailable: $linker" >&2; exit 1; }
        env_key=$(echo "$triple" | tr '[:lower:]-' '[:upper:]_')
        export "CARGO_TARGET_${env_key}_LINKER=$linker"
        clang_key=$(echo "$triple" | tr '-' '_')
        export "BINDGEN_EXTRA_CLANG_ARGS_${clang_key}=--target=${triple}24 --sysroot=$host/sysroot"
        export RUSTFLAGS="${RUSTFLAGS:-} -C link-arg=-Wl,-z,max-page-size=16384 -C link-arg=-Wl,-z,common-page-size=16384"
        ;;
    *) echo "unsupported target: $requested" >&2; exit 2 ;;
esac
if ! rustup target list --installed | grep -Fxq "$triple"; then
    [[ -z "$offline_option" ]] || { echo "error: Rust target unavailable offline: $triple" >&2; exit 1; }
    rustup target add "$triple"
fi
if $fault; then features="$features,prime-fault-injection"; out="$repo/artifacts/wgpu-native-fault-tests/$requested"; fi
export CARGO_TARGET_DIR="$repo/artifacts/wgpu-native-target-patched"
cargo build --manifest-path "$src/Cargo.toml" --locked ${offline_option:+"$offline_option"} --release --target "$triple" --no-default-features --features "$features"
if $unit_tests; then
    cargo test --manifest-path "$src/Cargo.toml" --locked ${offline_option:+"$offline_option"} --release --target "$triple" --no-default-features --features "$features" --lib prime_surface_tests
    rustc --test "$repo/tools/wgpu/prime-dx12-wsi-policy.rs" -o "$CARGO_TARGET_DIR/prime-dx12-policy-tests"
    "$CARGO_TARGET_DIR/prime-dx12-policy-tests"
    if [[ "$requested" == win-x64 ]]; then
        cargo test --manifest-path "${src}-core/wgpu-hal/Cargo.toml" --locked ${offline_option:+"$offline_option"} --release --features dx12 --lib prime_dx12_tests
        cargo test --manifest-path "${src}-core/wgpu-hal/Cargo.toml" --locked ${offline_option:+"$offline_option"} --release --features dx12 --lib prime_fxc_source_name_tests
    fi
fi
mkdir -p "$out"
cp "$CARGO_TARGET_DIR/$triple/release/$library" "$out/$library"
"$python_bin" "$repo/tools/wgpu/write-runtime-manifest.py" "$src/PRIME-PATCHES.json" "$out/$library" "$requested" "$features"
if [[ "$requested" == osx-* || "$requested" == linux-* || "$requested" == win-* ]]; then
    if $fault; then "$python_bin" "$repo/tools/wgpu/test-surface-outcomes.py" "$out/$library" --fault-library; else "$python_bin" "$repo/tools/wgpu/test-surface-outcomes.py" "$out/$library"; fi
fi
echo "patched wgpu-native $requested -> $out"
