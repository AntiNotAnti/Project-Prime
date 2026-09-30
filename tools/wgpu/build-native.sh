#!/usr/bin/env bash
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
commit=33133da4ec5a0174cb21539ef2d3346f75200411
src="$repo/artifacts/wgpu-native-src/$commit"
target="$repo/artifacts/wgpu-native-target"

usage() {
    cat >&2 <<'EOF'
usage: tools/wgpu/build-native.sh <target>

Targets:
  android-arm64   Vulkan for arm64-v8a
  android-x64     Vulkan for x86_64 emulator
  osx-arm64       Metal + Vulkan/MoltenVK for Apple Silicon
  osx-x64         Metal + Vulkan/MoltenVK for Intel Mac
EOF
    exit 2
}

[[ $# == 1 ]] || usage
requested=$1

command -v git >/dev/null || { echo "error: git is required" >&2; exit 1; }
command -v cargo >/dev/null || { echo "error: Rust/cargo is required" >&2; exit 1; }
command -v rustup >/dev/null || { echo "error: rustup is required" >&2; exit 1; }

if [[ ! -d "$src/.git" ]]; then
    mkdir -p "$(dirname "$src")"
    git clone --filter=blob:none --no-checkout https://github.com/gfx-rs/wgpu-native.git "$src"
fi
git -C "$src" fetch --depth 1 origin "$commit"
git -C "$src" checkout --detach "$commit"
# wgpu-native's C ABI headers are a git submodule at this revision. A shallow
# checkout of the parent alone leaves ffi/webgpu-headers empty and bindgen
# fails on '#include "webgpu.h"' on every target.
git -C "$src" submodule sync --recursive
git -C "$src" submodule update --init --recursive --depth 1

# Keep the ABI pin, adding only the AppKit-view surface entry point required
# by Vulkan portability. The upstream MetalLayer entry point is Metal-only.
cp "$repo/tools/wgpu/prime-appkit-surface.rs" "$src/prime-appkit-surface.rs"
if ! grep -Fq 'prime-appkit-surface.rs' "$src/src/lib.rs"; then
    printf '\ninclude!(concat!(env!("CARGO_MANIFEST_DIR"), "/prime-appkit-surface.rs"));\n' >> "$src/src/lib.rs"
fi

export CARGO_TARGET_DIR="$target"
features=wgsl,glsl

case "$requested" in
    android-arm64)
        triple=aarch64-linux-android
        abi=arm64-v8a
        api=24
        out="$repo/artifacts/wgpu-native-android/lib/$abi"
        ndk="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}"
        [[ -n "$ndk" && -d "$ndk" ]] || {
            echo "error: set ANDROID_NDK_ROOT (or ANDROID_NDK_HOME) to an installed Android NDK" >&2
            exit 1
        }
        host=$(find "$ndk/toolchains/llvm/prebuilt" -mindepth 1 -maxdepth 1 -type d | head -1)
        linker="$host/bin/aarch64-linux-android${api}-clang"
        [[ -x "$linker" ]] || { echo "error: Android linker not found: $linker" >&2; exit 1; }
        rustup target add "$triple"
        export CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER="$linker"
        # bindgen is a host process. Point clang at the Android sysroot too,
        # otherwise it follows /usr/include/stdint.h into host glibc headers
        # and dies on bits/libc-header-start.h while cross-compiling.
        export BINDGEN_EXTRA_CLANG_ARGS_aarch64_linux_android="--target=aarch64-linux-android${api} --sysroot=$host/sysroot"
        export RUSTFLAGS="-C link-arg=-Wl,-z,max-page-size=16384 -C link-arg=-Wl,-z,common-page-size=16384"
        cargo build --manifest-path "$src/Cargo.toml" --locked --release --target "$triple"             --no-default-features --features "$features"
        mkdir -p "$out"
        cp "$target/$triple/release/libwgpu_native.so" "$out/libwgpu_native.so"
        ;;
    android-x64)
        triple=x86_64-linux-android
        abi=x86_64
        api=24
        out="$repo/artifacts/wgpu-native-android/lib/$abi"
        ndk="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}"
        [[ -n "$ndk" && -d "$ndk" ]] || {
            echo "error: set ANDROID_NDK_ROOT (or ANDROID_NDK_HOME) to an installed Android NDK" >&2
            exit 1
        }
        host=$(find "$ndk/toolchains/llvm/prebuilt" -mindepth 1 -maxdepth 1 -type d | head -1)
        linker="$host/bin/x86_64-linux-android${api}-clang"
        [[ -x "$linker" ]] || { echo "error: Android linker not found: $linker" >&2; exit 1; }
        rustup target add "$triple"
        export CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER="$linker"
        export BINDGEN_EXTRA_CLANG_ARGS_x86_64_linux_android="--target=x86_64-linux-android${api} --sysroot=$host/sysroot"
        export RUSTFLAGS="-C link-arg=-Wl,-z,max-page-size=16384 -C link-arg=-Wl,-z,common-page-size=16384"
        cargo build --manifest-path "$src/Cargo.toml" --locked --release --target "$triple"             --no-default-features --features "$features"
        mkdir -p "$out"
        cp "$target/$triple/release/libwgpu_native.so" "$out/libwgpu_native.so"
        ;;
    osx-arm64)
        [[ $(uname -s) == Darwin ]] || { echo "error: macOS target must be built on macOS" >&2; exit 1; }
        triple=aarch64-apple-darwin
        rid=osx-arm64
        out="$repo/artifacts/wgpu-native-macos/$rid"
        rustup target add "$triple"
        cargo build --manifest-path "$src/Cargo.toml" --locked --release --target "$triple"             --no-default-features --features "$features,metal,vulkan-portability"
        mkdir -p "$out"
        cp "$target/$triple/release/libwgpu_native.dylib" "$out/libwgpu_native.dylib"
        ;;
    osx-x64)
        [[ $(uname -s) == Darwin ]] || { echo "error: macOS target must be built on macOS" >&2; exit 1; }
        triple=x86_64-apple-darwin
        rid=osx-x64
        out="$repo/artifacts/wgpu-native-macos/$rid"
        rustup target add "$triple"
        cargo build --manifest-path "$src/Cargo.toml" --locked --release --target "$triple"             --no-default-features --features "$features,metal,vulkan-portability"
        mkdir -p "$out"
        cp "$target/$triple/release/libwgpu_native.dylib" "$out/libwgpu_native.dylib"
        ;;
    *)
        usage
        ;;
esac

cat > "$out/WGPU-NATIVE.txt" <<EOF
Project Prime wgpu-native ABI pin
commit=$commit
target=$requested
features=$features
EOF

echo "wgpu-native $requested -> $out"
