#!/usr/bin/env bash
# Publish the Backend, control Node, and gameplay Worker as one self-contained
# server bundle. The Node apphost is renamed at the package boundary only; its
# managed assembly name remains ProjectPrime.Server.Node for diagnostics.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID=""
OUTPUT=""
CONFIGURATION="Release"
VERSION=""
SKIP_MAP_COOK=0
MAP_ARTIFACTS="$ROOT/artifacts/maps/current"

usage() {
  cat <<'NOTE'
Usage: tools/package-server.sh --rid RID --output DIRECTORY [--configuration CONFIGURATION] [--version VERSION] [--skip-map-cook] [--map-artifacts DIRECTORY]

Publishes the Backend beneath backend/, Server.Node at the bundle root, and
Server.Worker beneath worker/. The package contains binaries only: credentials,
database state, and game content remain operator-supplied.
Release RIDs are linux-x64, linux-arm64, win-x64. osx-arm64 is available for
local package-smoke validation and is intentionally not a release artifact.
Use --skip-map-cook only when the current map bundles were already cooked and
validated by the calling build; pass their artifact directory with
--map-artifacts. A normal invocation owns the incremental cook in
artifacts/maps/current and never writes generated bundles into maps/.
NOTE
}

while (($# > 0)); do
  case "$1" in
    --rid) [[ $# -ge 2 ]] || { echo "--rid requires a value" >&2; exit 2; }; RID="$2"; shift 2 ;;
    --output) [[ $# -ge 2 ]] || { echo "--output requires a value" >&2; exit 2; }; OUTPUT="$2"; shift 2 ;;
    --configuration) [[ $# -ge 2 ]] || { echo "--configuration requires a value" >&2; exit 2; }; CONFIGURATION="$2"; shift 2 ;;
    --version) [[ $# -ge 2 ]] || { echo "--version requires a value" >&2; exit 2; }; VERSION="$2"; shift 2 ;;
    --skip-map-cook) SKIP_MAP_COOK=1; shift ;;
    --map-artifacts) [[ $# -ge 2 ]] || { echo "--map-artifacts requires a value" >&2; exit 2; }; MAP_ARTIFACTS="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$RID" in
  linux-x64|linux-arm64|win-x64|osx-arm64) ;;
  *) echo "Unsupported server RID: ${RID:-<missing>}" >&2; exit 2 ;;
esac
[[ -n "$OUTPUT" ]] || { echo "--output is required" >&2; exit 2; }
[[ "$CONFIGURATION" =~ ^[A-Za-z0-9_.-]+$ ]] || { echo "Invalid configuration" >&2; exit 2; }
[[ -e "$OUTPUT" ]] && { echo "Refusing to overwrite existing package directory: $OUTPUT" >&2; exit 1; }
MAP_ARTIFACTS=$(python3 - "$MAP_ARTIFACTS" <<'PY'
import os
import sys
print(os.path.realpath(os.path.abspath(sys.argv[1])))
PY
)

# Keep a direct server-package invocation in step with the client publish.
# Bundles contain only map recipes/level data/textures, so this does not need
# extracted game content and can run before any of the server projects publish.
if [[ "$SKIP_MAP_COOK" -eq 0 ]]; then
  cook_version="$VERSION"
  [[ -n "$cook_version" ]] || cook_version=development
  package_version="$VERSION"
  [[ -n "$package_version" ]] || package_version=local
  "$ROOT/tools/cook-maps.sh" --source "$ROOT/maps" --output "$MAP_ARTIFACTS" \
    --configuration "$CONFIGURATION" --compiler-version "$cook_version" \
    --schema-version 1 --package-version "$package_version"
fi
if [[ ! -d "$MAP_ARTIFACTS" ]]; then
  echo "Map artifact directory does not exist: $MAP_ARTIFACTS" >&2
  exit 1
fi
if ! find "$MAP_ARTIFACTS" -maxdepth 1 -type f -name '*.fpmap' -print -quit | grep -q .; then
  echo "Map artifact directory contains no .fpmap bundles: $MAP_ARTIFACTS" >&2
  exit 1
fi

PUBLISH_ARGS=(-c "$CONFIGURATION" -r "$RID" --self-contained true -p:PublishSingleFile=true)
[[ -n "$VERSION" ]] && PUBLISH_ARGS+=("-p:Version=$VERSION" "-p:InformationalVersion=$VERSION")
PUBLISH_ARGS+=("-p:PrimeMapCopyToPublish=false")

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/project-prime-server-package.XXXXXX")"
cleanup() { rm -rf "$STAGE"; }
trap cleanup EXIT

mkdir -p "$STAGE/backend" "$STAGE/worker"
dotnet publish "$ROOT/src/Backend/Backend.csproj" "${PUBLISH_ARGS[@]}" -o "$STAGE/backend"
dotnet publish "$ROOT/src/Server.Node/Server.Node.csproj" "${PUBLISH_ARGS[@]}" -o "$STAGE/node"
dotnet publish "$ROOT/src/Server.Worker/Server.Worker.csproj" "${PUBLISH_ARGS[@]}" -o "$STAGE/worker"

if [[ "$RID" == win-x64 ]]; then
  BACKEND_APPHOST="$STAGE/backend/ProjectPrime.Backend.exe"
  NODE_APPHOST="$STAGE/node/ProjectPrime.Server.Node.exe"
  WORKER_APPHOST="$STAGE/worker/ProjectPrime.Server.Worker.exe"
  PACKAGE_NODE="$STAGE/node/ProjectPrimeServer.exe"
else
  BACKEND_APPHOST="$STAGE/backend/ProjectPrime.Backend"
  NODE_APPHOST="$STAGE/node/ProjectPrime.Server.Node"
  WORKER_APPHOST="$STAGE/worker/ProjectPrime.Server.Worker"
  PACKAGE_NODE="$STAGE/node/ProjectPrimeServer"
fi
[[ -f "$BACKEND_APPHOST" ]] || { echo "Backend publish did not produce the expected apphost: $BACKEND_APPHOST" >&2; exit 1; }
[[ -f "$NODE_APPHOST" ]] || { echo "Node publish did not produce the expected apphost: $NODE_APPHOST" >&2; exit 1; }
[[ -f "$WORKER_APPHOST" ]] || { echo "Worker publish did not produce the expected apphost: $WORKER_APPHOST" >&2; exit 1; }

# Rename only the native apphost. Leave all managed assembly/deps identities
# emitted by the project untouched.
mv "$NODE_APPHOST" "$PACKAGE_NODE"
python3 "$ROOT/tools/package-server-config.py" --rid "$RID" \
  --output "$STAGE/node/server.example.json"
chmod +x "$BACKEND_APPHOST" "$PACKAGE_NODE" "$WORKER_APPHOST"
if [[ "$RID" == linux-x64 || "$RID" == linux-arm64 ]]; then
  cp "$ROOT/tools/start-bundle-dev.sh" "$STAGE/node/start-dev.sh"
  cp "$ROOT/tools/start-dev.sh" "$STAGE/node/start-stack-dev.sh"
  chmod +x "$STAGE/node/start-dev.sh"
  chmod +x "$STAGE/node/start-stack-dev.sh"
fi

if ! strings "$PACKAGE_NODE" | grep -F 'ProjectPrime.Server.Node' >/dev/null; then
  echo "Node assembly identity is missing from the published metadata." >&2
  exit 1
fi
if find "$STAGE/node" -maxdepth 1 -type f -name 'ProjectPrime.Server.Worker*' | grep -q .; then
  echo "Worker publish leaked into the Node package root." >&2
  exit 1
fi
bash "$ROOT/tools/check-no-game-assets.sh" "$STAGE/backend" "$STAGE/node" "$STAGE/worker"

mkdir -p "$(dirname "$OUTPUT")"
mv "$STAGE/node" "$OUTPUT"
mv "$STAGE/backend" "$OUTPUT/backend"
mv "$STAGE/worker" "$OUTPUT/worker"
# The Node is the authority for custom-room discovery as well as the process
# that launches Workers. Carry the same cooked map bundles that desktop builds
# receive so a server package can validate and advertise the same room set.
map_count=0
while IFS= read -r -d '' bundle; do
  mkdir -p "$OUTPUT/maps"
  cp "$bundle" "$OUTPUT/maps/$(basename "$bundle")"
  map_count=$((map_count + 1))
done < <(find "$MAP_ARTIFACTS" -maxdepth 1 -type f -name '*.fpmap' -print0 2>/dev/null)
[[ "$map_count" -gt 0 ]] || { echo "No map bundles were copied into the server package." >&2; exit 1; }
bash "$ROOT/tools/check-maps-shipped.sh" "$OUTPUT"
printf 'Packaged %s server bundle at %s\n' "$RID" "$OUTPUT"
