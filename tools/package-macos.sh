#!/usr/bin/env bash
set -euo pipefail
[[ $# == 4 ]] || { echo 'usage: package-macos.sh <publish-directory> <dist-directory> <rid> <version>' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: packaging requires macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
root=$(cd "$1" && pwd)
mkdir -p "$2"
dist=$(cd "$2" && pwd)
rid=$3
version=${4#v}
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'error: version must be X.Y.Z' >&2; exit 1; }
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'error: unsupported RID' >&2; exit 1 ;; esac
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
app="$stage/package/Project Prime.app"
contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources"
# Keep the apphost, .NET runtime metadata and native dependencies beside one
# another: moving the DLLs or dylibs breaks .NET/native probing. Non-code asset
# directories belong in Contents/Resources, however; macOS codesign treats
# everything nested under Contents/MacOS as executable code.
ditto "$root" "$contents/MacOS"
for resource_dir in maps fidelity-baselines rmlui licenses; do
    if [[ -d "$contents/MacOS/$resource_dir" ]]; then
        mv "$contents/MacOS/$resource_dir" "$contents/Resources/$resource_dir"
    fi
done
# The native provenance manifest accompanies the DLL in a flat publish so
# verify-runtime.py can validate its hash before packaging. In a signed .app
# it is non-executable metadata: Apple's code seal requires it in Resources,
# while the dylib stays in MacOS for the runtime loader.
for resource in PRIME-RMLUI.json gamecontrollerdb.txt gamecontrollerdb.LICENSE; do
    if [[ -f "$contents/MacOS/$resource" ]]; then
        mv "$contents/MacOS/$resource" "$contents/Resources/$resource"
    fi
done
# Project references may also publish loose documentation at the flat root,
# including the corpse-physics THIRD-PARTY-NOTICES.md. Apple's code signer
# treats any document inside Contents/MacOS as an unsigned code component.
# Move root-level text/Markdown notices into the signed Resources seal, but
# leave managed .deps.json/.runtimeconfig.json and native libraries adjacent
# to the executable so normal .NET/native runtime probing is unchanged.
for notice in "$contents/MacOS"/*.md "$contents/MacOS"/*.txt; do
    [[ -f "$notice" ]] || continue
    mv "$notice" "$contents/Resources/"
done
# Additional project references can publish loose managed PDBs even when the
# apphost uses embedded symbols. They are optional debug data, not Mach-O code;
# leaving them under MacOS causes the entire app signature to fail.
find "$contents/MacOS" -type f -name '*.pdb' -delete
cp "$repo/src/MphRead/Platforms/macOS/Info.plist" "$contents/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleShortVersionString string $version" "$contents/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleVersion string $version" "$contents/Info.plist"
plutil -lint "$contents/Info.plist"
iconset="$stage/ProjectPrime.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$repo/src/MphRead/Assets/project-prime-mark.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$repo/src/MphRead/Assets/project-prime-mark.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$contents/Resources/ProjectPrime.icns"
cp "$repo/LICENSE" "$stage/package/LICENSE"
cp "$repo/tools/macos-README.txt" "$stage/package/README.txt"
chmod +x "$contents/MacOS/ProjectPrime"
"$repo/tools/sign-macos.sh" "$contents/MacOS"
codesign --force --sign - --entitlements "$repo/src/MphRead/Platforms/macOS/ProjectPrime.entitlements" "$app"
codesign --verify --deep --strict --verbose=4 "$app"
"$repo/tools/check-macos-build.sh" "$contents/MacOS" "$rid"
"$repo/tools/check-no-game-assets.sh" "$stage/package"
"$repo/tools/check-maps-shipped.sh" "$contents/Resources"
"$repo/tools/run-macos-smoke.sh" "$contents/MacOS" "$rid"
archive="$stage/ProjectPrime-v$version-$rid.tar.gz"
COPYFILE_DISABLE=1 tar -czf "$archive" -C "$stage/package" .
# Validate what a player extracts, including mode bits and resource seals.
mkdir "$stage/extracted"
tar -xzf "$archive" -C "$stage/extracted"
codesign --verify --deep --strict --verbose=4 "$stage/extracted/Project Prime.app"
"$repo/tools/run-macos-smoke.sh" "$stage/extracted/Project Prime.app/Contents/MacOS" "$rid"
cp "$archive" "$dist/"
