#!/usr/bin/env bash
# Start the complete local development stack from the repository checkout.
# The underlying launcher creates development credentials and supervises the
# Backend, Server Node, and Node-managed Worker processes.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
exec "$ROOT/tools/start-dev.sh" "$@" --with-backend
