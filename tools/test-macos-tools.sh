#!/usr/bin/env bash
# Negative regressions for the gates used by both workflows; no game assets.
set -euo pipefail
[[ $(uname -s) == Darwin ]] || { echo 'error: tests require macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
temp=$(mktemp -d)
trap 'rm -rf "$temp"' EXIT
case "$(uname -m)" in
    arm64) rid=osx-arm64; other=x86_64 ;;
    x86_64) rid=osx-x64; other=arm64 ;;
    *) exit 1 ;;
esac
project="$repo/src/MphRead/MphRead.csproj"
[[ $(dotnet msbuild "$project" -getProperty:MphReadAudioRid) == "$rid" ]] \
    || { echo 'error: local build selected the wrong audio RID' >&2; exit 1; }
for target in osx-arm64 osx-x64; do
    [[ $(dotnet msbuild "$project" -p:RuntimeIdentifier="$target" -getProperty:MphReadAudioRid) == "$target" ]] \
        || { echo 'error: explicit audio RID was ignored' >&2; exit 1; }
done
root="$temp/publish with spaces"
mkdir -p "$root/nested"
# This synthetic packaging fixture acknowledges the wrapper's success marker;
# the publish jobs separately run the real launcher's rendered window check.
cat > "$temp/main.c" <<'C'
#include <stdio.h>
#include <string.h>
#ifndef RELEASE_VERSION
#define RELEASE_VERSION "1.2.3"
#endif
int main(int argc, char **argv) {
    if (argc > 1 && (!strcmp(argv[1], "-studioversion") || !strcmp(argv[1], "--version"))) {
        printf("{\"gameVersion\":\"%s\",\"studioVersion\":\"%s\",\"studioIpcVersion\":1}\n",
               RELEASE_VERSION, RELEASE_VERSION);
        return 0;
    }
    puts("Launcher window check passed.");
    puts("GLFW extraction path check passed.");
    puts("Thumbnail window check passed.");
    return 0;
}
C
printf 'int native_probe(void) { return 42; }\n' > "$temp/native.c"
clang "$temp/main.c" -o "$root/ProjectPrime"
clang -dynamiclib "$temp/native.c" -o "$root/libopenal.1.dylib"
clang -dynamiclib "$temp/native.c" -o "$root/libwgpu_native.dylib"
clang -dynamiclib "$temp/native.c" -o "$root/libktx.dylib"
clang -dynamiclib "$temp/native.c" -o "$root/nested/extensionless-native"
"$repo/tools/sign-macos.sh" "$root"
"$repo/tools/check-macos-build.sh" "$root" "$rid"
expect_failure() {
    if "$@" > "$temp/failure.log" 2>&1; then
        cat "$temp/failure.log"
        echo "error: unexpectedly accepted: $*" >&2
        exit 1
    fi
    cat "$temp/failure.log"
}
expect_failure "$repo/tools/check-macos-build.sh" "$root" unsupported
chmod -x "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
chmod +x "$root/ProjectPrime"
mv "$root/libopenal.1.dylib" "$temp/openal"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
mv "$temp/openal" "$root/libopenal.1.dylib"
for library in libwgpu_native.dylib libktx.dylib; do
    mv "$root/$library" "$temp/$library"
    expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
    mv "$temp/$library" "$root/$library"
done
clang -arch "$other" -dynamiclib "$temp/native.c" -o "$root/nested/wrong.dylib"
codesign --force --sign - "$root/nested/wrong.dylib"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
rm "$root/nested/wrong.dylib"
codesign --remove-signature "$root/nested/extensionless-native"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
"$repo/tools/sign-macos.sh" "$root"
# A fresh executable proves the missing-entitlement case independently of
# any metadata preserved while replacing an existing code signature.
clang "$temp/main.c" -o "$root/NoJit"
codesign --force --sign - "$root/NoJit"
mv "$root/NoJit" "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
printf '<plist version="1.0"><dict><key>com.apple.security.cs.allow-jit</key><false/></dict></plist>' > "$temp/false.plist"
codesign --force --sign - --entitlements "$temp/false.plist" "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
# A compatible universal library must pass, including both signed slices.
clang -arch "$other" -dynamiclib "$temp/native.c" -o "$temp/other.dylib"
lipo -create "$root/libopenal.1.dylib" "$temp/other.dylib" -output "$temp/universal.dylib"
mv "$temp/universal.dylib" "$root/libopenal.1.dylib"
"$repo/tools/sign-macos.sh" "$root"
"$repo/tools/check-macos-build.sh" "$root" "$rid"
echo 'macOS signing gate regressions passed.'

