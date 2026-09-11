#!/usr/bin/env bash
# Start a packaged Project Prime Linux Server Node and its managed Worker.
# This file is intentionally self-contained so it can be copied into an
# extracted server bundle. It does not require the source checkout or dotnet.
set -Eeo pipefail

BUNDLE_DIR=$PRIME_SERVER_BUNDLE
if [[ -z "$BUNDLE_DIR" ]]; then
    if [[ -f "$PWD/ProjectPrimeServer" ]]; then BUNDLE_DIR=$PWD; else BUNDLE_DIR=$(cd "$(dirname "$0")" && pwd); fi
fi
BUNDLE_DIR=$(cd "$BUNDLE_DIR" && pwd)
ENV_FILE=$PRIME_DEV_ENV_FILE
if [[ -z "$ENV_FILE" && -f "$BUNDLE_DIR/dev.env" ]]; then ENV_FILE=$BUNDLE_DIR/dev.env; fi
if [[ -n "$ENV_FILE" && -f "$ENV_FILE" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
fi

usage() {
    cat <<'USAGE'
Usage: ./start-dev.sh [options]

Run this from an extracted Linux server bundle. The Node and Worker executables
come from that bundle; no repository checkout or dotnet SDK is needed.

  --bundle-dir PATH    extracted bundle (default: this directory)
  --content-dir PATH   AMHE1 content directory
  --state-dir PATH     state/log directory (default: /tmp/project-prime-bundle-dev)
  --help               show this help

The shared Backend must already know this Node. Set PRIME_NODE_ID,
PRIME_NODE_PUBLIC_KEY_FILE (Backend ticket-signing public SPKI PEM), and either
PRIME_NODE_DIRECTORY_SECRET or PRIME_NODE_DIRECTORY_SECRET_FILE. The Backend
defaults to http://51.161.113.128:18085/. PRIME_NODE_CERT_PFX and
PRIME_NODE_CERT_PASSWORD may provide a trusted WSS certificate; otherwise a
self-signed development certificate is generated in the state directory.
The Node defaults to HTTPS/WSS port 8443, which is Cloudflare-proxyable without
requiring root; set PRIME_NODE_BIND and PRIME_NODE_PUBLIC_CONTROL_URI to use a
different endpoint.

Put persistent settings in dev.env beside this script or set
PRIME_DEV_ENV_FILE. Required example:

  PRIME_CONTENT_DIRECTORY=/srv/project-prime/AMHE1
  PRIME_NODE_ID=00000000-0000-0000-0000-000000000001
  PRIME_NODE_PUBLIC_KEY_FILE=/srv/project-prime/backend-ticket-public.pem
  PRIME_NODE_DIRECTORY_SECRET_FILE=/srv/project-prime/node-secret
USAGE
}

CONTENT_DIR=$PRIME_CONTENT_DIRECTORY
if [[ -z "$CONTENT_DIR" ]]; then CONTENT_DIR=$GAME_DATA_DIRECTORY; fi
STATE_DIR=$PRIME_DEV_STATE_DIR
if [[ -z "$STATE_DIR" ]]; then STATE_DIR=/tmp/project-prime-bundle-dev; fi

while (($#)); do
    case "$1" in
        --bundle-dir) [[ $# -ge 2 ]] || { echo "--bundle-dir needs a path." >&2; exit 2; }; BUNDLE_DIR=$2; shift 2 ;;
        --content-dir) [[ $# -ge 2 ]] || { echo "--content-dir needs a path." >&2; exit 2; }; CONTENT_DIR=$2; shift 2 ;;
        --state-dir) [[ $# -ge 2 ]] || { echo "--state-dir needs a path." >&2; exit 2; }; STATE_DIR=$2; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done
BUNDLE_DIR=$(cd "$BUNDLE_DIR" && pwd)
if [[ -z "$CONTENT_DIR" && -d "$BUNDLE_DIR/AMHE1" ]]; then CONTENT_DIR=$BUNDLE_DIR/AMHE1; fi
if [[ -z "$CONTENT_DIR" || ! -d "$CONTENT_DIR" ]]; then
    echo "Set PRIME_CONTENT_DIRECTORY or use --content-dir; the bundle does not include game content." >&2
    exit 1
fi
CONTENT_DIR=$(cd "$CONTENT_DIR" && pwd -P)

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing command: $1" >&2; exit 1; }; }
need python3
need curl
need openssl
NODE_PATH=$BUNDLE_DIR/ProjectPrimeServer
WORKER_PATH=$BUNDLE_DIR/worker/ProjectPrime.Server.Worker
[[ -f "$NODE_PATH" ]] || { echo "Missing $NODE_PATH" >&2; exit 1; }
[[ -f "$WORKER_PATH" ]] || { echo "Missing $WORKER_PATH" >&2; exit 1; }
chmod +x "$NODE_PATH" "$WORKER_PATH"
mkdir -p "$STATE_DIR/artifacts" "$STATE_DIR/replays"
chmod 700 "$STATE_DIR" "$STATE_DIR/artifacts" "$STATE_DIR/replays"

CONTENT_VERSION=$PRIME_CONTENT_VERSION
if [[ -z "$CONTENT_VERSION" ]]; then CONTENT_VERSION=AMHE1; fi
BACKEND_URL=$PRIME_BACKEND_URL
if [[ -z "$BACKEND_URL" ]]; then BACKEND_URL=http://51.161.113.128:18085/; fi
NODE_PUBLIC_HOST=$PRIME_NODE_PUBLIC_HOST
if [[ -z "$NODE_PUBLIC_HOST" ]]; then NODE_PUBLIC_HOST=51.161.113.128; fi
NODE_CONTROL_URI=$PRIME_NODE_PUBLIC_CONTROL_URI
if [[ -z "$NODE_CONTROL_URI" ]]; then NODE_CONTROL_URI=wss://$NODE_PUBLIC_HOST:8443/v1/control; fi
NODE_BIND=$PRIME_NODE_BIND
if [[ -z "$NODE_BIND" ]]; then NODE_BIND=https://0.0.0.0:8443; fi
MAP_KEY=$PRIME_MAP_KEY
WORKER_HOST=$PRIME_WORKER_PUBLIC_HOST
if [[ -z "$WORKER_HOST" ]]; then WORKER_HOST=$NODE_PUBLIC_HOST; fi
WORKER_BIND=$PRIME_WORKER_BIND
if [[ -z "$WORKER_BIND" ]]; then WORKER_BIND=0.0.0.0; fi
LANES=$PRIME_WORKER_LANES
if [[ -z "$LANES" ]]; then LANES=2; fi
MAX_MATCHES=$PRIME_WORKER_MAX_MATCHES
if [[ -z "$MAX_MATCHES" ]]; then MAX_MATCHES=4; fi
MAX_MATCHES_PER_LANE=$PRIME_WORKER_MAX_MATCHES_PER_LANE
if [[ -z "$MAX_MATCHES_PER_LANE" ]]; then MAX_MATCHES_PER_LANE=2; fi
PLAYER_LIMIT=$PRIME_WORKER_PLAYER_LIMIT
if [[ -z "$PLAYER_LIMIT" ]]; then PLAYER_LIMIT=32; fi
MAX_LOBBIES=$PRIME_NODE_MAX_LOBBIES
if [[ -z "$MAX_LOBBIES" ]]; then MAX_LOBBIES=64; fi
MAX_SESSIONS=$PRIME_NODE_MAX_SESSIONS
if [[ -z "$MAX_SESSIONS" ]]; then MAX_SESSIONS=256; fi
NODE_NAME=$PRIME_NODE_NAME
if [[ -z "$NODE_NAME" ]]; then NODE_NAME="Project Prime Dev"; fi
NODE_REGION=$PRIME_NODE_REGION
if [[ -z "$NODE_REGION" ]]; then NODE_REGION=dev; fi
KEY_ID=$PRIME_TICKET_KEY_ID
if [[ -z "$KEY_ID" ]]; then KEY_ID=dev-current; fi

NODE_ID=$PRIME_NODE_ID
if [[ -z "$NODE_ID" ]]; then echo "PRIME_NODE_ID is required for the shared Backend." >&2; exit 1; fi
python3 - "$NODE_ID" <<'PY'
import sys,uuid
try: value=uuid.UUID(sys.argv[1])
except ValueError: raise SystemExit("PRIME_NODE_ID must be a UUID")
if value.int == 0: raise SystemExit("PRIME_NODE_ID must be non-empty")
PY
PUBLIC_KEY=$PRIME_NODE_PUBLIC_KEY_FILE
if [[ -z "$PUBLIC_KEY" || ! -s "$PUBLIC_KEY" ]]; then
    echo "PRIME_NODE_PUBLIC_KEY_FILE must point to the Backend ticket public SPKI PEM." >&2
    exit 1
fi
if [[ "$PUBLIC_KEY" != /* ]]; then PUBLIC_KEY=$(cd "$(dirname "$PUBLIC_KEY")" && pwd)/$(basename "$PUBLIC_KEY"); fi
NODE_SECRET=$PRIME_NODE_DIRECTORY_SECRET
NODE_SECRET_FILE=$PRIME_NODE_DIRECTORY_SECRET_FILE
if [[ -n "$NODE_SECRET" && -n "$NODE_SECRET_FILE" ]]; then
    echo "Configure one Node directory credential source." >&2
    exit 1
fi
if [[ -z "$NODE_SECRET" ]]; then
    [[ -s "$NODE_SECRET_FILE" ]] || { echo "Set PRIME_NODE_DIRECTORY_SECRET or PRIME_NODE_DIRECTORY_SECRET_FILE." >&2; exit 1; }
    NODE_SECRET_FILE=$(cd "$(dirname "$NODE_SECRET_FILE")" && pwd)/$(basename "$NODE_SECRET_FILE")
else
    [[ $(printf '%s' "$NODE_SECRET" | wc -c) -ge 32 ]] || { echo "Node directory credential must contain at least 32 characters." >&2; exit 1; }
fi

TICKET_ISSUER=$PRIME_TICKET_ISSUER
if [[ -z "$TICKET_ISSUER" ]]; then TICKET_ISSUER=https://$NODE_PUBLIC_HOST; fi
BACKEND_PARTS=$(python3 - "$BACKEND_URL" <<'PY'
import sys
from urllib.parse import urlparse
u = urlparse(sys.argv[1])
if not u.scheme or not u.hostname or u.username or u.password or u.query or u.fragment:
    raise SystemExit(1)
print(u.scheme.lower(), u.hostname.lower())
PY
) || { echo "PRIME_BACKEND_URL must be an origin without credentials, query, or fragment." >&2; exit 1; }
read -r BACKEND_SCHEME BACKEND_HOST <<< "$BACKEND_PARTS"
if [[ "$BACKEND_SCHEME" != http && "$BACKEND_SCHEME" != https ]]; then
    echo "PRIME_BACKEND_URL must use HTTP or HTTPS." >&2
    exit 1
fi
if [[ "$BACKEND_SCHEME" == http && "$BACKEND_HOST" != 51.161.113.128 && "$BACKEND_HOST" != localhost && "$BACKEND_HOST" != 127.0.0.1 && "$BACKEND_HOST" != ::1 ]]; then
    echo "Plain HTTP is limited to 51.161.113.128 or loopback." >&2
    exit 1
fi
python3 - "$TICKET_ISSUER" <<'PY'
import sys
from urllib.parse import urlparse
u = urlparse(sys.argv[1])
if u.scheme.lower() != "https" or not u.hostname or u.username or u.password or u.query or u.fragment:
    raise SystemExit("PRIME_TICKET_ISSUER must be an HTTPS origin")
PY
if [[ "$NODE_CONTROL_URI" != wss://* ]]; then
    echo "PRIME_NODE_PUBLIC_CONTROL_URI must use wss://." >&2
    exit 1
fi

CERT_PFX=$PRIME_NODE_CERT_PFX
if [[ -z "$CERT_PFX" ]]; then CERT_PFX=$STATE_DIR/node-tls.pfx; fi
CERT_PASSWORD=$PRIME_NODE_CERT_PASSWORD
if [[ -n "$PRIME_NODE_CERT_PFX" && -z "$CERT_PASSWORD" ]]; then
    echo "PRIME_NODE_CERT_PASSWORD is required with PRIME_NODE_CERT_PFX." >&2
    exit 1
fi
if [[ -z "$CERT_PASSWORD" && -s "$STATE_DIR/node-tls-password" ]]; then CERT_PASSWORD=$(tr -d '\r\n' < "$STATE_DIR/node-tls-password"); fi
if [[ ! -s "$CERT_PFX" ]]; then
    CERT_PASSWORD=$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')
    printf '%s\n' "$CERT_PASSWORD" > "$STATE_DIR/node-tls-password"
    chmod 600 "$STATE_DIR/node-tls-password"
    if python3 - "$NODE_PUBLIC_HOST" <<'PY'
import ipaddress,sys
try: ipaddress.ip_address(sys.argv[1])
except ValueError: raise SystemExit(1)
PY
    then TLS_SAN=IP; else TLS_SAN=DNS; fi
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
        -keyout "$STATE_DIR/node-tls.key" -out "$STATE_DIR/node-tls.crt" \
        -subj "/CN=$NODE_PUBLIC_HOST" -addext "subjectAltName=$TLS_SAN:$NODE_PUBLIC_HOST" >/dev/null 2>&1
    openssl pkcs12 -export -out "$CERT_PFX" -inkey "$STATE_DIR/node-tls.key" \
        -in "$STATE_DIR/node-tls.crt" -passout "pass:$CERT_PASSWORD" >/dev/null 2>&1
    chmod 600 "$CERT_PFX" "$STATE_DIR/node-tls.key" "$STATE_DIR/node-tls.crt"
    echo "Generated self-signed Node TLS at $CERT_PFX; trusted PFX is recommended for testers."
fi
[[ -n "$CERT_PASSWORD" && -s "$CERT_PFX" ]] || { echo "Node PFX and password are required." >&2; exit 1; }

MAP_DIR=$BUNDLE_DIR/maps
[[ -d "$MAP_DIR" ]] || { echo "Worker map directory is missing: $MAP_DIR" >&2; exit 1; }

CONTENT_IS_BAKED=0
if [[ -f "$CONTENT_DIR/server-content.json" ]]; then
    CONTENT_IS_BAKED=1
fi

CONTENT_LOCK_HELD=0
release_content_lock() {
    lock_exit_status=$?
    if [[ "$CONTENT_LOCK_HELD" == 1 ]]; then
        exec 9>&-
        CONTENT_LOCK_HELD=0
    fi
    return "$lock_exit_status"
}

if [[ "$CONTENT_IS_BAKED" == 0 ]]; then
    CONTENT_LOCK_ROOT=${TMPDIR:-/tmp}/project-prime-content-locks-$UID
    if ! mkdir -p "$CONTENT_LOCK_ROOT" || ! chmod 700 "$CONTENT_LOCK_ROOT"; then
        echo "Unable to create the private content lock directory: $CONTENT_LOCK_ROOT" >&2
        exit 1
    fi
    if ! CONTENT_LOCK_KEY=$(python3 - "$CONTENT_DIR" <<'PY'
import hashlib
import sys
print(hashlib.sha256(sys.argv[1].encode("utf-8")).hexdigest())
PY
    ); then
        echo "Unable to derive the content lock key for $CONTENT_DIR." >&2
        exit 1
    fi
    CONTENT_LOCK_FILE=$CONTENT_LOCK_ROOT/$CONTENT_LOCK_KEY.lock
    if ! exec 9>"$CONTENT_LOCK_FILE"; then
        echo "Unable to open the content lock file: $CONTENT_LOCK_FILE" >&2
        exit 1
    fi
    if ! chmod 600 "$CONTENT_LOCK_FILE"; then
        exec 9>&-
        echo "Unable to secure the content lock file: $CONTENT_LOCK_FILE" >&2
        exit 1
    fi
if python3 - <<'PY'
import fcntl
import sys
try:
    fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
except BlockingIOError:
    print("Content directory is already locked by another Project Prime launcher.", file=sys.stderr)
    raise SystemExit(75)
except OSError as error:
    print(f"Unable to acquire the content lock: {error}", file=sys.stderr)
    raise SystemExit(1)
PY
    then
        CONTENT_LOCK_HELD=1
        trap release_content_lock EXIT
    else
        lock_status=$?
        exec 9>&-
        if [[ "$lock_status" == 75 ]]; then
            echo "Could not acquire the content lock for $CONTENT_DIR; another launcher is using it. Worker content preparation was not started." >&2
        else
            echo "Could not acquire the content lock for $CONTENT_DIR. Worker content preparation was not started." >&2
        fi
        exit 1
    fi
fi

DESCRIPTOR_PATH=$STATE_DIR/content-description.json
if ! DESCRIPTOR_TMP=$(mktemp "$STATE_DIR/.content-description.XXXXXX"); then
    echo "Unable to create the Worker content descriptor in $STATE_DIR." >&2
    exit 1
fi
if ! "$WORKER_PATH" --describe-content true --content-dir "$CONTENT_DIR" \
    --content-version "$CONTENT_VERSION" --map-dir "$MAP_DIR" > "$DESCRIPTOR_TMP"; then
    rm -f "$DESCRIPTOR_TMP"
    echo "Worker content discovery failed; no Node configuration was generated." >&2
    exit 1
fi
chmod 600 "$DESCRIPTOR_TMP"
if ! mv -f "$DESCRIPTOR_TMP" "$DESCRIPTOR_PATH"; then
    rm -f "$DESCRIPTOR_TMP"
    echo "Unable to publish the Worker content descriptor: $DESCRIPTOR_PATH" >&2
    exit 1
fi

CONFIG_PATH=$STATE_DIR/appsettings.json
export CONFIG_PATH DESCRIPTOR_PATH MAP_DIR CONTENT_VERSION NODE_ID TICKET_ISSUER KEY_ID PUBLIC_KEY MAP_KEY
export CONTENT_DIR ARTIFACT_DIR="$STATE_DIR/artifacts" REPLAY_DIR="$STATE_DIR/replays"
export WORKER_HOST WORKER_BIND LANES MAX_MATCHES MAX_MATCHES_PER_LANE PLAYER_LIMIT MAX_LOBBIES MAX_SESSIONS
export NODE_NAME NODE_REGION NODE_CONTROL_URI BACKEND_URL BUNDLE_DIR
if ! identity=$(python3 - <<'PY'
import json, os, sys

class DescriptorError(ValueError):
    pass

def invalid(message):
    raise DescriptorError(message)

def valid_text(name, value):
    if not isinstance(value, str) or not value or not value.strip() or len(value) > 128 \
            or any(ord(char) < 0x20 or ord(char) == 0x7f for char in value):
        invalid(f"{name} must be non-empty bounded text")

try:
    with open(os.environ["DESCRIPTOR_PATH"], encoding="utf-8") as descriptor_file:
        descriptor = json.load(descriptor_file)
    if not isinstance(descriptor, dict):
        invalid("descriptor root must be an object")
    for field in ("ContentVersion", "ContentHash", "BuildVersion"):
        valid_text(field, descriptor.get(field))
    protocol = descriptor.get("ProtocolVersion")
    if type(protocol) is not int or not 0 < protocol <= 255:
        invalid("ProtocolVersion must be a positive byte")

    entries = descriptor.get("Maps")
    if not isinstance(entries, list) or not entries:
        invalid("Maps must be a non-empty array")
    if len(entries) > 256:
        invalid("discovery returned more than 256 maps")

    maps = []
    map_keys = set()
    pair_count = 0
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            invalid(f"Maps[{index}] must be an object")
        key = entry.get("MapKey")
        if type(key) is not str or not 0 < len(key) <= 128 \
                or not key.strip() \
                or any(ord(char) < 0x20 or ord(char) > 0x7e for char in key):
            invalid(f"Maps[{index}].MapKey must be printable ASCII text of at most 128 characters")
        if key in map_keys:
            invalid(f"duplicate map key: {key!r}")
        map_keys.add(key)
        modes = entry.get("Modes")
        if not isinstance(modes, list) or not modes:
            invalid(f"Maps[{index}].Modes must be a non-empty array")
        normalized_modes = []
        seen_modes = set()
        for mode in modes:
            if type(mode) is not int or mode < 0 or mode > 11:
                invalid(f"map {key!r} contains unknown MatchMode {mode!r}; expected 0..11")
            if mode in seen_modes:
                invalid(f"map {key!r} contains duplicate MatchMode {mode}")
            seen_modes.add(mode)
            normalized_modes.append(mode)
            pair_count += 1
            if pair_count > 768:
                invalid("discovery returned more than 768 map/mode pairs")
        mapped = {"MapKey": key, "Modes": sorted(normalized_modes)}
        required = entry.get("RequiredMap")
        package_path = entry.get("PackagePath")
        if required is not None:
            if not isinstance(required, dict) or type(package_path) is not str or not package_path:
                invalid(f"map {key!r} has incomplete package distribution metadata")
            for field in ("StableId", "Version", "ContentHash", "ArtifactHash"):
                valid_text(f"Maps[{index}].RequiredMap.{field}", required.get(field))
            package_size = required.get("PackageSize")
            if type(package_size) is not int or not 0 < package_size <= 268435456:
                invalid(f"map {key!r} package size is invalid")
            mapped.update({"StableId": required["StableId"], "Version": required["Version"],
                "MapContentHash": required["ContentHash"], "ArtifactHash": required["ArtifactHash"],
                "PackageSize": package_size, "PackagePath": package_path})
        elif package_path is not None:
            invalid(f"map {key!r} has a package path without acquisition metadata")
        maps.append(mapped)

    maps.sort(key=lambda entry: entry["MapKey"])
    override = os.environ.get("MAP_KEY", "")
    if override:
        if len(override) > 128 or not override.strip() \
                or any(ord(char) < 0x20 or ord(char) > 0x7e for char in override):
            invalid("PRIME_MAP_KEY must be printable ASCII text of at most 128 characters")
        selected = [entry for entry in maps if entry["MapKey"] == override]
        if not selected:
            invalid(f"PRIME_MAP_KEY {override!r} is not present in the Worker descriptor")
        maps = selected
    content = {
        "ContentVersion": descriptor["ContentVersion"],
        "ContentHash": descriptor["ContentHash"],
        "BuildVersion": descriptor["BuildVersion"],
        "ProtocolVersion": protocol,
    }
except DescriptorError as error:
    print(f"Worker content discovery is invalid: {error}", file=sys.stderr)
    raise SystemExit(1)
except (OSError, UnicodeError, json.JSONDecodeError) as error:
    print(f"Worker content descriptor could not be read: {error}", file=sys.stderr)
    raise SystemExit(1)

def number(name):
    value=int(os.environ[name])
    if value < 1: raise SystemExit(name+" must be positive")
    return value
lanes=number("LANES"); matches=number("MAX_MATCHES"); per_lane=number("MAX_MATCHES_PER_LANE"); players=number("PLAYER_LIMIT")
if matches > lanes * per_lane: raise SystemExit("Worker match capacity exceeds lane capacity")
cfg={"Node":{
    "MaximumLobbies":number("MAX_LOBBIES"),"MaximumSessions":number("MAX_SESSIONS"),
    "Authentication":{"NodeId":os.environ["NODE_ID"],"Issuer":os.environ["TICKET_ISSUER"],
                      "Keys":[{"KeyId":os.environ["KEY_ID"],"PublicKeyPemPath":os.environ["PUBLIC_KEY"]}]},
    "Maps":[{"MapKey":entry["MapKey"],**content,"Modes":entry["Modes"]} for entry in maps],
    "Workers":{"DrainTimeout":"00:00:30","ForceAfterDrainDeadline":False,"Processes":[{
        "FileName":"worker/ProjectPrime.Server.Worker","Content":content,
        "ArtifactDirectory":os.environ["ARTIFACT_DIR"],"WorkingDirectory":os.environ["BUNDLE_DIR"],
        "Arguments":["--content-dir",os.environ["CONTENT_DIR"],"--content-version",os.environ["CONTENT_VERSION"],
                     "--map-dir",os.environ["MAP_DIR"],
                     "--host",os.environ["WORKER_HOST"],"--bind",os.environ["WORKER_BIND"],
                     "--lanes",str(lanes),"--max-matches",str(matches),"--max-matches-per-lane",str(per_lane),
                     "--replay-dir",os.environ["REPLAY_DIR"]],
        "Capacity":{"MatchLimit":matches,"PlayerLimit":players,"ActiveMatches":0,"ActivePlayers":0},
        "StartupTimeout":"00:00:30","HeartbeatTimeout":"00:00:10","ShutdownTimeout":"00:00:10"}]},
    "Directory":{"Enabled":True,"BackendUri":os.environ["BACKEND_URL"],
                 "PublicControlUri":os.environ["NODE_CONTROL_URI"],"Name":os.environ["NODE_NAME"],
                 "Region":os.environ["NODE_REGION"]}}}
config_path = os.environ["CONFIG_PATH"]
temporary_config = config_path + ".tmp"
with open(temporary_config,"w",encoding="utf-8") as f:
    json.dump(cfg,f,indent=2); f.write("\n")
os.chmod(temporary_config, 0o600)
os.replace(temporary_config, config_path)
print("\t".join((content["ContentHash"], content["BuildVersion"], str(content["ProtocolVersion"]))))
PY
); then
    echo "Node configuration was not generated from the Worker content descriptor." >&2
    exit 1
fi
IFS=$'\t' read -r CONTENT_HASH BUILD_VERSION PROTOCOL_VERSION <<< "$identity"
[[ -n "$CONTENT_HASH" && -n "$BUILD_VERSION" && -n "$PROTOCOL_VERSION" ]] || {
    echo "Worker content identity is incomplete." >&2
    exit 1
}
chmod 600 "$CONFIG_PATH"

NODE_PID=
cleanup() {
    rc=$?
    trap - EXIT INT TERM
    [[ -n "$NODE_PID" && "$NODE_PID" != 0 ]] && kill "$NODE_PID" 2>/dev/null || true
    wait "$NODE_PID" 2>/dev/null || true
    release_content_lock
    rm -f "$STATE_DIR/node.pid"
    exit "$rc"
}
trap cleanup EXIT INT TERM

HEALTH=$BACKEND_URL
if [[ "$HEALTH" != */ ]]; then HEALTH=$HEALTH/; fi
HEALTH=$HEALTH"health/ready"
echo "Waiting for shared Backend: $BACKEND_URL"
for ((i=0;i<30;i++)); do
    if curl --fail --silent --show-error --max-time 3 "$HEALTH" >/dev/null 2>&1; then break; fi
    if ((i == 29)); then echo "Shared Backend did not become reachable." >&2; exit 1; fi
    sleep 1
done

NODE_LOG=$STATE_DIR/node.log
echo "Starting Server Node (logs: $NODE_LOG)"
(
    export ASPNETCORE_ENVIRONMENT=Development
    export ASPNETCORE_URLS=$NODE_BIND
    export ASPNETCORE_Kestrel__Certificates__Default__Path=$CERT_PFX
    export ASPNETCORE_Kestrel__Certificates__Default__Password=$CERT_PASSWORD
    if [[ -n "$NODE_SECRET_FILE" ]]; then
        export PRIME_NODE_DIRECTORY_SECRET_FILE=$NODE_SECRET_FILE
        unset PRIME_NODE_DIRECTORY_SECRET || true
    else
        export PRIME_NODE_DIRECTORY_SECRET=$NODE_SECRET
        unset PRIME_NODE_DIRECTORY_SECRET_FILE || true
    fi
    "$NODE_PATH" --contentRoot "$STATE_DIR"
) > "$NODE_LOG" 2>&1 &
NODE_PID=$!
printf '%s\n' "$NODE_PID" > "$STATE_DIR/node.pid"

NODE_HEALTH=$(printf '%s' "$NODE_CONTROL_URI" | sed 's#^wss://#https://#; s#/v1/control$#/health#')
echo "Waiting for Node health: $NODE_HEALTH"
for ((i=0;i<30;i++)); do
    if curl --fail --silent --show-error --insecure --max-time 3 "$NODE_HEALTH" >/dev/null 2>&1; then break; fi
    if ! kill -0 "$NODE_PID" 2>/dev/null; then
        echo "Server Node exited; see $NODE_LOG" >&2
        exit 1
    fi
    if ((i == 29)); then echo "Server Node did not become healthy; see $NODE_LOG." >&2; exit 1; fi
    sleep 1
done

echo "Development Server Node is running."
echo "  Backend: $BACKEND_URL"
echo "  Node:    $NODE_CONTROL_URI"
echo "  State:   $STATE_DIR"
echo "  Stop:    Ctrl-C"
if [[ -z "$PRIME_NODE_CERT_PFX" ]]; then echo "  TLS:     self-signed; give testers the certificate or set PRIME_NODE_CERT_PFX."; fi
wait "$NODE_PID"
