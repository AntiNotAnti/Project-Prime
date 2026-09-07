#!/usr/bin/env bash
# Build and upload only the ARM64 binary. Content must already exist remotely.
# Example: MPH_SERVER_HOST=games.example.com MPH_SERVER_USER=gameuser \
#   MPH_SERVER_DATA=/srv/fruity-content ./deploy-server.sh
set -euo pipefail

DEPLOY_HOST="${MPH_SERVER_HOST:-net.livetek.fr}"
DEPLOY_USER="${MPH_SERVER_USER:-livetek}"
DEPLOY_DIR="${MPH_SERVER_DIR:-/home/$DEPLOY_USER/mphread-server}"
DEPLOY_DATA="${MPH_SERVER_DATA:?Set MPH_SERVER_DATA to the existing absolute content directory on the remote machine}"
DEPLOY_VERSION="${MPH_SERVER_DATA_VERSION:-AMHE1}"
DEPLOY_MASTER="${MPH_DEPLOY_MASTER:-1}"
DEPLOY_LISTING="${MPH_SERVER_MASTER:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAGE="$(mktemp -d -t fruity-deploy.XXXXXXXX)"
REMOTE_STAGE="$DEPLOY_DIR/.$(basename "$STAGE")"
REMOTE_STAGE_CREATED=0

# Pass each remote argument through POSIX shell quoting. No configuration value
# becomes shell source, including paths containing spaces or apostrophes.
shell_quote() { printf "'%s'" "${1//\'/\'\\\'\'}"; }
ssh_run() {
  # SC2029: remote() passes a complete POSIX-quoted command string. The caller
  # deliberately constructs it locally; no unquoted configuration enters it.
  # shellcheck disable=SC2029
  if [[ -n "${MPH_SERVER_PASS:-}" ]]; then
    SSHPASS="$MPH_SERVER_PASS" sshpass -e ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  else
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
  if [[ "$REMOTE_STAGE_CREATED" == 1 ]]; then remote rm -rf -- "$REMOTE_STAGE" || true; fi
  rm -rf -- "$STAGE"
}
trap cleanup EXIT

# Validate arguments and template rendering before opening an SSH connection.
server_args=(--user "$DEPLOY_USER" --directory "$DEPLOY_DIR" --data "$DEPLOY_DATA" --version "$DEPLOY_VERSION")
[[ -z "$DEPLOY_LISTING" ]] || server_args+=(--master "$DEPLOY_LISTING")
python3 "$ROOT/tools/render-server-unit.py" "$ROOT/tools/systemd/mphread-server.service" \
  "${server_args[@]}" > "$STAGE/mphread-server.service"
[[ "$DEPLOY_MASTER" == 0 || "$DEPLOY_MASTER" == 1 ]] || { echo "MPH_DEPLOY_MASTER must be 0 or 1" >&2; exit 1; }

printf 'Checking installed content on %s...\n' "$DEPLOY_HOST"
remote python3 -c '
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
manifest = root / "server-content.json"
if not root.is_dir() or not (manifest.is_file() or (
    (root / "_bin/arm9.bin").is_file() and (root / "models").is_dir() and (root / "levels").is_dir())):
    raise SystemExit("Missing installed server content at " + str(root) + "; install it separately before deploying")
if manifest.is_file() and (manifest.stat().st_size > 2 * 1024 * 1024 or json.loads(manifest.read_text()).get("Version") != sys.argv[2]):
    raise SystemExit("Installed content manifest does not match " + sys.argv[2])
' "$DEPLOY_DATA" "$DEPLOY_VERSION"

printf 'Building linux-arm64...\n'
dotnet publish "$ROOT/src/MphRead/MphRead.csproj" -c Release -r linux-arm64 -p:MphReadServer=true \
  --self-contained true -p:PublishSingleFile=true -o "$STAGE/publish"
test -f "$STAGE/publish/FruityPrime"
bash "$ROOT/tools/check-no-game-assets.sh" "$STAGE/publish"

services=(mphread-server)
[[ "$DEPLOY_MASTER" == 0 ]] || services+=(mphread-master)
for service in "${services[@]}"; do
  source="$ROOT/tools/systemd/$service.service"
  if remote test -f "/etc/systemd/system/$service.service"; then
    remote cat "/etc/systemd/system/$service.service" > "$STAGE/$service.original"
    source="$STAGE/$service.original"
  fi
  args=(--user "$DEPLOY_USER" --directory "$DEPLOY_DIR")
  [[ "$service" != mphread-server ]] || args=("${server_args[@]}")
  python3 "$ROOT/tools/render-server-unit.py" "$source" "${args[@]}" > "$STAGE/$service.service"
done

# Upload exactly one binary and the rendered units; never upload a publish tree,
# an extracted directory, paths.txt, or a content package.
remote mkdir -p -- "$DEPLOY_DIR"
remote mkdir -m 700 -- "$REMOTE_STAGE"
REMOTE_STAGE_CREATED=1
upload_file "$STAGE/publish/FruityPrime" "$REMOTE_STAGE/FruityPrime"
remote chmod +x -- "$REMOTE_STAGE/FruityPrime"
for service in "${services[@]}"; do
  upload_file "$STAGE/$service.service" "$REMOTE_STAGE/$service.service"
done

# Keep the current processes running until upload and configuration validation
# have succeeded. A failed stop is an error, not permission to replace a server.
for service in "${services[@]}"; do
  if remote test -f "/etc/systemd/system/$service.service"; then
    remote sudo -n systemctl stop "$service"
  fi
done
remote mv -- "$REMOTE_STAGE/FruityPrime" "$DEPLOY_DIR/FruityPrime"
for service in "${services[@]}"; do
  remote sudo -n install -m 644 -- "$REMOTE_STAGE/$service.service" "/etc/systemd/system/$service.service"
done
remote sudo -n systemctl daemon-reload
for service in "${services[@]}"; do
  remote sudo -n systemctl enable "$service"
  remote sudo -n systemctl start "$service"
done
sleep 3
for service in "${services[@]}"; do
  remote systemctl is-active "$service"
  remote journalctl -u "$service" -n 8 --no-pager
done
printf 'Deployment complete. Content stayed on the remote machine; no cartridge assets were uploaded.\n'