# Reproduce the full app layout, including non-code map data. A flat publish
# can sign and launch successfully while its enclosing app cannot be signed.
fixture="$temp/package-input"
mkdir -p "$fixture/maps" "$fixture/fidelity-baselines"
cp "$root/ProjectPrime" "$root/libopenal.1.dylib" "$root/libwgpu_native.dylib" "$root/libktx.dylib" "$fixture/"
# Synthetic data keeps the packaging gate independent of controller/fidelity feature branches.
printf '# Controller mapping packaging fixture\n' > "$fixture/gamecontrollerdb.txt"
printf 'Controller mapping license fixture\n' > "$fixture/gamecontrollerdb.LICENSE"
printf '{"Name":"PACKAGING TEST"}\n' > "$fixture/maps/fixture.json"
printf '{"fixture":true}\n' > "$fixture/fidelity-baselines/fixture.json"
studio="$temp/studio package input"
mkdir -p "$studio"
clang "$temp/main.c" -o "$studio/ProjectPrimeStudio"
cp "$root/libopenal.1.dylib" "$root/libwgpu_native.dylib" "$root/libktx.dylib" "$studio/"
"$repo/tools/sign-macos.sh" "$studio" ProjectPrimeStudio
"$repo/tools/check-macos-build.sh" "$studio" "$rid" ProjectPrimeStudio
# The paired archive must reject a missing Studio, mismatched version, and a
# Studio apphost without its own JIT entitlement before publishing anything.
mv "$studio/ProjectPrimeStudio" "$temp/studio-good"
expect_failure "$repo/tools/package-macos.sh" "$fixture" "$temp/dist" "$rid" 1.2.3 "$studio"
clang -DRELEASE_VERSION='"1.2.4"' "$temp/main.c" -o "$studio/ProjectPrimeStudio"
expect_failure "$repo/tools/package-macos.sh" "$fixture" "$temp/dist" "$rid" 1.2.3 "$studio"
mv "$temp/studio-good" "$studio/ProjectPrimeStudio"
codesign --remove-signature "$studio/ProjectPrimeStudio"
codesign --force --sign - "$studio/ProjectPrimeStudio"
expect_failure "$repo/tools/check-macos-build.sh" "$studio" "$rid" ProjectPrimeStudio
"$repo/tools/sign-macos.sh" "$studio" ProjectPrimeStudio
"$repo/tools/package-macos.sh" "$fixture" "$temp/dist" "$rid" 1.2.3 "$studio"
mkdir "$temp/unpacked"
tar -xzf "$temp/dist/ProjectPrime-v1.2.3-$rid.tar.gz" -C "$temp/unpacked"
app="$temp/unpacked/Project Prime.app"
studio_app="$temp/unpacked/Project Prime Studio.app"
[[ -x "$studio_app/Contents/MacOS/ProjectPrimeStudio" ]] || exit 1
[[ $(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$studio_app/Contents/Info.plist") == com.projectprime.studio ]] || exit 1
[[ $(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$studio_app/Contents/Info.plist") == 1.2.3 ]] || exit 1
cmp "$temp/unpacked/.project-prime-desktop.json" "$app/Contents/Resources/.project-prime-desktop.json"
cmp "$temp/unpacked/.project-prime-desktop.json" "$studio_app/Contents/Resources/.project-prime-desktop.json"
codesign --verify --deep --strict "$studio_app"
python3 "$repo/tools/studio-pair.py" smoke "$app/Contents/MacOS/ProjectPrime" "$studio_app/Contents/MacOS/ProjectPrimeStudio" 1.2.3
[[ -f "$app/Contents/Resources/maps/fixture.json" ]] || exit 1
[[ ! -e "$app/Contents/MacOS/maps" ]] || exit 1
cmp "$fixture/fidelity-baselines/fixture.json" "$app/Contents/Resources/fidelity-baselines/fixture.json"
[[ ! -e "$app/Contents/MacOS/fidelity-baselines" ]] || exit 1
for resource in gamecontrollerdb.txt gamecontrollerdb.LICENSE; do
    cmp "$fixture/$resource" "$app/Contents/Resources/$resource"
    [[ ! -e "$app/Contents/MacOS/$resource" ]] || exit 1
done
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/gamecontrollerdb.txt"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/gamecontrollerdb.txt" "$app/Contents/Resources/gamecontrollerdb.txt"
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/maps/fixture.json"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/maps/fixture.json" "$app/Contents/Resources/maps/fixture.json"
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/fidelity-baselines/fixture.json"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/fidelity-baselines/fixture.json" "$app/Contents/Resources/fidelity-baselines/fixture.json"
codesign --verify --deep --strict "$app"
if [[ ${PROJECT_PRIME_SKIP_PLATFORM_TEST:-false} != true ]]; then
    dotnet run --project "$repo/tools/platformtest/platformtest.csproj" -c Release
fi
echo 'macOS bundle resource regressions passed.'
