#!/usr/bin/env bash
# Validate a combined Backend + Node + Worker server bundle. Runtime gameplay requires
# private AMHE1 content; when GAME_DATA_DIRECTORY is present, delegate to the
# full fresh-extracted package smoke. Without it, keep the check structural and
# data-free rather than starting the retired standalone server CLI.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE="${1:-publish/server-linux-x64}"
PACKAGE="$(cd "$PACKAGE" && pwd)"

if [[ ! -x "$PACKAGE/FruityPrimeServer" && ! -f "$PACKAGE/FruityPrimeServer.exe" ]]; then
  echo "FAIL: combined Node apphost is missing from $PACKAGE" >&2
  exit 1
fi
if [[ ! -x "$PACKAGE/backend/PrimeHunters.Backend" && ! -f "$PACKAGE/backend/PrimeHunters.Backend.exe" ]]; then
  echo "FAIL: Backend apphost is missing below $PACKAGE/backend" >&2
  exit 1
fi
if [[ ! -x "$PACKAGE/worker/FruityPrime.Server.Worker" && ! -f "$PACKAGE/worker/FruityPrime.Server.Worker.exe" ]]; then
  echo "FAIL: Worker apphost is missing below $PACKAGE/worker" >&2
  exit 1
fi
[[ -f "$PACKAGE/server.example.json" ]] || { echo "FAIL: example server config is missing" >&2; exit 1; }
! find "$PACKAGE" -maxdepth 1 -type f -name 'FruityPrime.Server.Worker*' | grep -q . || {
  echo "FAIL: Worker leaked into the Node bundle root" >&2; exit 1;
}
bash "$ROOT/tools/check-no-game-assets.sh" "$PACKAGE"

if [[ -n "${GAME_DATA_DIRECTORY:-}" ]]; then
  exec "$ROOT/tools/package-smoke.sh" --bundle "$PACKAGE" --content-dir "$GAME_DATA_DIRECTORY"
fi
echo "Combined Backend + Node + Worker package contract passed; GAME_DATA_DIRECTORY is unset, so runtime smoke was skipped."
