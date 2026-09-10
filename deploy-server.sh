#!/usr/bin/env bash
# Transactionally deploy the production Node and its managed Workers.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ENV_FILE=${PRIME_DEPLOY_ENV_FILE:-$ROOT/.env.deploy}
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi

DEPLOY_HOST=${MPH_SERVER_HOST:-net.livetek.fr}
DEPLOY_USER=${MPH_SERVER_USER:-livetek}
DEPLOY_DIR=${MPH_SERVER_DIR:-/home/$DEPLOY_USER/fruityprime-server}
DEPLOY_CONFIG=${MPH_SERVER_CONFIG:-}
DEPLOY_DATA=${MPH_SERVER_DATA:-}
DEPLOY_VERSION=${MPH_SERVER_DATA_VERSION:-AMHE1}
DEPLOY_PASS=${MPH_SERVER_PASS:-}
BUNDLE=${PRIME_DEPLOY_BUNDLE:-${MPH_SERVER_BUNDLE:-}}
RID=${MPH_SERVER_RID:-linux-arm64}
HEALTH_URL=${MPH_SERVER_HEALTH_URL:-https://127.0.0.1:8443/health}
KEEP_RELEASES=${MPH_SERVER_KEEP_RELEASES:-3}
PREFLIGHT_ONLY=0

usage() {
  cat <<'USAGE'
Usage: ./deploy-server.sh [options]

Deploys the production Project Prime Server Node and its managed Workers with
verified releases, an atomic current pointer, health checks, and rollback.
The bundled Backend is uploaded but is never installed or started.

  --host HOST          SSH host (MPH_SERVER_HOST)
  --user USER          SSH/service user (MPH_SERVER_USER)
  --deploy-dir PATH    remote deploy root (MPH_SERVER_DIR)
  --config FILE        local operator Node config (MPH_SERVER_CONFIG)
  --data PATH          existing remote AMHE1 content (MPH_SERVER_DATA)
  --data-version VER   content version, default AMHE1
  --bundle DIR         precompiled bundle (PRIME_DEPLOY_BUNDLE; MPH_SERVER_BUNDLE compatible)
  --rid RID            linux-arm64 or linux-x64 (default linux-arm64)
  --health-url URL     loopback Node health URL
  --keep-releases N    successful release retention, minimum 2 (default 3)
  --preflight-only     local validation plus remote read-only checks; no upload/stop
  --help               show this help

With no --bundle, a fresh temporary bundle is built with package-server.sh.
Secrets may be supplied through an ignored .env.deploy or PRIME_DEPLOY_ENV_FILE;
MPH_SERVER_PASS remains compatible and is never printed. CLI values win.
USAGE
}

while (($#)); do
  case "$1" in
    --host) [[ $# -ge 2 ]] || { echo "--host needs a value" >&2; exit 2; }; DEPLOY_HOST=$2; shift 2 ;;
    --user) [[ $# -ge 2 ]] || { echo "--user needs a value" >&2; exit 2; }; DEPLOY_USER=$2; shift 2 ;;
    --deploy-dir) [[ $# -ge 2 ]] || { echo "--deploy-dir needs a value" >&2; exit 2; }; DEPLOY_DIR=$2; shift 2 ;;
    --config) [[ $# -ge 2 ]] || { echo "--config needs a value" >&2; exit 2; }; DEPLOY_CONFIG=$2; shift 2 ;;
    --data) [[ $# -ge 2 ]] || { echo "--data needs a value" >&2; exit 2; }; DEPLOY_DATA=$2; shift 2 ;;
    --data-version) [[ $# -ge 2 ]] || { echo "--data-version needs a value" >&2; exit 2; }; DEPLOY_VERSION=$2; shift 2 ;;
    --bundle) [[ $# -ge 2 ]] || { echo "--bundle needs a value" >&2; exit 2; }; BUNDLE=$2; shift 2 ;;
    --rid) [[ $# -ge 2 ]] || { echo "--rid needs a value" >&2; exit 2; }; RID=$2; shift 2 ;;
    --health-url) [[ $# -ge 2 ]] || { echo "--health-url needs a value" >&2; exit 2; }; HEALTH_URL=$2; shift 2 ;;
    --keep-releases) [[ $# -ge 2 ]] || { echo "--keep-releases needs a value" >&2; exit 2; }; KEEP_RELEASES=$2; shift 2 ;;
    --preflight-only) PREFLIGHT_ONLY=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$RID" in linux-arm64|linux-x64) ;; *) echo "--rid must be linux-arm64 or linux-x64" >&2; exit 2 ;; esac
case "$KEEP_RELEASES" in ''|*[!0-9]*) echo "--keep-releases must be an integer of at least 2" >&2; exit 2 ;; esac
[[ "$KEEP_RELEASES" -ge 2 ]] || { echo "--keep-releases must be at least 2" >&2; exit 2; }
[[ -n "$DEPLOY_CONFIG" ]] || { echo "Set MPH_SERVER_CONFIG or --config" >&2; exit 2; }
[[ -n "$DEPLOY_DATA" ]] || { echo "Set MPH_SERVER_DATA or --data" >&2; exit 2; }
[[ "$DEPLOY_HOST" =~ ^[A-Za-z0-9_.:-]+$ && "$DEPLOY_USER" =~ ^[A-Za-z_][A-Za-z0-9_.-]*$ ]] || { echo "Invalid remote host or user" >&2; exit 2; }

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing command: $1" >&2; exit 1; }; }
for command_name in python3 tar ssh file; do need "$command_name"; done
if [[ -n "$DEPLOY_PASS" ]]; then need sshpass; fi
python3 - "$HEALTH_URL" <<'PY'
import sys
from urllib.parse import urlparse
try:
    value=urlparse(sys.argv[1]); port=value.port
except ValueError:
    raise SystemExit("MPH_SERVER_HEALTH_URL is invalid")
if value.scheme not in ("http","https") or value.hostname not in ("127.0.0.1","localhost") \
        or value.username or value.password or port is None or value.query or value.fragment:
    raise SystemExit("MPH_SERVER_HEALTH_URL must be an HTTP(S) loopback URL with an explicit port and no credentials")
PY

STAGE=$(mktemp -d "${TMPDIR:-/tmp}/project-prime-deploy.XXXXXXXX")
REMOTE_LOCK_HELD=0
ACTIVATION_DISPATCHED=0
REMOTE_STAGE_CREATED=0
REMOTE_CONFIG_CREATED=0
REMOTE_UNIT_CREATED=0
RELEASE_ID=$(date -u +%Y%m%dT%H%M%SZ)-$RID-$$
RELEASES_DIR=$DEPLOY_DIR/releases
REMOTE_STAGE=$RELEASES_DIR/$RELEASE_ID.staging
REMOTE_RELEASE=$RELEASES_DIR/$RELEASE_ID
REMOTE_CONFIG_STAGE=$DEPLOY_DIR/.appsettings.$RELEASE_ID.staging
REMOTE_UNIT_STAGE=$DEPLOY_DIR/.fruityprime-node.$RELEASE_ID.service
REMOTE_LOCK=$DEPLOY_DIR/.deploy.lock

shell_quote() { printf "'%s'" "${1//\'/\'\\\'\'}"; }
ssh_run() {
  if [[ -n "$DEPLOY_PASS" ]]; then
    SSHPASS=$DEPLOY_PASS sshpass -e ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  else
    # shellcheck disable=SC2029
    ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  fi
}
remote() {
  local command="" argument
  for argument in "$@"; do command="$command$(shell_quote "$argument") "; done
  ssh_run "$command"
}
remote_remove_exact() {
  remote python3 -c 'import pathlib,shutil,sys
p=pathlib.Path(sys.argv[1]); root=pathlib.Path(sys.argv[2])
if p.parent != root: raise SystemExit("refusing unscoped cleanup")
if p.is_dir() and not p.is_symlink(): shutil.rmtree(p)
elif p.exists() or p.is_symlink(): p.unlink()' "$1" "$2"
}
upload_new_file() {
  remote python3 -c 'import pathlib,shutil,sys
path=pathlib.Path(sys.argv[1]); created=False
try:
    with path.open("xb") as target:
        created=True
        shutil.copyfileobj(sys.stdin.buffer,target,1024*1024)
except BaseException:
    if created:
        try: path.unlink()
        except OSError: pass
    raise' "$2" < "$1"
}
cleanup() {
  cleanup_status=$?
  trap - EXIT INT TERM
  if [[ "$ACTIVATION_DISPATCHED" == 0 ]]; then
    if [[ "$REMOTE_STAGE_CREATED" == 1 ]]; then remote_remove_exact "$REMOTE_STAGE" "$RELEASES_DIR" || true; fi
    if [[ "$REMOTE_CONFIG_CREATED" == 1 ]]; then remote rm -f -- "$REMOTE_CONFIG_STAGE" || true; fi
    if [[ "$REMOTE_UNIT_CREATED" == 1 ]]; then remote rm -f -- "$REMOTE_UNIT_STAGE" || true; fi
    if [[ "$REMOTE_LOCK_HELD" == 1 ]]; then remote rmdir "$REMOTE_LOCK" || true; fi
  elif [[ "$REMOTE_LOCK_HELD" == 1 ]]; then
    echo "Remote activation outcome is uncertain; deployment lock retained at $REMOTE_LOCK" >&2
  fi
  rm -rf "$STAGE"
  exit "$cleanup_status"
}
trap cleanup EXIT INT TERM

if [[ -z "$BUNDLE" ]]; then
  echo "Building fresh $RID combined server bundle..."
  "$ROOT/tools/package-server.sh" --rid "$RID" --output "$STAGE/built-package"
  BUNDLE=$STAGE/built-package
fi
[[ -d "$BUNDLE" && ! -L "$BUNDLE" ]] || { echo "Bundle must be a real directory, not a symlink" >&2; exit 1; }
BUNDLE=$(cd "$BUNDLE" 2>/dev/null && pwd -P) || { echo "Bundle directory does not exist" >&2; exit 1; }

KEY_PATH_FILE=$STAGE/key-paths.txt
python3 "$ROOT/tools/validate-deploy-inputs.py" --bundle "$BUNDLE" --rid "$RID" \
  --config "$DEPLOY_CONFIG" --deploy-dir "$DEPLOY_DIR" --data-dir "$DEPLOY_DATA" --key-path-output "$KEY_PATH_FILE"
for executable in "$BUNDLE/FruityPrimeServer" "$BUNDLE/worker/FruityPrime.Server.Worker" "$BUNDLE/backend/PrimeHunters.Backend"; do
  description=$(file -b "$executable")
  case "$RID:$description" in
    linux-arm64:*ARM*aarch64*|linux-arm64:*ARM64*|linux-x64:*x86-64*) ;;
    *) echo "Bundle/RID mismatch: $executable is $description" >&2; exit 1 ;;
  esac
done
bash "$ROOT/tools/check-no-game-assets.sh" "$BUNDLE"
bash "$ROOT/tools/check-maps-shipped.sh" "$BUNDLE"

mkdir "$STAGE/package"
tar -C "$BUNDLE" -cf - . | tar -C "$STAGE/package" -xf -
python3 - "$STAGE/package" <<'PY'
import hashlib,pathlib,sys
root=pathlib.Path(sys.argv[1]); lines=[]
for path in sorted(item for item in root.rglob('*') if item.is_file()):
    lines.append(hashlib.sha256(path.read_bytes()).hexdigest()+'  '+path.relative_to(root).as_posix())
(root/'.deploy-manifest.sha256').write_text('\n'.join(lines)+'\n',encoding='ascii')
PY
python3 - "$ROOT/tools/systemd/fruityprime-node.service" "$STAGE/fruityprime-node.service" "$DEPLOY_USER" "$DEPLOY_DIR" <<'PY'
from pathlib import Path
import sys
source,target,user,directory=sys.argv[1:]
text=Path(source).read_text(encoding='utf-8')
text=text.replace('__USER__',user).replace('__DIR__',directory).replace('__CONFIG_DIR__',directory+'/config')
Path(target).write_text(text,encoding='utf-8')
PY
CONFIG_SHA=$(python3 -c 'import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$DEPLOY_CONFIG")
UNIT_SHA=$(python3 -c 'import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$STAGE/fruityprime-node.service")
BUNDLE_KB=$(du -sk "$STAGE/package" | awk '{print $1}')
REQUIRED_KB=$((BUNDLE_KB * 3 + 102400))

KEY_ARGS=""
while IFS= read -r key_path; do KEY_ARGS="$KEY_ARGS $(shell_quote "$key_path")"; done < "$KEY_PATH_FILE"
echo "Running read-only remote preflight on $DEPLOY_HOST..."
ssh_run "bash -s -- $(shell_quote "$DEPLOY_DIR") $(shell_quote "$DEPLOY_DATA") $(shell_quote "$DEPLOY_VERSION") $(shell_quote "$RID") $(shell_quote "$REQUIRED_KB")$KEY_ARGS" <<'REMOTE_PREFLIGHT'
set -eu
deploy=$1; data=$2; version=$3; rid=$4; required_kb=$5; shift 5
for command_name in python3 tar curl; do command -v "$command_name" >/dev/null 2>&1 || { echo "Remote is missing $command_name" >&2; exit 1; }; done
sudo -n true >/dev/null
sudo -n systemctl --version >/dev/null
case "$(uname -m):$rid" in aarch64:linux-arm64|arm64:linux-arm64|x86_64:linux-x64|amd64:linux-x64) ;; *) echo "Remote architecture does not match $rid" >&2; exit 1 ;; esac
python3 - "$deploy" "$data" "$version" "$@" <<'PY'
import json,os,pathlib,sys
deploy=pathlib.Path(sys.argv[1]); data=pathlib.Path(sys.argv[2]); version=sys.argv[3]
def safe(path,label):
    if not path.is_absolute() or '..' in path.parts or str(path) in ('/','/home','/srv','/opt','/usr','/var','/tmp'): raise SystemExit(label+' is unsafe')
safe(deploy,'deploy root'); safe(data,'content path')
for path,label in ((deploy,'deploy root'),(deploy/'releases','release root')):
    if path.is_symlink(): raise SystemExit(label+' must not be a symlink')
    if path.exists() and not path.is_dir(): raise SystemExit(label+' must be a directory')
current=deploy/'current'
if current.exists() and not current.is_symlink(): raise SystemExit('current must be a symlink')
manifest=data/'server-content.json'
if not data.is_dir() or not (manifest.is_file() or ((data/'_bin/arm9.bin').is_file() and (data/'models').is_dir() and (data/'levels').is_dir())): raise SystemExit('installed server content is missing')
if manifest.is_file() and json.loads(manifest.read_text()).get('Version') != version: raise SystemExit('content version mismatch')
for path in (data, data/'models', data/'levels'):
    if not os.access(path, os.R_OK | os.X_OK): raise SystemExit('installed server content is not readable: '+str(path))
if not manifest.is_file():
    try:
        with (data/'_bin/arm9.bin').open('rb') as handle: handle.read(1)
    except OSError: raise SystemExit('installed server content is not readable: '+str(data/'_bin/arm9.bin'))
for key in sys.argv[4:]:
    path=pathlib.Path(key); safe(path,'authentication public key')
    if not path.is_file() or not path.stat().st_size:
        raise SystemExit('authentication public key is not readable: '+str(path))
    try:
        with path.open('rb') as handle: handle.read(1)
    except OSError:
        raise SystemExit('authentication public key is not readable: '+str(path))
PY
probe=$deploy
while [ ! -e "$probe" ]; do probe=$(dirname "$probe"); done
available=$(df -Pk "$probe" | awk 'NR==2 {print $4}')
[ -n "$available" ] && [ "$available" -ge "$required_kb" ] || { echo "Insufficient remote disk space" >&2; exit 1; }
REMOTE_PREFLIGHT

if [[ "$PREFLIGHT_ONLY" == 1 ]]; then
  echo "Preflight complete; no upload occurred and fruityprime-node was not stopped."
  exit 0
fi

echo "Acquiring remote deployment lock..."
ssh_run "bash -s -- $(shell_quote "$DEPLOY_DIR") $(shell_quote "$RELEASES_DIR") $(shell_quote "$REMOTE_LOCK")" <<'REMOTE_LOCK'
set -eu
deploy=$1; releases=$2; lock=$3
[ ! -L "$deploy" ] && [ ! -L "$releases" ] || { echo "Deploy roots became symlinks; refusing" >&2; exit 1; }
mkdir -p "$deploy" "$releases"
[ ! -L "$deploy" ] && [ ! -L "$releases" ] || { echo "Deploy roots changed during lock acquisition; refusing" >&2; exit 1; }
chmod 700 "$deploy" "$releases"
if ! mkdir -m 700 "$lock" 2>/dev/null; then
  echo "Deployment lock already exists: $lock" >&2
  echo "After verifying no deployment is active, recover with: rmdir '$lock'" >&2
  exit 75
fi
REMOTE_LOCK
REMOTE_LOCK_HELD=1

remote mkdir -m 700 "$REMOTE_STAGE"
REMOTE_STAGE_CREATED=1
tar -C "$STAGE/package" -czf - . | ssh_run "tar -xzf - -C $(shell_quote "$REMOTE_STAGE")"
upload_new_file "$DEPLOY_CONFIG" "$REMOTE_CONFIG_STAGE"
REMOTE_CONFIG_CREATED=1
upload_new_file "$STAGE/fruityprime-node.service" "$REMOTE_UNIT_STAGE"
REMOTE_UNIT_CREATED=1
remote chmod 600 "$REMOTE_CONFIG_STAGE" "$REMOTE_UNIT_STAGE"

echo "Verifying uploaded release before service downtime..."
ssh_run "bash -s -- $(shell_quote "$REMOTE_STAGE") $(shell_quote "$REMOTE_RELEASE") $(shell_quote "$REMOTE_CONFIG_STAGE") $(shell_quote "$CONFIG_SHA") $(shell_quote "$REMOTE_UNIT_STAGE") $(shell_quote "$UNIT_SHA")" <<'REMOTE_VERIFY'
set -eu
stage=$1; release=$2; config=$3; config_sha=$4; unit=$5; unit_sha=$6
[ -d "$stage" ] && [ ! -e "$release" ] || { echo "Release staging collision" >&2; exit 1; }
python3 - "$stage" "$config" "$config_sha" "$unit" "$unit_sha" <<'PY'
import hashlib,pathlib,sys
root=pathlib.Path(sys.argv[1]); manifest=root/'.deploy-manifest.sha256'; expected={}
for line in manifest.read_text(encoding='ascii').splitlines():
    digest,relative=line.split('  ',1)
    if relative.startswith('/') or '..' in pathlib.PurePosixPath(relative).parts: raise SystemExit('unsafe manifest path')
    expected[relative]=digest
actual={}
for path in root.rglob('*'):
    if path.is_symlink() or not (path.is_file() or path.is_dir()): raise SystemExit('unsafe staged file')
    if path.is_file() and path != manifest: actual[path.relative_to(root).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
if actual != expected: raise SystemExit('uploaded SHA-256 manifest verification failed')
for path,want in ((pathlib.Path(sys.argv[2]),sys.argv[3]),(pathlib.Path(sys.argv[4]),sys.argv[5])):
    if hashlib.sha256(path.read_bytes()).hexdigest()!=want: raise SystemExit('uploaded operator file hash mismatch')
PY
chmod -R go-rwx "$stage"
mv "$stage" "$release"
REMOTE_VERIFY
REMOTE_STAGE_CREATED=0

echo "Activating $RELEASE_ID..."
ACTIVATION_DISPATCHED=1
if ! ssh_run "bash -s -- $(shell_quote "$DEPLOY_DIR") $(shell_quote "$RELEASE_ID") $(shell_quote "$DEPLOY_USER") $(shell_quote "$REMOTE_CONFIG_STAGE") $(shell_quote "$REMOTE_UNIT_STAGE") $(shell_quote "$HEALTH_URL") $(shell_quote "$KEEP_RELEASES")" <<'REMOTE_ACTIVATE'
set -Eeuo pipefail
deploy=$1; release_id=$2; service_user=$3; config_stage=$4; unit_stage=$5; health=$6; keep=$7
releases=$deploy/releases; release=$releases/$release_id; current=$deploy/current; rollback=$deploy/.rollback-$release_id
prior_target=""; prior_active=absent; prior_enabled=absent; had_unit=0; had_config=0; legacy_migrated=0; activated=0
mkdir -m 700 "$rollback"
early_activation_exit() {
  status=$?; trap - EXIT
  rm -rf "$rollback" || true
  rm -f "$config_stage" "$unit_stage" || true
  rmdir "$deploy/.deploy.lock" 2>/dev/null || true
  exit "$status"
}
trap early_activation_exit EXIT
if [ -L "$current" ]; then
  prior_target=$(readlink "$current")
  prior_name=${prior_target#releases/}
  if [[ "$prior_target" != "releases/$prior_name" || ! "$prior_name" =~ ^[0-9]{8}T[0-9]{6}Z-linux-(arm64|x64)-[0-9]+$ ]]; then
    echo "Unsafe current release link: $prior_target" >&2; exit 1
  fi
  [ -d "$deploy/$prior_target" ] && [ -f "$deploy/$prior_target/.deploy-manifest.sha256" ] \
    || { echo "Current release target is incomplete" >&2; exit 1; }
elif [ -e "$current" ]; then echo "Current path is not a symlink" >&2; exit 1
fi
if sudo -n test -L /etc/systemd/system/fruityprime-node.service; then
  echo "Symlinked or masked fruityprime-node units are unsupported; refusing before downtime" >&2; exit 1
elif sudo -n test -e /etc/systemd/system/fruityprime-node.service \
    && ! sudo -n test -f /etc/systemd/system/fruityprime-node.service; then
  echo "Non-regular fruityprime-node unit is unsupported; refusing before downtime" >&2; exit 1
elif sudo -n test -f /etc/systemd/system/fruityprime-node.service; then
  sudo -n cp /etc/systemd/system/fruityprime-node.service "$rollback/unit"
  sudo -n chown "$service_user" "$rollback/unit"
  had_unit=1
  prior_active=$(sudo -n systemctl is-active fruityprime-node 2>/dev/null || true)
  prior_enabled=$(sudo -n systemctl is-enabled fruityprime-node 2>/dev/null || true)
  case "$prior_active" in active|inactive) ;; *) echo "Unsupported pre-deploy service state: $prior_active" >&2; exit 1 ;; esac
  case "$prior_enabled" in enabled|disabled) ;; *) echo "Unsupported pre-deploy enablement state: $prior_enabled" >&2; exit 1 ;; esac
else
  load_state=$(sudo -n systemctl show -p LoadState --value fruityprime-node 2>/dev/null || true)
  if [ "$load_state" != not-found ]; then
    echo "A fruityprime-node unit exists outside /etc/systemd/system; refusing to replace unsupported unit state: ${load_state:-unknown}" >&2; exit 1
  fi
fi
if [ -f "$deploy/config/appsettings.json" ]; then cp "$deploy/config/appsettings.json" "$rollback/appsettings.json"; had_config=1; fi
rollback_deploy() {
  status=$?; trap - ERR EXIT; [ "$status" -ne 0 ] || status=1
  if [ "$activated" -ne 1 ]; then
    rm -rf "$rollback" || true
    rmdir "$deploy/.deploy.lock" 2>/dev/null || true
    exit "$status"
  fi
  rollback_ok=1
  if ! sudo -n systemctl stop fruityprime-node >/dev/null 2>&1 \
      && sudo -n systemctl is-active --quiet fruityprime-node 2>/dev/null; then rollback_ok=0; fi
  sudo -n journalctl -u fruityprime-node -n 100 --no-pager > "$release/deploy-failure-journal.txt" 2>&1 || true
  if [ -n "$prior_target" ]; then
    if ! ln -s "$prior_target" "$deploy/.current.rollback.$release_id" \
        || ! mv -Tf "$deploy/.current.rollback.$release_id" "$current"; then rollback_ok=0; fi
  else
    if ! rm -f "$current"; then rollback_ok=0; fi
    if [ "$legacy_migrated" -eq 1 ]; then
      legacy=$releases/$release_id-legacy
      for name in FruityPrimeServer worker backend maps start-dev.sh start-stack-dev.sh server.example.json; do
        if [ -e "$legacy/$name" ] && ! mv "$legacy/$name" "$deploy/$name"; then rollback_ok=0; fi
      done
      if ! rmdir "$legacy" 2>/dev/null; then rollback_ok=0; fi
    fi
  fi
  if [ "$had_config" -eq 1 ]; then
    if ! install -m 600 "$rollback/appsettings.json" "$deploy/config/appsettings.json"; then rollback_ok=0; fi
  elif ! rm -f "$deploy/config/appsettings.json"; then rollback_ok=0
  fi
  if [ "$had_unit" -eq 1 ]; then
    if ! sudo -n install -m 644 "$rollback/unit" /etc/systemd/system/fruityprime-node.service; then rollback_ok=0; fi
  else
    if ! sudo -n systemctl disable fruityprime-node >/dev/null 2>&1; then rollback_ok=0; fi
    if ! sudo -n rm -f /etc/systemd/system/fruityprime-node.service; then rollback_ok=0; fi
  fi
  if ! sudo -n systemctl daemon-reload; then rollback_ok=0; fi
  if [ "$had_unit" -eq 1 ]; then
    if [ "$prior_enabled" = enabled ]; then
      if ! sudo -n systemctl enable fruityprime-node >/dev/null; then rollback_ok=0; fi
    elif ! sudo -n systemctl disable fruityprime-node >/dev/null 2>&1; then rollback_ok=0
    fi
    if [ "$prior_active" = active ]; then
      if ! sudo -n systemctl start fruityprime-node \
          || ! sudo -n systemctl is-active --quiet fruityprime-node; then rollback_ok=0; fi
    elif sudo -n systemctl is-active --quiet fruityprime-node 2>/dev/null; then rollback_ok=0
    fi
  fi
  if [ "$rollback_ok" -ne 1 ]; then
    echo "CRITICAL: activation and automatic rollback both failed; recovery snapshot and deployment lock retained: $rollback" >&2
    exit "$status"
  fi
  rm -rf "$rollback" || true
  rm -f "$config_stage" "$unit_stage" || true
  rmdir "$deploy/.deploy.lock" || { echo "Rollback succeeded but deployment lock could not be released" >&2; exit "$status"; }
  echo "Activation failed; previous unit/current/config state was restored. Failed release: $release" >&2
  exit "$status"
}
trap rollback_deploy ERR EXIT

activated=1
case "$prior_active" in
  active)
    sudo -n systemctl stop fruityprime-node
    ! sudo -n systemctl is-active --quiet fruityprime-node || { echo "fruityprime-node remained active after stop" >&2; false; }
    ;;
esac
if [ -z "$prior_target" ] && [ -f "$deploy/FruityPrimeServer" ]; then
  for name in FruityPrimeServer worker backend maps start-dev.sh start-stack-dev.sh server.example.json; do
    [ ! -L "$deploy/$name" ] || { echo "Refusing symlinked legacy component: $name" >&2; false; }
  done
  legacy=$releases/$release_id-legacy; mkdir -m 700 "$legacy"; legacy_migrated=1
  for name in FruityPrimeServer worker backend maps start-dev.sh start-stack-dev.sh server.example.json; do [ ! -e "$deploy/$name" ] || mv "$deploy/$name" "$legacy/$name"; done
fi
mkdir -p "$deploy/config"; chmod 700 "$deploy/config"
install -m 600 "$config_stage" "$deploy/config/appsettings.json.new"; mv -f "$deploy/config/appsettings.json.new" "$deploy/config/appsettings.json"
ln -s "releases/$release_id" "$deploy/.current.$release_id"; mv -Tf "$deploy/.current.$release_id" "$current"
sudo -n install -m 644 "$unit_stage" /etc/systemd/system/fruityprime-node.service
sudo -n systemctl daemon-reload
sudo -n systemctl enable fruityprime-node
sudo -n systemctl start fruityprime-node
healthy=0
for attempt in $(seq 1 30); do
  if curl --fail --silent --show-error --insecure --connect-timeout 2 --max-time 4 "$health" >/dev/null 2>&1; then healthy=1; break; fi
  sleep 1
done
[ "$healthy" -eq 1 ] || { echo "Node health check failed" >&2; false; }
main_pid=$(sudo -n systemctl show -p MainPID --value fruityprime-node)
case "$main_pid" in ''|0|*[!0-9]*) echo "systemd returned invalid MainPID" >&2; false ;; esac
exe=$(sudo -n readlink -f "/proc/$main_pid/exe")
case "$exe" in "$release"/*) ;; *) echo "MainPID executable is outside selected release: $exe" >&2; false ;; esac
rm -f "$config_stage" "$unit_stage"
python3 - "$releases" "$release_id" "$prior_target" "$keep" <<'PY'
import pathlib,re,shutil,sys
root=pathlib.Path(sys.argv[1]); current=sys.argv[2]; previous=pathlib.PurePosixPath(sys.argv[3]).name if sys.argv[3] else ''; keep=max(2,int(sys.argv[4])); protected={current,previous}
release_name=re.compile(r'^\d{8}T\d{6}Z-linux-(?:arm64|x64)-\d+$')
children=[]
for path in root.iterdir():
    if path.is_symlink() or not path.is_dir() or path.name in protected \
            or not release_name.fullmatch(path.name) or not (path/'.deploy-manifest.sha256').is_file():
        continue
    children.append(path)
children.sort(key=lambda path:(path.stat().st_mtime,path.name),reverse=True)
for path in children[max(0,keep-len([name for name in protected if name])):]:
    if path.parent==root and path.name not in protected: shutil.rmtree(path)
PY
activated=0; trap - ERR EXIT
rm -rf "$rollback" || echo "Warning: completed deployment left rollback scratch at $rollback" >&2
rmdir "$deploy/.deploy.lock"
REMOTE_ACTIVATE
then
  if remote test ! -d "$REMOTE_LOCK" 2>/dev/null; then REMOTE_LOCK_HELD=0; fi
  echo "Deployment activation did not complete; inspect the retained release/lock diagnostics before retrying." >&2
  exit 1
fi
REMOTE_LOCK_HELD=0
REMOTE_CONFIG_CREATED=0
REMOTE_UNIT_CREATED=0
echo "Deployment complete: $REMOTE_RELEASE"
echo "Only fruityprime-node and its managed Workers were activated; Backend and external AMHE1 content were untouched."
