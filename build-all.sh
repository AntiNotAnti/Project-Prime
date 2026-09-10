#!/usr/bin/env bash
# Build every deployable Project Prime client and server artifact.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
exec "$ROOT/tools/build-all.sh" "$@"
