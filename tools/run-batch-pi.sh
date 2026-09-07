#!/usr/bin/env bash
# Connect to an operator-configured server; no SSH, deployment, service restart
# or remote rotation mutation. Required: MPH_SERVER_HOST, GAME_DATA_DIRECTORY.
# run-batch-pi.sh [runs=8] [seed] [max-extra-ms=150]
# MPH_SERVER_PORT defaults to 27888. Extra delay is bounded by max-extra-ms;
# randomized packet loss is still applied when extra delay is zero.
# All local runtime/output/duration overrides are documented in run-batch.sh.
set -euo pipefail
if (( $# > 3 )); then
  echo 'usage: run-batch-pi.sh [runs] [seed] [max-extra-ms]' >&2
  exit 2
fi
export MPH_BATCH_REMOTE=1 BATCH_MAX_EXTRA_MS="${3:-150}"
TOOLS_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$TOOLS_DIRECTORY/run-batch.sh" "${1:-8}" "${2:-$RANDOM}"
