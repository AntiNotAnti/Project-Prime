#!/usr/bin/env bash
set -euo pipefail
[[ $# == 5 ]] || { echo 'usage: package-macos.sh <publish-directory> <dist-directory> <rid> <version> <studio-publish-directory>' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: packaging requires macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
root=$(cd "$1" && pwd)
mkdir -p "$2"
dist=$(cd "$2" && pwd)
studio_root=$(cd "$5" && pwd)
rid=$3
version=${4#v}
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'error: version must be X.Y.Z' >&2; exit 1; }
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'error: unsupported RID' >&2; exit 1 ;; esac
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
app="$stage/package/Project Prime.app"
contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources"
# Keep the publish layout intact: .NET and native dependencies probe beside
# the apphost. Apple's signer treats subdirectories of MacOS as nested code,
# so non-code data belongs in Resources, resolved by AppPaths.ResourceDirectory.
ditto "$root" "$contents/MacOS"
[[ ! -e "$contents/MacOS/.project-prime-update-transaction" ]] || { echo 'error: Game publish contains an unfinished update' >&2; exit 1; }
rm -f "$contents/MacOS/.project-prime-update.lock" "$contents/MacOS/.project-prime-in-use.lock"
if [[ -d "$contents/MacOS/maps" ]]; then
    mv "$contents/MacOS/maps" "$contents/Resources/maps"
fi
if [[ -d "$contents/MacOS/fidelity-baselines" ]]; then
    mv "$contents/MacOS/fidelity-baselines" "$contents/Resources/fidelity-baselines"
fi
for resource in gamecontrollerdb.txt gamecontrollerdb.LICENSE; do
    if [[ -f "$contents/MacOS/$resource" ]]; then
        mv "$contents/MacOS/$resource" "$contents/Resources/$resource"
    fi
done
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
# Studio is a distinct sibling application, never a helper hidden in Game.app.
studio_app="$stage/package/Project Prime Studio.app"
studio_contents="$studio_app/Contents"
mkdir -p "$studio_contents/MacOS" "$studio_contents/Resources"
ditto "$studio_root" "$studio_contents/MacOS"
[[ ! -e "$studio_contents/MacOS/.project-prime-update-transaction" ]] || { echo 'error: Studio publish contains an unfinished update' >&2; exit 1; }
rm -f "$studio_contents/MacOS/.project-prime-update.lock" "$studio_contents/MacOS/.project-prime-in-use.lock"
for resource in maps fidelity-baselines gamecontrollerdb.txt gamecontrollerdb.LICENSE; do
    if [[ -e "$studio_contents/MacOS/$resource" ]]; then
        mv "$studio_contents/MacOS/$resource" "$studio_contents/Resources/$resource"
    fi
done
cp "$contents/Info.plist" "$studio_contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleName Project Prime Studio' "$studio_contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleDisplayName Project Prime Studio' "$studio_contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier com.projectprime.studio' "$studio_contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleExecutable ProjectPrimeStudio' "$studio_contents/Info.plist"
cp "$contents/Resources/ProjectPrime.icns" "$studio_contents/Resources/ProjectPrime.icns"
chmod +x "$studio_contents/MacOS/ProjectPrimeStudio"
"$repo/tools/sign-macos.sh" "$studio_contents/MacOS" ProjectPrimeStudio
codesign --force --sign - --entitlements "$repo/src/MphRead/Platforms/macOS/ProjectPrime.entitlements" "$studio_app"
codesign --verify --deep --strict --verbose=4 "$studio_app"
# Signing changes apphost bytes. Bind the final sealed executables, then write
# the complete ownership manifest outside both resource-sealed bundles.
rm -f "$stage/package/.project-prime-update.lock" "$stage/package/.project-prime-in-use.lock"
python3 "$repo/tools/studio-pair.py" metadata "$stage/package" "$version"
python3 "$repo/tools/write-release-manifest.py" "$stage/package"
python3 "$repo/tools/paired-install-check.py" "$stage/package" --version "$version" --rid "$rid"
"$repo/tools/check-no-game-assets.sh" "$stage/package"
archive="$stage/ProjectPrime-v$version-$rid.tar.gz"
COPYFILE_DISABLE=1 tar -czf "$archive" -C "$stage/package" .
# Validate what a player extracts, including mode bits and resource seals.
mkdir "$stage/extracted"
tar -xzf "$archive" -C "$stage/extracted"
codesign --verify --deep --strict --verbose=4 "$stage/extracted/Project Prime.app"
"$repo/tools/run-macos-smoke.sh" "$stage/extracted/Project Prime.app/Contents/MacOS" "$rid"
codesign --verify --deep --strict --verbose=4 "$stage/extracted/Project Prime Studio.app"
python3 "$repo/tools/paired-install-check.py" "$stage/extracted" --version "$version" --rid "$rid"
cp "$archive" "$dist/"
