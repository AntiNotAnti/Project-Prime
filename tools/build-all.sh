#!/usr/bin/env bash
# Build and validate all Project Prime deployable artifacts in one transaction.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
SKIP_ANDROID=0
VERSION=""
OUTPUT=""

usage() {
    cat <<'USAGE'
Usage: ./build-all.sh [--version VERSION] [--output DIRECTORY] [--skip-android]

Cooks every custom map once, publishes all deployable desktop clients,
packages every release server RID plus a local/dev Apple Silicon server, and
builds Android by default. The final directory appears only after all outputs
have passed package, map, proprietary-asset, and Windows subsystem checks.

  --version VERSION   stamp all packages (default: local timestamp version)
  --output DIRECTORY  final output (default: publish/full-TIMESTAMP)
  --skip-android      omit Android when its workload/JDK/SDK are unavailable
  --help              show this help

Optional Android release signing requires all four environment variables:
PRIME_ANDROID_KEYSTORE_FILE, PRIME_ANDROID_KEYSTORE_PASSWORD,
PRIME_ANDROID_KEY_ALIAS, and PRIME_ANDROID_KEY_PASSWORD. With none set, the
APK uses the local development/debug signing identity.
USAGE
}

while (($#)); do
    case "$1" in
        --version) [[ $# -ge 2 ]] || { echo "--version needs a value." >&2; exit 2; }; VERSION=$2; shift 2 ;;
        --output) [[ $# -ge 2 ]] || { echo "--output needs a path." >&2; exit 2; }; OUTPUT=$2; shift 2 ;;
        --skip-android) SKIP_ANDROID=1; shift ;;
        --help|-h) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

BUILD_TIMESTAMP=$(date -u +%Y%m%d-%H%M%S)
if [[ -z "$VERSION" ]]; then VERSION=0.0.0-local.$(date -u +%Y%m%d%H%M%S); fi
if [[ ! "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z.+-]*$ ]]; then
    echo "Version must contain only letters, digits, '.', '+' and '-' and start with a letter or digit." >&2
    exit 2
fi
if [[ -z "$OUTPUT" ]]; then OUTPUT=$ROOT/publish/full-$BUILD_TIMESTAMP; fi
OUTPUT=$(python3 - "$OUTPUT" <<'PY'
import os
import sys
print(os.path.realpath(os.path.abspath(sys.argv[1])))
PY
)
[[ "$OUTPUT" != / ]] || { echo "Refusing to use the filesystem root as output." >&2; exit 2; }
[[ ! -e "$OUTPUT" ]] || { echo "Refusing to overwrite existing output: $OUTPUT" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required command: $1" >&2; exit 1; }; }
for command_name in bash python3 dotnet unzip strings; do need "$command_name"; done
SDK_VERSION=$(dotnet --version)
case "$SDK_VERSION" in
    10.*) ;;
    *) echo "Project Prime requires .NET SDK 10.x; found $SDK_VERSION." >&2; exit 1 ;;
esac

ANDROID_SIGNING_STATUS=omitted
ANDROID_SDK=""
if [[ "$SKIP_ANDROID" -eq 0 ]]; then
    if ! dotnet workload list | grep -E '^[[:space:]]*android([[:space:]]|$)' >/dev/null; then
        echo "Android was requested, but the .NET android workload is not installed." >&2
        echo "Install it with 'dotnet workload install android' or pass --skip-android." >&2
        exit 1
    fi
    need java
    ANDROID_SDK=${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}
    [[ -n "$ANDROID_SDK" && -d "$ANDROID_SDK" ]] || {
        echo "Android was requested, but ANDROID_HOME or ANDROID_SDK_ROOT is not a valid directory." >&2
        exit 1
    }
    if [[ -n "${JAVA_HOME:-}" && ! -x "$JAVA_HOME/bin/java" ]]; then
        echo "JAVA_HOME does not contain an executable bin/java." >&2
        exit 1
    fi
    signing_count=0
    for signing_value in "${PRIME_ANDROID_KEYSTORE_FILE:-}" "${PRIME_ANDROID_KEYSTORE_PASSWORD:-}" \
        "${PRIME_ANDROID_KEY_ALIAS:-}" "${PRIME_ANDROID_KEY_PASSWORD:-}"; do
        if [[ -n "$signing_value" ]]; then signing_count=$((signing_count + 1)); fi
    done
    if [[ "$signing_count" -ne 0 && "$signing_count" -ne 4 ]]; then
        echo "Android signing is partially configured; set all four PRIME_ANDROID_* signing variables or none." >&2
        exit 1
    fi
    if [[ "$signing_count" -eq 4 ]]; then
        case "$PRIME_ANDROID_KEYSTORE_FILE" in
            /*) ;;
            *) echo "PRIME_ANDROID_KEYSTORE_FILE must be an absolute path." >&2; exit 1 ;;
        esac
        [[ -f "$PRIME_ANDROID_KEYSTORE_FILE" && ! -L "$PRIME_ANDROID_KEYSTORE_FILE" && -r "$PRIME_ANDROID_KEYSTORE_FILE" ]] || {
            echo "PRIME_ANDROID_KEYSTORE_FILE must be a readable regular file, not a symlink." >&2
            exit 1
        }
        ANDROID_SIGNING_STATUS=local-release
    else
        ANDROID_SIGNING_STATUS=development-debug
    fi
fi

OUTPUT_PARENT=$(dirname "$OUTPUT")
OUTPUT_NAME=$(basename "$OUTPUT")
mkdir -p "$OUTPUT_PARENT"
STAGE=$(mktemp -d "$OUTPUT_PARENT/.$OUTPUT_NAME.stage.XXXXXX")
BUILD_COMPLETE=0
cleanup() {
    cleanup_status=$?
    trap - EXIT INT TERM
    if [[ "$BUILD_COMPLETE" -eq 0 && -d "$STAGE" ]]; then
        incomplete=$OUTPUT.incomplete
        if [[ -e "$incomplete" ]]; then incomplete=$OUTPUT.incomplete.$BUILD_TIMESTAMP.$$; fi
        mv "$STAGE" "$incomplete"
        echo "Build did not complete; partial artifacts were preserved at $incomplete" >&2
    fi
    exit "$cleanup_status"
}
trap cleanup EXIT INT TERM

cd "$ROOT"
echo "Validating repository boundaries and generated sources..."
bash tools/check-no-game-assets.sh
python3 tools/check-project-boundaries.py
python3 tools/check-render-shaders.py
python3 tools/check-multiplayer-only.py
git diff --check

echo "Cooking all custom map bundles once..."
dotnet run --project src/Tools/Tools.csproj -c Release -- -mapdir maps -mapbundle all
bash tools/check-maps-shipped.sh
MAP_COUNT=$(find maps -type f -name '*.fpmap' | wc -l | tr -d '[:space:]')
[[ "$MAP_COUNT" -gt 0 ]] || { echo "Map cook produced no .fpmap bundles." >&2; exit 1; }

STAMP_ARGS=(-p:Version="$VERSION" -p:InformationalVersion="$VERSION")
for rid in win-x64 linux-x64 osx-x64 osx-arm64; do
    destination=$STAGE/clients/$rid
    echo "Publishing desktop client $rid..."
    dotnet publish src/Client/Client.csproj -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true -p:SkipCustomMapBundleCook=true "${STAMP_ARGS[@]}" -o "$destination"
    python3 tools/check-renderer-package.py --rid "$rid" "$destination"
    bash tools/check-maps-shipped.sh "$destination"
    bash tools/check-no-game-assets.sh "$destination"
    if [[ "$rid" == win-x64 ]]; then bash tools/check-subsystem.sh gui "$destination/FruityPrime.exe"; fi
done

for rid in win-x64 linux-x64 linux-arm64 osx-arm64; do
    destination=$STAGE/servers/$rid
    label=release
    if [[ "$rid" == osx-arm64 ]]; then label=local/dev; fi
    echo "Packaging $label server $rid..."
    tools/package-server.sh --rid "$rid" --version "$VERSION" --skip-map-cook --output "$destination"
    bash tools/check-maps-shipped.sh "$destination"
    bash tools/check-no-game-assets.sh "$destination"
    if [[ "$rid" == win-x64 ]]; then bash tools/check-subsystem.sh console "$destination/FruityPrimeServer.exe"; fi
done

if [[ "$SKIP_ANDROID" -eq 0 ]]; then
    echo "Publishing Android APK ($ANDROID_SIGNING_STATUS signing)..."
    ANDROID_ARGS=(-p:AndroidSdkDirectory="$ANDROID_SDK")
    if [[ -n "${JAVA_HOME:-}" ]]; then ANDROID_ARGS+=(-p:JavaSdkDirectory="$JAVA_HOME"); fi
    if [[ "$ANDROID_SIGNING_STATUS" == local-release ]]; then
        ANDROID_ARGS+=(
            -p:AndroidKeyStore=true
            -p:AndroidSigningKeyStore="$PRIME_ANDROID_KEYSTORE_FILE"
            -p:AndroidSigningStorePass=env:PRIME_ANDROID_KEYSTORE_PASSWORD
            -p:AndroidSigningKeyAlias="$PRIME_ANDROID_KEY_ALIAS"
            -p:AndroidSigningKeyPass=env:PRIME_ANDROID_KEY_PASSWORD
        )
    fi
    mkdir -p "$STAGE/android"
    dotnet publish src/Android/Android.csproj -c Release -p:AndroidPackageFormat=apk \
        -p:SkipCustomMapBundleCook=true -p:ApplicationDisplayVersion="$VERSION" \
        "${STAMP_ARGS[@]}" "${ANDROID_ARGS[@]}" -o "$STAGE/android"
    APK=$(find "$STAGE/android" -maxdepth 1 -type f -name '*-Signed.apk' | head -n 1)
    [[ -n "$APK" ]] || { echo "Android publish produced no signed/installable APK." >&2; exit 1; }
    unzip -t "$APK" >/dev/null
    ANDROID_CHECK=$(mktemp -d "${TMPDIR:-/tmp}/project-prime-android-check.XXXXXX")
    unzip -q "$APK" -d "$ANDROID_CHECK"
    if find "$ANDROID_CHECK" -type f \( -iname '*.nds' -o -iname '*.arc' -o -iname '*.narc' -o -path '*/AMHE1/*' -o -path '*/_bin/arm9.bin' \) | grep . >/dev/null; then
        echo "Android APK contains proprietary game content." >&2
        rm -rf "$ANDROID_CHECK"
        exit 1
    fi
    APK_MAP_COUNT=$(find "$ANDROID_CHECK" -type f -name '*.fpmap' | wc -l | tr -d '[:space:]')
    rm -rf "$ANDROID_CHECK"
    [[ "$APK_MAP_COUNT" -eq "$MAP_COUNT" ]] || {
        echo "Android APK contains $APK_MAP_COUNT map bundles; expected $MAP_COUNT." >&2
        exit 1
    }
fi

python3 - "$STAGE" "$VERSION" "$MAP_COUNT" "$ANDROID_SIGNING_STATUS" <<'PY'
import json
import os
import sys

root, version, map_count, android = sys.argv[1:]
summary = f"""Project Prime complete build
Version: {version}
Desktop clients: win-x64, linux-x64, osx-x64, osx-arm64
Release servers: win-x64, linux-x64, linux-arm64
Local/dev server: osx-arm64
Android: {android}
Custom map bundles: {map_count}

AMHE1-derived room binaries are not shipped. Each client/server prepares them
at runtime from the operator's own extracted AMHE1 content.
"""
with open(os.path.join(root, "BUILD-SUMMARY.txt"), "w", encoding="utf-8") as handle:
    handle.write(summary)
manifest = {
    "product": "Project Prime",
    "version": version,
    "clients": ["win-x64", "linux-x64", "osx-x64", "osx-arm64"],
    "release_servers": ["win-x64", "linux-x64", "linux-arm64"],
    "local_dev_servers": ["osx-arm64"],
    "android_signing": android,
    "map_bundles": int(map_count),
    "runtime_content": "AMHE1-derived room binaries are prepared at runtime and are not shipped",
}
path = os.path.join(root, "build-manifest.json")
with open(path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

bash tools/check-no-game-assets.sh "$STAGE/clients" "$STAGE/servers"
mv "$STAGE" "$OUTPUT"
BUILD_COMPLETE=1
trap - EXIT INT TERM
echo "Complete Project Prime build: $OUTPUT"
echo "Version $VERSION; $MAP_COUNT custom map bundle(s); Android: $ANDROID_SIGNING_STATUS"
