#!/usr/bin/env bash
set -euo pipefail
[[ $# == 5 ]] || { echo 'usage: package-macos.sh <game-publish-directory> <dist-directory> <rid> <version> <studio-publish-directory>' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: packaging requires macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
game_root=$(cd "$1" && pwd)
mkdir -p "$2"
dist=$(cd "$2" && pwd)
rid=$3
version=${4#v}
studio_root=$(cd "$5" && pwd)
[[ "$game_root" != "$studio_root" ]] || { echo 'error: game and Studio require separate publish directories' >&2; exit 1; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'error: version must be X.Y.Z' >&2; exit 1; }
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'error: unsupported RID' >&2; exit 1 ;; esac
[[ -x "$game_root/ProjectPrime" && -x "$studio_root/ProjectPrimeStudio" ]] || { echo 'error: paired desktop executables are required' >&2; exit 1; }
# Both apphosts and Studio's referenced engine must carry the exact release version.
python3 "$repo/tools/studio-pair.py" smoke "$game_root/ProjectPrime" "$studio_root/ProjectPrimeStudio" "$version"
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/package"
python3 "$repo/tools/studio-pair.py" metadata "$stage/package" "$version"
iconset="$stage/ProjectPrime.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$repo/src/MphRead/Assets/project-prime-mark.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$repo/src/MphRead/Assets/project-prime-mark.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$stage/ProjectPrime.icns"

for executable in ProjectPrime ProjectPrimeStudio; do
    if [[ "$executable" == ProjectPrime ]]; then
        root="$game_root"
        app="$stage/package/Project Prime.app"
        platform="$repo/src/MphRead/Platforms/macOS"
    else
        root="$studio_root"
        app="$stage/package/Project Prime Studio.app"
        platform="$repo/src/ProjectPrime.Studio/Platforms/macOS"
    fi
    contents="$app/Contents"
    mkdir -p "$contents/MacOS" "$contents/Resources"
    # .NET/native dependencies probe beside each own apphost. Apple treats
    # non-code subdirectories in MacOS as nested code, so move data to Resources.
    ditto "$root" "$contents/MacOS"
    for resource in maps fidelity-baselines gamecontrollerdb.txt gamecontrollerdb.LICENSE PRIME-WGPU.json; do
        if [[ -e "$contents/MacOS/$resource" ]]; then
            mv "$contents/MacOS/$resource" "$contents/Resources/$resource"
        fi
    done
    # Portable PDBs are data. Keeping them in the executable directory makes
    # strict bundle verification classify them as unsigned nested code.
    for symbols in "$contents/MacOS"/*.pdb; do
        [[ -f "$symbols" ]] || continue
        mkdir -p "$contents/Resources/DebugSymbols"
        mv "$symbols" "$contents/Resources/DebugSymbols/"
    done
    # Studio's single-file runtime configuration is embedded. The SDK also
    # copies its referenced engine's configuration, which is diagnostic data
    # because this app has no ProjectPrime apphost.
    if [[ "$executable" == ProjectPrimeStudio && -f "$contents/MacOS/ProjectPrime.runtimeconfig.json" ]]; then
        mv "$contents/MacOS/ProjectPrime.runtimeconfig.json" "$contents/Resources/"
    fi
    if [[ -f "$contents/MacOS/MoltenVK_icd.json" ]]; then
        mv "$contents/MacOS/MoltenVK_icd.json" "$contents/Resources/MoltenVK_icd.json"
        /usr/bin/python3 - "$contents/Resources/MoltenVK_icd.json" <<'PY'
import json, pathlib, sys
path = pathlib.Path(sys.argv[1])
data = json.loads(path.read_text())
data["ICD"]["library_path"] = "../MacOS/libMoltenVK.dylib"
path.write_text(json.dumps(data, indent=4) + "\n")
PY
    fi
    cp "$platform/Info.plist" "$contents/Info.plist"
    /usr/libexec/PlistBuddy -c "Add :CFBundleShortVersionString string $version" "$contents/Info.plist"
    /usr/libexec/PlistBuddy -c "Add :CFBundleVersion string $version" "$contents/Info.plist"
    plutil -lint "$contents/Info.plist"
    cp "$stage/ProjectPrime.icns" "$contents/Resources/$executable.icns"
    cp "$stage/package/.project-prime-desktop.json" "$contents/Resources/.project-prime-desktop.json"
    chmod +x "$contents/MacOS/$executable"
    "$repo/tools/sign-macos.sh" "$contents/MacOS" "$executable"
    codesign --force --sign - --entitlements "$platform/$executable.entitlements" "$app"
    codesign --verify --deep --strict --verbose=4 "$app"
    "$repo/tools/check-macos-build.sh" "$contents/MacOS" "$rid" "$executable"
    if [[ "$executable" == ProjectPrime ]]; then
        "$repo/tools/check-maps-shipped.sh" "$contents/Resources"
        "$repo/tools/run-macos-smoke.sh" "$contents/MacOS" "$rid"
    fi
done
cp "$repo/LICENSE" "$stage/package/LICENSE"
cp "$repo/tools/macos-README.txt" "$stage/package/README.txt"
"$repo/tools/check-no-game-assets.sh" "$stage/package"
python3 "$repo/tools/studio-pair.py" smoke \
    "$stage/package/Project Prime.app/Contents/MacOS/ProjectPrime" \
    "$stage/package/Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio" "$version"
archive="$stage/ProjectPrime-v$version-$rid.tar.gz"
COPYFILE_DISABLE=1 tar -czf "$archive" -C "$stage/package" .
# Validate the actual extracted archive, including both seals, architectures,
# entitlement ownership and their resolved sibling app layout.
mkdir "$stage/extracted"
tar -xzf "$archive" -C "$stage/extracted"
for name in 'Project Prime' 'Project Prime Studio'; do
    codesign --verify --deep --strict --verbose=4 "$stage/extracted/$name.app"
done
"$repo/tools/run-macos-smoke.sh" "$stage/extracted/Project Prime.app/Contents/MacOS" "$rid"
python3 "$repo/tools/studio-pair.py" smoke \
    "$stage/extracted/Project Prime.app/Contents/MacOS/ProjectPrime" \
    "$stage/extracted/Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio" "$version"
cp "$archive" "$dist/"
