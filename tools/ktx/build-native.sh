#!/usr/bin/env bash
set -euo pipefail

target="${1:?usage: tools/ktx/build-native.sh <osx-arm64|osx-x64|android-arm64|android-x64>}"
revision="6b3d8bf15788f604c6b91dd95fafabb6c59cd723"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="${root}/artifacts/ktx-source"
source_dir="${work}/KTX-Software"
build_dir="${work}/build-${target}"

if [ ! -d "${source_dir}/.git" ]; then
  rm -rf "${source_dir}"
  git clone --depth 1 --recurse-submodules --shallow-submodules     https://github.com/BoyBaykiller/KTX-Software.git "${source_dir}"
fi

if [ "$(git -C "${source_dir}" rev-parse HEAD)" != "${revision}" ]; then
  git -C "${source_dir}" fetch --depth 1 origin "${revision}"
  git -C "${source_dir}" checkout --detach "${revision}"
  git -C "${source_dir}" submodule update --init --recursive --depth 1
fi

common=(
  -G Ninja
  -DCMAKE_BUILD_TYPE=Release
  -DBUILD_SHARED_LIBS=ON
  -DKTX_FEATURE_TOOLS=OFF
  -DKTX_FEATURE_TESTS=OFF
  -DKTX_FEATURE_TOOLS_CTS=OFF
  -DKTX_FEATURE_LOADTEST_APPS=OFF
  -DKTX_FEATURE_DOC=OFF
  -DKTX_FEATURE_GL_UPLOAD=OFF
  -DKTX_FEATURE_VK_UPLOAD=OFF
  -DBASISU_SUPPORT_OPENCL=OFF
)

rm -rf "${build_dir}"

case "${target}" in
  osx-arm64|osx-x64)
    arch="arm64"
    [ "${target}" = "osx-x64" ] && arch="x86_64"
    cmake -S "${source_dir}" -B "${build_dir}" "${common[@]}"       -DCMAKE_OSX_ARCHITECTURES="${arch}"
    cmake --build "${build_dir}" --target ktx --parallel
    library="$(find "${build_dir}" -type f -name 'libktx*.dylib' | head -1)"
    [ -n "${library}" ] || { echo "libktx.dylib was not produced" >&2; exit 1; }
    out="${root}/artifacts/ktx-native-macos/${target}"
    mkdir -p "${out}"
    cp "${library}" "${out}/libktx.dylib"
    install_name_tool -id libktx.dylib "${out}/libktx.dylib"
    codesign --force --sign - "${out}/libktx.dylib"
    ;;
  android-arm64|android-x64)
    ndk="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}"
    [ -n "${ndk}" ] || { echo "ANDROID_NDK_ROOT is required" >&2; exit 1; }
    abi="arm64-v8a"
    [ "${target}" = "android-x64" ] && abi="x86_64"
    cmake -S "${source_dir}" -B "${build_dir}" "${common[@]}"       -DANDROID_ABI="${abi}"       -DANDROID_PLATFORM=android-24       -DANDROID_NDK="${ndk}"       -DCMAKE_TOOLCHAIN_FILE="${ndk}/build/cmake/android.toolchain.cmake"       -DBASISU_SUPPORT_SSE=OFF
    cmake --build "${build_dir}" --target ktx --parallel
    library="$(find "${build_dir}" -type f -name 'libktx.so' | head -1)"
    [ -n "${library}" ] || { echo "libktx.so was not produced" >&2; exit 1; }
    out="${root}/artifacts/ktx-native-android/lib/${abi}"
    mkdir -p "${out}"
    cp "${library}" "${out}/libktx.so"
    ;;
  *)
    echo "unsupported target: ${target}" >&2
    exit 2
    ;;
esac

echo "KTX runtime ${revision} ready for ${target}"
