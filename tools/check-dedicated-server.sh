#!/usr/bin/env bash
# Asset-free startup contract. Python owns exact child processes and bounded
# process-tree teardown on POSIX and Windows (including Git Bash paths).
set -euo pipefail
PYTHON=python3
command -v "$PYTHON" >/dev/null 2>&1 || PYTHON=python
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DIR="${1:-publish/linux-x64}"
if command -v cygpath >/dev/null 2>&1; then
  SCRIPT_DIR="$(cygpath -w "$SCRIPT_DIR")"
  DIR="$(cygpath -w "$DIR")"
fi
exec "$PYTHON" "$SCRIPT_DIR/dedicated_smoke.py" "$DIR"
