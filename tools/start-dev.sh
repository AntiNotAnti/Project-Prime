#!/usr/bin/env bash
# Start the Project Prime development Backend (optional), Server Node, and its
# managed Worker. Generated configuration and secrets stay outside the checkout.
# With no Node credentials it bootstraps a local guest-capable Backend; when
# persistent shared-Backend credentials are supplied it reuses that Backend.
set -Eeo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
if [[ -f "$SCRIPT_DIR/FruityPrimeServer" ]]; then
    # This launcher can be copied into a combined package as start-stack-dev.sh.
    ROOT=$SCRIPT_DIR
fi
LAUNCHER_NAME=$(basename "${BASH_SOURCE[0]}")
ENV_FILE=$PRIME_DEV_ENV_FILE
if [[ -z "$ENV_FILE" ]]; then ENV_FILE=$ROOT/.env.dev; fi
if [[ -f "$ENV_FILE" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
fi

usage() {
    cat <<USAGE
Usage: $LAUNCHER_NAME [options]

Starts the packaged Server Node and Worker in the foreground. Press Ctrl-C to
stop children. Runtime state and logs are outside the checkout by default.

  --content-dir PATH   AMHE1 content directory
  --package-dir PATH   server-linux-x64 bundle
  --state-dir PATH     runtime state and logs
  --with-backend       start a local development Backend too
  --no-backend         use the shared Backend
  --help               show this help

Shared Backend mode needs PRIME_NODE_ID, PRIME_NODE_PUBLIC_KEY_FILE, and
PRIME_NODE_DIRECTORY_SECRET (or PRIME_NODE_DIRECTORY_SECRET_FILE). Local
Backend mode generates development Node/ticket/TLS credentials in the state
directory and defaults to 51.161.113.128 on ports 18085 (Backend) and 8443
(Node), binding both services on all interfaces. Port 8443 is
Cloudflare-proxyable and does not require root. With no complete shared
credential set, the default auto mode starts the local Backend; use
--no-backend to require the shared path. Put persistent overrides in .env.dev
or set PRIME_DEV_ENV_FILE.
USAGE
}

CONTENT_DIR=$PRIME_CONTENT_DIRECTORY
if [[ -z "$CONTENT_DIR" ]]; then CONTENT_DIR=$GAME_DATA_DIRECTORY; fi
if [[ -z "$CONTENT_DIR" && -d "$ROOT/AMHE1" ]]; then CONTENT_DIR=$ROOT/AMHE1; fi
PACKAGE_DIR=$PRIME_SERVER_PACKAGE
if [[ -z "$PACKAGE_DIR" ]]; then
    if [[ -f "$ROOT/FruityPrimeServer" ]]; then PACKAGE_DIR=$ROOT; else PACKAGE_DIR=$ROOT/publish/server-linux-x64; fi
fi
STATE_DIR=$PRIME_DEV_STATE_DIR
if [[ -z "$STATE_DIR" ]]; then
    if [[ -n "$TMPDIR" ]]; then STATE_DIR=$TMPDIR/project-prime-dev; else STATE_DIR=/tmp/project-prime-dev; fi
fi
START_BACKEND=$PRIME_START_BACKEND
if [[ -z "$START_BACKEND" ]]; then START_BACKEND=auto; fi
FORCE_LOCAL_STACK=0
if [[ "$(basename "${BASH_SOURCE[0]}")" == start-stack-dev.sh ]]; then FORCE_LOCAL_STACK=1; fi

while (($#)); do
    case "$1" in
        --content-dir) [[ $# -ge 2 ]] || { echo "--content-dir needs a path." >&2; exit 2; }; CONTENT_DIR=$2; shift 2 ;;
        --package-dir) [[ $# -ge 2 ]] || { echo "--package-dir needs a path." >&2; exit 2; }; PACKAGE_DIR=$2; shift 2 ;;
        --state-dir) [[ $# -ge 2 ]] || { echo "--state-dir needs a path." >&2; exit 2; }; STATE_DIR=$2; shift 2 ;;
        --with-backend) START_BACKEND=1; shift ;;
        --no-backend) START_BACKEND=0; shift ;;
        --help|-h) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done
if [[ "$FORCE_LOCAL_STACK" == 1 ]]; then
    START_BACKEND=1
elif [[ "$START_BACKEND" == auto ]]; then
    if [[ -n "$PRIME_NODE_ID" && -n "$PRIME_NODE_PUBLIC_KEY_FILE" \
        && ( -n "$PRIME_NODE_DIRECTORY_SECRET" || -n "$PRIME_NODE_DIRECTORY_SECRET_FILE" ) ]]; then
        START_BACKEND=0
    else
        START_BACKEND=1
    fi
fi
[[ "$START_BACKEND" == 0 || "$START_BACKEND" == 1 ]] || { echo "PRIME_START_BACKEND must be 0, 1, or auto." >&2; exit 2; }

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing command: $1" >&2; exit 1; }; }
need python3
need curl
mkdir -p "$STATE_DIR/artifacts" "$STATE_DIR/replays" "$STATE_DIR/data-protection"
chmod 700 "$STATE_DIR" "$STATE_DIR/data-protection"

if [[ ! -f "$PACKAGE_DIR/FruityPrimeServer" ]]; then
    PACKAGE_DIR=$STATE_DIR/package
    if [[ ! -f "$PACKAGE_DIR/FruityPrimeServer" ]]; then
        [[ -x "$ROOT/tools/package-server.sh" ]] || { echo "Missing Linux server bundle: $PACKAGE_DIR" >&2; exit 1; }
        echo "Publishing the Linux server bundle into $PACKAGE_DIR"
        "$ROOT/tools/package-server.sh" --rid linux-x64 --output "$PACKAGE_DIR"
    fi
fi
PACKAGE_DIR=$(cd "$PACKAGE_DIR" && pwd)
NODE_PATH=$PACKAGE_DIR/FruityPrimeServer
WORKER_PATH=$PACKAGE_DIR/worker/FruityPrime.Server.Worker
[[ -f "$NODE_PATH" ]] || { echo "Missing Node executable: $NODE_PATH" >&2; exit 1; }
[[ -f "$WORKER_PATH" ]] || { echo "Missing Worker executable: $WORKER_PATH" >&2; exit 1; }
chmod +x "$NODE_PATH" "$WORKER_PATH"
BACKEND_EXECUTABLE=$PRIME_BACKEND_EXECUTABLE
if [[ -z "$BACKEND_EXECUTABLE" ]]; then
    if [[ -x "$PACKAGE_DIR/backend/PrimeHunters.Backend" ]]; then
        BACKEND_EXECUTABLE=$PACKAGE_DIR/backend/PrimeHunters.Backend
    elif [[ -f "$PACKAGE_DIR/backend/PrimeHunters.Backend.exe" ]]; then
        BACKEND_EXECUTABLE=$PACKAGE_DIR/backend/PrimeHunters.Backend.exe
    fi
fi

if [[ -z "$CONTENT_DIR" || ! -d "$CONTENT_DIR" ]]; then
    echo "Set PRIME_CONTENT_DIRECTORY (or --content-dir) to the AMHE1 content directory." >&2
    exit 1
fi
CONTENT_DIR=$(cd "$CONTENT_DIR" && pwd)
CONTENT_VERSION=$PRIME_CONTENT_VERSION
if [[ -z "$CONTENT_VERSION" ]]; then CONTENT_VERSION=AMHE1; fi
BACKEND_BIND=$PRIME_BACKEND_BIND
if [[ -z "$BACKEND_BIND" ]]; then
    if [[ "$START_BACKEND" == 1 ]]; then BACKEND_BIND=http://0.0.0.0:18085; else BACKEND_BIND=http://0.0.0.0:80; fi
fi
BACKEND_URL=$PRIME_BACKEND_URL
if [[ -z "$BACKEND_URL" ]]; then
    if [[ "$START_BACKEND" == 1 ]]; then
        BACKEND_URL=http://51.161.113.128:18085/
    else
        BACKEND_URL=http://51.161.113.128/
    fi
fi
NODE_PUBLIC_HOST=$PRIME_NODE_PUBLIC_HOST
if [[ -z "$NODE_PUBLIC_HOST" ]]; then
    NODE_PUBLIC_HOST=51.161.113.128
fi
NODE_CONTROL_URI=$PRIME_NODE_PUBLIC_CONTROL_URI
if [[ -z "$NODE_CONTROL_URI" ]]; then
    NODE_CONTROL_URI=wss://$NODE_PUBLIC_HOST:8443/v1/control
fi
# shellcheck disable=SC2153
NODE_BIND=$PRIME_NODE_BIND
if [[ -z "$NODE_BIND" ]]; then
    NODE_BIND=https://0.0.0.0:8443
fi
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
TICKET_ISSUER=$PRIME_TICKET_ISSUER
if [[ -z "$TICKET_ISSUER" ]]; then TICKET_ISSUER=https://$NODE_PUBLIC_HOST; fi
ARTIFACT_DIR=$STATE_DIR/artifacts
REPLAY_DIR=$STATE_DIR/replays

MAP_DIR=$PACKAGE_DIR/maps
[[ -d "$MAP_DIR" ]] || { echo "Worker map directory is missing: $MAP_DIR" >&2; exit 1; }
DESCRIPTOR_PATH=$STATE_DIR/content-description.json
if ! DESCRIPTOR_TMP=$(mktemp "$STATE_DIR/.content-description.XXXXXX"); then
    echo "Unable to create the Worker content descriptor in $STATE_DIR." >&2
    exit 1
fi
if ! "$WORKER_PATH" --describe-content true --content-dir "$CONTENT_DIR" \
    --content-version "$CONTENT_VERSION" --map-dir "$PACKAGE_DIR/maps" > "$DESCRIPTOR_TMP"; then
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

read -r BACKEND_SCHEME BACKEND_HOST < <(python3 - "$BACKEND_URL" <<'PY'
import sys
from urllib.parse import urlparse
u = urlparse(sys.argv[1])
if not u.scheme or not u.hostname or u.username or u.password or u.query or u.fragment:
    raise SystemExit("Backend URL must be an origin without credentials, query, or fragment.")
print(u.scheme.lower(), u.hostname.lower())
PY
)
if [[ "$BACKEND_SCHEME" != http && "$BACKEND_SCHEME" != https ]]; then
    echo "Backend URL must use HTTP or HTTPS: $BACKEND_URL" >&2
    exit 1
fi
if [[ "$BACKEND_SCHEME" == http && "$BACKEND_HOST" != 51.161.113.128 && "$BACKEND_HOST" != localhost && "$BACKEND_HOST" != 127.0.0.1 && "$BACKEND_HOST" != ::1 ]]; then
    echo "Plain HTTP is limited to 51.161.113.128 or loopback." >&2
    exit 1
fi

value_from_file() {
    file=$1
    code=$2
    if [[ ! -s "$file" ]]; then
        python3 -c "$code" > "$file"
        chmod 600 "$file"
    fi
    tr -d '\r\n' < "$file"
}
check_uuid() {
    python3 - "$1" <<'PY'
import sys,uuid
try: value=uuid.UUID(sys.argv[1])
except ValueError: raise SystemExit(1)
if value.int == 0: raise SystemExit(1)
PY
}

NODE_SECRET_FILE=
if [[ "$START_BACKEND" == 1 ]]; then
    NODE_ID=$PRIME_NODE_ID
    if [[ -z "$NODE_ID" ]]; then NODE_ID=$(value_from_file "$STATE_DIR/node-id" 'import uuid; print(uuid.uuid4())'); fi
    check_uuid "$NODE_ID" || { echo "PRIME_NODE_ID must be a non-empty UUID." >&2; exit 1; }
    TICKET_KEY=$PRIME_TICKET_SIGNING_KEY
    if [[ -z "$TICKET_KEY" ]]; then TICKET_KEY=$STATE_DIR/ticket-signing.pem; fi
    need openssl
    if [[ ! -s "$TICKET_KEY" ]]; then
        openssl ecparam -name prime256v1 -genkey -noout -out "$TICKET_KEY"
        chmod 600 "$TICKET_KEY"
    fi
    PUBLIC_KEY=$STATE_DIR/node-public.pem
    openssl ec -in "$TICKET_KEY" -pubout -out "$PUBLIC_KEY" 2>/dev/null
    chmod 644 "$PUBLIC_KEY"
    NODE_SECRET=$PRIME_NODE_DIRECTORY_SECRET
    if [[ -z "$NODE_SECRET" ]]; then NODE_SECRET=$(value_from_file "$STATE_DIR/node-directory-secret" 'import secrets; print(secrets.token_urlsafe(48))'); fi
    [[ $(printf '%s' "$NODE_SECRET" | wc -c) -ge 32 ]] || { echo "Node directory credential must contain at least 32 characters." >&2; exit 1; }
else
    NODE_ID=$PRIME_NODE_ID
    if [[ -z "$NODE_ID" ]] || ! check_uuid "$NODE_ID"; then
        echo "Shared Backend mode needs a valid PRIME_NODE_ID." >&2
        exit 1
    fi
    PUBLIC_KEY=$PRIME_NODE_PUBLIC_KEY_FILE
    [[ -s "$PUBLIC_KEY" ]] || { echo "Shared Backend mode needs PRIME_NODE_PUBLIC_KEY_FILE." >&2; exit 1; }
    if [[ "$PUBLIC_KEY" != /* ]]; then PUBLIC_KEY=$(cd "$(dirname "$PUBLIC_KEY")" && pwd)/$(basename "$PUBLIC_KEY"); fi
    NODE_SECRET=$PRIME_NODE_DIRECTORY_SECRET
    NODE_SECRET_FILE=$PRIME_NODE_DIRECTORY_SECRET_FILE
    if [[ -n "$NODE_SECRET" && -n "$NODE_SECRET_FILE" ]]; then
        echo "Configure one Node directory credential source." >&2
        exit 1
    fi
    if [[ -z "$NODE_SECRET" ]]; then
        [[ -s "$NODE_SECRET_FILE" ]] || { echo "Shared Backend mode needs PRIME_NODE_DIRECTORY_SECRET or PRIME_NODE_DIRECTORY_SECRET_FILE." >&2; exit 1; }
        NODE_SECRET_FILE=$(cd "$(dirname "$NODE_SECRET_FILE")" && pwd)/$(basename "$NODE_SECRET_FILE")
    else
        [[ $(printf '%s' "$NODE_SECRET" | wc -c) -ge 32 ]] || { echo "Node directory credential must contain at least 32 characters." >&2; exit 1; }
    fi
    TICKET_KEY=
fi

need openssl
CERT_PFX=$PRIME_NODE_CERT_PFX
if [[ -z "$CERT_PFX" ]]; then CERT_PFX=$STATE_DIR/node-tls.pfx; fi
CERT_PASSWORD=$PRIME_NODE_CERT_PASSWORD
if [[ -n "$PRIME_NODE_CERT_PFX" && -z "$CERT_PASSWORD" ]]; then
    echo "PRIME_NODE_CERT_PASSWORD is required when PRIME_NODE_CERT_PFX is supplied." >&2
    exit 1
fi
if [[ -z "$CERT_PASSWORD" && -s "$STATE_DIR/node-tls-password" ]]; then CERT_PASSWORD=$(tr -d '\r\n' < "$STATE_DIR/node-tls-password"); fi
if python3 - "$NODE_PUBLIC_HOST" <<'PY'
import ipaddress,sys
try: ipaddress.ip_address(sys.argv[1])
except ValueError: raise SystemExit(1)
PY
then
    TLS_SAN=IP
else
    TLS_SAN=DNS
fi
if [[ -z "$PRIME_NODE_CERT_PFX" && -s "$CERT_PFX" && -n "$CERT_PASSWORD" ]]; then
    if [[ "$TLS_SAN" == IP ]]; then
        CERT_CHECK=(openssl x509 -checkip "$NODE_PUBLIC_HOST" -noout)
    else
        CERT_CHECK=(openssl x509 -checkhost "$NODE_PUBLIC_HOST" -noout)
    fi
    if ! openssl pkcs12 -in "$CERT_PFX" -clcerts -nokeys -passin "pass:$CERT_PASSWORD" 2>/dev/null \
        | "${CERT_CHECK[@]}" >/dev/null 2>&1; then
        echo "Existing generated Node TLS does not cover $NODE_PUBLIC_HOST; regenerating it."
        rm -f "$CERT_PFX" "$STATE_DIR/node-tls.key" "$STATE_DIR/node-tls.crt"
    fi
fi
if [[ ! -s "$CERT_PFX" ]]; then
    CERT_PASSWORD=$(value_from_file "$STATE_DIR/node-tls-password" 'import secrets; print(secrets.token_urlsafe(32))')
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
        -keyout "$STATE_DIR/node-tls.key" -out "$STATE_DIR/node-tls.crt" \
        -subj "/CN=$NODE_PUBLIC_HOST" -addext "subjectAltName=$TLS_SAN:$NODE_PUBLIC_HOST" >/dev/null 2>&1
    openssl pkcs12 -export -out "$CERT_PFX" -inkey "$STATE_DIR/node-tls.key" \
        -in "$STATE_DIR/node-tls.crt" -passout "pass:$CERT_PASSWORD" >/dev/null 2>&1
    chmod 600 "$CERT_PFX" "$STATE_DIR/node-tls.key" "$STATE_DIR/node-tls.crt" "$STATE_DIR/node-tls-password"
    echo "Generated self-signed Node TLS at $CERT_PFX; trusted PFX is recommended for testers."
fi
[[ -n "$CERT_PASSWORD" && -s "$CERT_PFX" ]] || { echo "Node PFX and password are required." >&2; exit 1; }

CONFIG_PATH=$STATE_DIR/appsettings.json
export CONFIG_PATH DESCRIPTOR_PATH MAP_DIR CONTENT_VERSION NODE_ID TICKET_ISSUER KEY_ID PUBLIC_KEY MAP_KEY
export CONTENT_DIR ARTIFACT_DIR REPLAY_DIR WORKER_HOST WORKER_BIND LANES MAX_MATCHES MAX_MATCHES_PER_LANE PLAYER_LIMIT
export MAX_LOBBIES MAX_SESSIONS NODE_NAME NODE_REGION NODE_CONTROL_URI BACKEND_URL PACKAGE_DIR
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
        maps.append({"MapKey": key, "Modes": sorted(normalized_modes)})

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

def n(name):
    value=int(os.environ[name])
    if value < 1: raise SystemExit(name+" must be positive")
    return value
lanes=n("LANES"); matches=n("MAX_MATCHES"); per_lane=n("MAX_MATCHES_PER_LANE"); players=n("PLAYER_LIMIT")
if matches > lanes * per_lane: raise SystemExit("Worker match capacity exceeds lane capacity")
cfg={"Node":{
    "MaximumLobbies":n("MAX_LOBBIES"),"MaximumSessions":n("MAX_SESSIONS"),
    "Authentication":{"NodeId":os.environ["NODE_ID"],"Issuer":os.environ["TICKET_ISSUER"],
                      "Keys":[{"KeyId":os.environ["KEY_ID"],"PublicKeyPemPath":os.environ["PUBLIC_KEY"]}]},
    "Maps":[{"MapKey":entry["MapKey"],**content,"Modes":entry["Modes"]} for entry in maps],
    "Workers":{"DrainTimeout":"00:00:30","ForceAfterDrainDeadline":False,"Processes":[{
        "FileName":"worker/FruityPrime.Server.Worker","Content":content,
        "ArtifactDirectory":os.environ["ARTIFACT_DIR"],"WorkingDirectory":os.environ["PACKAGE_DIR"],
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

BACKEND_HEALTH_URL=$PRIME_BACKEND_HEALTH_URL
if [[ -z "$BACKEND_HEALTH_URL" ]]; then
    if [[ "$START_BACKEND" == 1 ]]; then
        BACKEND_HEALTH_URL=$BACKEND_BIND
        BACKEND_HEALTH_URL=${BACKEND_HEALTH_URL/0.0.0.0/127.0.0.1}
        BACKEND_HEALTH_URL=${BACKEND_HEALTH_URL/'[::]'/'127.0.0.1'}
    else
        BACKEND_HEALTH_URL=$BACKEND_URL
    fi
fi

BACKEND_PID=
BACKEND_LOG=
if [[ "$START_BACKEND" == 1 ]]; then
    if [[ -z "$ConnectionStrings__Backend" ]]; then
        ConnectionStrings__Backend='Host=127.0.0.1;Database=prime_dev;Username=prime_dev;Password=prime_dev'
        echo "No PostgreSQL connection supplied; guest/node endpoints work, account endpoints need PostgreSQL and migrations."
    fi
    SECRET_HASH=$(printf '%s' "$NODE_SECRET" | python3 -c 'import hashlib,sys; print(hashlib.sha256(sys.stdin.buffer.read()).hexdigest().upper())')
    BACKEND_LOG=$STATE_DIR/backend.log
    if [[ -z "$BACKEND_EXECUTABLE" && ! -x "$ROOT/src/Backend/bin/Release/net10.0/PrimeHunters.Backend" ]]; then need dotnet; fi
    echo "Starting Backend on $BACKEND_BIND (logs: $BACKEND_LOG)"
    # shellcheck disable=SC2030
    (
        export ASPNETCORE_ENVIRONMENT=Development
        export ASPNETCORE_URLS=$BACKEND_BIND
        export Backend__AllowRemoteHttp=true Backend__AllowLoopbackHttp=true
        export Accounts__DataProtectionKeyPath=$STATE_DIR/data-protection
        export Tickets__Issuer=$TICKET_ISSUER Tickets__KeyId=$KEY_ID Tickets__SigningKeyPemPath=$TICKET_KEY
        export GameServers__Servers__0__Id=$NODE_ID GameServers__Servers__0__Enabled=true
        export GameServers__Servers__0__ApiKeySha256=$SECRET_HASH GameServers__Servers__0__TrustClass=Community
        export ConnectionStrings__Backend
        if [[ -n "$BACKEND_EXECUTABLE" ]]; then
            "$BACKEND_EXECUTABLE"
        elif [[ -x "$ROOT/src/Backend/bin/Release/net10.0/PrimeHunters.Backend" ]]; then
            "$ROOT/src/Backend/bin/Release/net10.0/PrimeHunters.Backend"
        else
            dotnet run --project "$ROOT/src/Backend/Backend.csproj" --no-launch-profile
        fi
    ) > "$BACKEND_LOG" 2>&1 &
    BACKEND_PID=$!
fi

NODE_PID=
cleanup() {
    status=$?
    trap - EXIT INT TERM
    [[ -n "$NODE_PID" && "$NODE_PID" != 0 ]] && kill "$NODE_PID" 2>/dev/null || true
    [[ -n "$BACKEND_PID" && "$BACKEND_PID" != 0 ]] && kill "$BACKEND_PID" 2>/dev/null || true
    wait "$NODE_PID" 2>/dev/null || true
    wait "$BACKEND_PID" 2>/dev/null || true
    rm -f "$STATE_DIR/node.pid" "$STATE_DIR/backend.pid"
    exit "$status"
}
trap cleanup EXIT INT TERM
if [[ -n "$BACKEND_PID" ]]; then printf '%s\n' "$BACKEND_PID" > "$STATE_DIR/backend.pid"; fi

HEALTH=$BACKEND_HEALTH_URL
if [[ "$HEALTH" != */ ]]; then HEALTH=$HEALTH/; fi
BUILD_QUERY=$(python3 -c 'from urllib.parse import quote; import sys; print(quote(sys.argv[1], safe=""))' "$BUILD_VERSION")
HEALTH=$HEALTH"v1/nodes?protocol=1&build=$BUILD_QUERY&content=$CONTENT_HASH"
echo "Waiting for Backend: $BACKEND_URL"
for ((i=0;i<30;i++)); do
    if [[ -n "$BACKEND_PID" ]] && ! kill -0 "$BACKEND_PID" 2>/dev/null; then
        echo "Backend exited; see $BACKEND_LOG" >&2
        exit 1
    fi
    if curl --fail --silent --show-error --max-time 3 "$HEALTH" >/dev/null 2>&1; then
        if [[ -n "$BACKEND_PID" ]]; then
            # A previous Backend can answer this health request while the
            # newly started child is still failing its Kestrel bind. Give the
            # child a short startup window, then verify that it is still ours
            # before accepting the response as readiness.
            sleep 1
            if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
                echo "Backend exited; see $BACKEND_LOG" >&2
                exit 1
            fi
        fi
        break
    fi
    if ((i == 29)); then
        FAILURE_LOG=$BACKEND_LOG
        if [[ -z "$FAILURE_LOG" ]]; then FAILURE_LOG="the shared Backend"; fi
        echo "Backend did not become reachable; see $FAILURE_LOG." >&2
        exit 1
    fi
    sleep 1
done

NODE_LOG=$STATE_DIR/node.log
echo "Starting Server Node (logs: $NODE_LOG)"
 # shellcheck disable=SC2031
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

NODE_HEALTH=$PRIME_NODE_HEALTH_URL
if [[ -z "$NODE_HEALTH" ]]; then
    if [[ "$START_BACKEND" == 1 ]]; then NODE_HEALTH=https://127.0.0.1:8443/health
    else NODE_HEALTH=$(printf '%s' "$NODE_CONTROL_URI" | sed 's#^wss://#https://#; s#/v1/control$#/health#'); fi
fi
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

echo "Development stack is running."
echo "  Backend: $BACKEND_URL"
echo "  Node:    $NODE_CONTROL_URI"
echo "  State:   $STATE_DIR"
echo "  Stop:    Ctrl-C"
if [[ -z "$PRIME_NODE_CERT_PFX" ]]; then echo "  TLS:     self-signed; give testers the certificate or set PRIME_NODE_CERT_PFX."; fi
wait "$NODE_PID"
