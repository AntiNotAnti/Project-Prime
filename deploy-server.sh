#!/usr/bin/env bash
# Build and deploy one combined Node + Worker bundle. Game content stays on the
# remote host; appsettings.json is supplied explicitly by the operator because
# it contains the Node admission public-key path and content identity.
#
# Example:
#   MPH_SERVER_HOST=games.example.com MPH_SERVER_USER=gameuser \
#   MPH_SERVER_CONFIG=./appsettings.games.json \
#   MPH_SERVER_DATA=/srv/fruity-content ./deploy-server.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_HOST="${MPH_SERVER_HOST:-net.livetek.fr}"
DEPLOY_USER="${MPH_SERVER_USER:-livetek}"
DEPLOY_DIR="${MPH_SERVER_DIR:-/home/$DEPLOY_USER/fruityprime-server}"
DEPLOY_CONFIG="${MPH_SERVER_CONFIG:?Set MPH_SERVER_CONFIG to an operator-authored Node appsettings file}"
DEPLOY_DATA="${MPH_SERVER_DATA:?Set MPH_SERVER_DATA to the existing absolute content directory on the remote machine}"
DEPLOY_VERSION="${MPH_SERVER_DATA_VERSION:-AMHE1}"
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/fruity-prime-deploy.XXXXXXXX")"
REMOTE_STAGE="${DEPLOY_DIR}.staging.$(basename "$STAGE")"
REMOTE_STAGE_CREATED=0

shell_quote() { printf "'%s'" "${1//\'/\'\\\'\'}"; }
ssh_run() {
  if [[ -n "${MPH_SERVER_PASS:-}" ]]; then
    SSHPASS="$MPH_SERVER_PASS" sshpass -e ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  else
    # Callers build fully POSIX-quoted remote commands before invoking ssh_run.
    # shellcheck disable=SC2029
    ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  fi
}
remote() {
  local command="" argument
  for argument in "$@"; do command+="$(shell_quote "$argument") "; done
  ssh_run "$command"
}
upload_file() {
  remote python3 -c '
import shutil, sys
with open(sys.argv[1], "xb") as target:
    shutil.copyfileobj(sys.stdin.buffer, target, 1024 * 1024)
' "$2" < "$1"
}
cleanup() {
  if [[ "$REMOTE_STAGE_CREATED" == 1 ]]; then remote rm -rf "$REMOTE_STAGE" || true; fi
  rm -rf "$STAGE"
}
trap cleanup EXIT

[[ -f "$DEPLOY_CONFIG" ]] || { echo "MPH_SERVER_CONFIG is not a regular file" >&2; exit 1; }
[[ -r "$DEPLOY_CONFIG" ]] || { echo "MPH_SERVER_CONFIG is not readable" >&2; exit 1; }
[[ -s "$DEPLOY_CONFIG" ]] || { echo "MPH_SERVER_CONFIG is empty" >&2; exit 1; }
[[ "$DEPLOY_DATA" = /* ]] || { echo "MPH_SERVER_DATA must be an absolute remote path" >&2; exit 1; }
[[ "$DEPLOY_DIR" = /* ]] || { echo "MPH_SERVER_DIR must be an absolute remote path" >&2; exit 1; }
python3 -m json.tool "$DEPLOY_CONFIG" >/dev/null

echo "Checking installed content on $DEPLOY_HOST..."
remote python3 -c '
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
manifest = root / "server-content.json"
if not root.is_dir() or not (manifest.is_file() or (
    (root / "_bin/arm9.bin").is_file() and (root / "models").is_dir() and (root / "levels").is_dir())):
    raise SystemExit("Missing installed server content at " + str(root) + "; install it separately before deploying")
if manifest.is_file():
    if manifest.stat().st_size > 2 * 1024 * 1024:
        raise SystemExit("Installed content manifest exceeds 2 MiB")
    if json.loads(manifest.read_text()).get("Version") != sys.argv[2]:
        raise SystemExit("Installed content manifest does not match " + sys.argv[2])
' "$DEPLOY_DATA" "$DEPLOY_VERSION"

echo "Building linux-arm64 Node + Worker bundle..."
tools=("$ROOT/tools/package-server.sh" --rid linux-arm64 --output "$STAGE/package")
"${tools[@]}"
bash "$ROOT/tools/check-no-game-assets.sh" "$STAGE/package"
cp "$DEPLOY_CONFIG" "$STAGE/package/appsettings.json"

python3 - "$ROOT/tools/systemd/fruityprime-node.service" "$STAGE/package/fruityprime-node.service" "$DEPLOY_USER" "$DEPLOY_DIR" <<'PY'
from pathlib import Path
import sys
source, target, user, directory = map(Path, sys.argv[1:])
text = source.read_text()
text = text.replace("__USER__", str(user)).replace("__DIR__", str(directory))
target.write_text(text)
PY

# Stage the whole bundle beside the live directory. The current installation
# remains untouched until package, config, and unit upload have completed.
remote mkdir -p "$(dirname "$DEPLOY_DIR")"
remote mkdir -m 700 "$REMOTE_STAGE"
REMOTE_STAGE_CREATED=1
tar -C "$STAGE/package" -czf - . | ssh_run "tar -xzf - -C $(shell_quote "$REMOTE_STAGE")"
remote chmod 600 "$REMOTE_STAGE/appsettings.json"
remote chmod +x "$REMOTE_STAGE/FruityPrimeServer" "$REMOTE_STAGE/worker/FruityPrime.Server.Worker"
remote python3 -c '
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
config = json.loads((root / "appsettings.json").read_text())
if not (root / "FruityPrimeServer").is_file() or not (root / "worker/FruityPrime.Server.Worker").is_file():
    raise SystemExit("staged Node + Worker apphosts are incomplete")
if not config.get("Node", {}).get("Authentication", {}).get("Keys"):
    raise SystemExit("operator config has no Node admission verification keys")
' "$REMOTE_STAGE"

# Stop and replace only the new Node unit. Legacy mphread-* units are not
# silently migrated; operators must perform that migration explicitly.
remote sudo -n systemctl stop fruityprime-node || true
remote sudo -n systemctl disable fruityprime-node || true
REMOTE_BACKUP="${DEPLOY_DIR}.previous.$(basename "$STAGE")"
remote "if test -e $(shell_quote "$DEPLOY_DIR"); then mv $(shell_quote "$DEPLOY_DIR") $(shell_quote "$REMOTE_BACKUP"); fi; mv $(shell_quote "$REMOTE_STAGE") $(shell_quote "$DEPLOY_DIR")"
REMOTE_STAGE_CREATED=0
remote sudo -n install -m 644 "$DEPLOY_DIR/fruityprime-node.service" /etc/systemd/system/fruityprime-node.service
remote sudo -n systemctl daemon-reload
remote sudo -n systemctl enable fruityprime-node
remote sudo -n systemctl start fruityprime-node
remote sudo -n systemctl is-active fruityprime-node
echo "Deployment complete. Content and operator configuration stayed on the remote host; no cartridge assets were uploaded."
