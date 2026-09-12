#!/usr/bin/env bash
# Stable shell entry point for the incremental, single-owner map cook.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/tools/cook-maps.py" "$@"
