#!/usr/bin/env bash
# Restart the complete Project Prime development server stack from a checkout.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ENV_FILE=${PRIME_DEV_ENV_FILE:-$ROOT/.env.dev}
if [[ -f "$ENV_FILE" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
fi
STATE_DIR=""
GRACE_SECONDS=60
MODE=start
FORWARD_ARGS=()

usage() {
    cat <<'USAGE'
Usage: ./start-server.sh [control options] [start-dev options]

Stops a verified Project Prime development stack that owns the selected state
directory, then starts Backend, Server Node, and Node-managed Workers.

Control options:
  --status             report the selected stack without changing it
  --stop-only          stop the selected stack and do not restart it
  --grace-seconds N    seconds to allow a graceful stop (default: 60)
  --help               show this help

Start options passed through to start-dev.sh:
  --content-dir PATH   AMHE1 content directory
  --package-dir PATH   host-compatible server bundle
  --state-dir PATH     runtime state and logs

This all-in-one entry point always starts the local development Backend. Use
tools/start-dev.sh directly when intentionally using a shared Backend.
Environment overrides may be placed in .env.dev or selected with
PRIME_DEV_ENV_FILE.
USAGE
}

while (($#)); do
    case "$1" in
        --status) MODE=status; shift ;;
        --stop-only) MODE=stop; shift ;;
        --grace-seconds)
            [[ $# -ge 2 ]] || { echo "--grace-seconds needs a value." >&2; exit 2; }
            GRACE_SECONDS=$2
            shift 2
            ;;
        --state-dir)
            [[ $# -ge 2 ]] || { echo "--state-dir needs a path." >&2; exit 2; }
            STATE_DIR=$2
            FORWARD_ARGS+=("$1" "$2")
            shift 2
            ;;
        --help|-h) usage; exit 0 ;;
        *) FORWARD_ARGS+=("$1"); shift ;;
    esac
done

case "$GRACE_SECONDS" in
    ''|*[!0-9]*) echo "--grace-seconds must be a non-negative integer." >&2; exit 2 ;;
esac

CONTROL_ARGS=(--root "$ROOT" --grace-seconds "$GRACE_SECONDS")
if [[ -n "$STATE_DIR" ]]; then CONTROL_ARGS+=(--state-dir "$STATE_DIR"); fi

if [[ "$MODE" == status ]]; then
    exec python3 "$ROOT/tools/dev-stack-control.py" status "${CONTROL_ARGS[@]}"
fi

python3 "$ROOT/tools/dev-stack-control.py" stop "${CONTROL_ARGS[@]}"
if [[ "$MODE" == stop ]]; then exit 0; fi
exec "$ROOT/start-dev.sh" "${FORWARD_ARGS[@]}"
