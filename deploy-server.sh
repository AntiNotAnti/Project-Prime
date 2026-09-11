#!/usr/bin/env bash
# Transactionally deploy the complete Project Prime Backend + Node + Worker stack.
set -Eeuo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ENV_FILE=${PRIME_DEPLOY_ENV_FILE:-$ROOT/.env.deploy}
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi

DEPLOY_HOST=${MPH_SERVER_HOST:-51.161.113.128}
DEPLOY_USER=${MPH_SERVER_USER:-ubuntu}
DEPLOY_DIR=${MPH_SERVER_DIR:-/srv/project-prime}
DEPLOY_CONFIG=${MPH_SERVER_CONFIG:-}
DEPLOY_DATA=${MPH_SERVER_DATA:-/srv/project-prime/AMHE1}
DEPLOY_VERSION=${MPH_SERVER_DATA_VERSION:-AMHE1}
DEPLOY_PASS=${MPH_SERVER_PASS:-}
BUNDLE=${PRIME_DEPLOY_BUNDLE:-${MPH_SERVER_BUNDLE:-}}
RID=${MPH_SERVER_RID:-linux-x64}
STATE_DIR=${MPH_SERVER_STATE_DIR:-$DEPLOY_DIR/state}
STACK_ENV=${MPH_SERVER_ENV_FILE:-$STATE_DIR/dev.env}
PUBLIC_HOST=${PRIME_NODE_PUBLIC_HOST:-rebooty.xyz}
PUBLIC_CONTROL_URI=${PRIME_NODE_PUBLIC_CONTROL_URI:-wss://$PUBLIC_HOST:8443/v1/control}
NODE_HEALTH=${MPH_SERVER_HEALTH_URL:-https://127.0.0.1:8443/health}
BACKEND_HEALTH=${MPH_BACKEND_HEALTH_URL:-http://127.0.0.1:18085/health/ready}
HEALTH_TIMEOUT=${MPH_SERVER_HEALTH_TIMEOUT:-180}
KEEP_RELEASES=${MPH_SERVER_KEEP_RELEASES:-3}
PREFLIGHT_ONLY=0

usage() {
  cat <<'USAGE'
Usage: ./deploy-server.sh [options]

Deploy the complete Project Prime VPS stack. The default target is
ubuntu@51.161.113.128, linux-x64, rooted at /srv/project-prime.

  --host HOST          SSH host (MPH_SERVER_HOST)
  --user USER          SSH/service user (MPH_SERVER_USER)
  --deploy-dir PATH    remote root (MPH_SERVER_DIR)
  --data PATH          existing remote AMHE1 path (MPH_SERVER_DATA)
  --data-version VER   content version (default AMHE1)
  --bundle DIR         precompiled combined bundle (MPH_SERVER_BUNDLE)
  --rid RID            linux-x64 or linux-arm64 (default linux-x64)
  --config FILE        accepted compatibility option; stack config is generated
  --preflight-only     validate locally/remotely without upload or downtime
  --keep-releases N    retain at least current + previous (default 3)
  --help               show this help

Without --bundle a fresh package is built. The packaged start-stack-dev.sh
generates appsettings and credentials in persistent <root>/state, sourcing
<root>/state/dev.env. The legacy MPH_SERVER_CONFIG name remains accepted but
is not uploaded or read because production configuration is state-generated.
USAGE
}

while (($#)); do
  case "$1" in
    --host) [[ $# -ge 2 ]] || exit 2; DEPLOY_HOST=$2; shift 2 ;;
    --user) [[ $# -ge 2 ]] || exit 2; DEPLOY_USER=$2; shift 2 ;;
    --deploy-dir) [[ $# -ge 2 ]] || exit 2; DEPLOY_DIR=$2; STATE_DIR=$DEPLOY_DIR/state; STACK_ENV=$STATE_DIR/dev.env; shift 2 ;;
    --data) [[ $# -ge 2 ]] || exit 2; DEPLOY_DATA=$2; shift 2 ;;
    --data-version) [[ $# -ge 2 ]] || exit 2; DEPLOY_VERSION=$2; shift 2 ;;
    --bundle) [[ $# -ge 2 ]] || exit 2; BUNDLE=$2; shift 2 ;;
    --rid) [[ $# -ge 2 ]] || exit 2; RID=$2; shift 2 ;;
    --config) [[ $# -ge 2 ]] || exit 2; DEPLOY_CONFIG=$2; shift 2 ;;
    --keep-releases) [[ $# -ge 2 ]] || exit 2; KEEP_RELEASES=$2; shift 2 ;;
    --preflight-only) PREFLIGHT_ONLY=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$RID" in linux-x64|linux-arm64) ;; *) echo "Unsupported deployment RID: $RID" >&2; exit 2 ;; esac
case "$KEEP_RELEASES" in ''|*[!0-9]*) echo "MPH_SERVER_KEEP_RELEASES must be an integer" >&2; exit 2 ;; esac
[[ "$KEEP_RELEASES" -ge 2 ]] || { echo "MPH_SERVER_KEEP_RELEASES must be at least 2" >&2; exit 2; }
case "$HEALTH_TIMEOUT" in ''|*[!0-9]*) echo "MPH_SERVER_HEALTH_TIMEOUT must be an integer" >&2; exit 2 ;; esac
[[ "$HEALTH_TIMEOUT" -ge 60 ]] || { echo "MPH_SERVER_HEALTH_TIMEOUT must be at least 60 seconds" >&2; exit 2; }
[[ "$DEPLOY_HOST" =~ ^[A-Za-z0-9_.:-]+$ && "$DEPLOY_USER" =~ ^[A-Za-z_][A-Za-z0-9_.-]*$ ]] || { echo "Invalid host or user" >&2; exit 2; }
[[ "$DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || { echo "Deploy root must not contain shell-sensitive characters" >&2; exit 2; }
[[ "$PUBLIC_HOST" =~ ^[A-Za-z0-9.-]+$ && "$PUBLIC_CONTROL_URI" == wss://* ]] \
  || { echo "Invalid Project Prime public host or control URI" >&2; exit 2; }
python3 - "$DEPLOY_DIR" "$DEPLOY_DATA" "$STATE_DIR" "$STACK_ENV" <<'PY'
import pathlib,sys
for value,label in zip(sys.argv[1:],('deploy root','content','state','stack environment')):
    path=pathlib.PurePosixPath(value)
    if not path.is_absolute() or '..' in path.parts or any(ord(c)<32 for c in value): raise SystemExit(label+' path is unsafe')
deploy=pathlib.PurePosixPath(sys.argv[1])
if pathlib.PurePosixPath(sys.argv[2]) != deploy/'AMHE1' or pathlib.PurePosixPath(sys.argv[3]) != deploy/'state' \
        or pathlib.PurePosixPath(sys.argv[4]) != deploy/'state/dev.env':
    raise SystemExit('VPS content/state/env must be the exact protected paths below the deploy root')
if str(deploy) in ('/','/srv','/home','/opt','/tmp'): raise SystemExit('deploy root is too broad')
PY
if [[ -n "$DEPLOY_CONFIG" ]]; then echo "Note: MPH_SERVER_CONFIG is retained for compatibility but is not read or uploaded."; fi

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing command: $1" >&2; exit 1; }; }
for command_name in python3 rsync ssh file; do need "$command_name"; done
if [[ -n "$DEPLOY_PASS" ]]; then need sshpass; fi

WORK=$(mktemp -d "${TMPDIR:-/tmp}/project-prime-stack-deploy.XXXXXXXX")
RELEASE_ID=$(date -u +%Y%m%dT%H%M%SZ)-$RID-$$
RELEASES=$DEPLOY_DIR/releases
REMOTE_STAGE=$RELEASES/$RELEASE_ID.staging
REMOTE_RELEASE=$RELEASES/$RELEASE_ID
REMOTE_UNIT=$RELEASES/.projectprime-stack.$RELEASE_ID.service
REMOTE_LOCK=$RELEASES/.deploy-lock
LOCK_HELD=0
ACTIVATION_SENT=0
REMOTE_STAGE_CREATED=0
REMOTE_UNIT_CREATED=0

quote() { printf "'%s'" "${1//\'/\'\\\'\'}"; }
ssh_run() {
  # shellcheck disable=SC2029
  if [[ -n "$DEPLOY_PASS" ]]; then SSHPASS=$DEPLOY_PASS sshpass -e ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"
  else ssh "$DEPLOY_USER@$DEPLOY_HOST" "$@"; fi
}
rsync_upload() {
  local source=$1 destination=$2 remote_target
  remote_target="$DEPLOY_USER@$DEPLOY_HOST:$destination"
  if [[ -n "$DEPLOY_PASS" ]]; then
    SSHPASS=$DEPLOY_PASS rsync -az -e "sshpass -e ssh" -- "$source" "$remote_target"
  else
    rsync -az -- "$source" "$remote_target"
  fi
}
remote() { local command="" value; for value in "$@"; do command="$command$(quote "$value") "; done; ssh_run "$command"; }
cleanup() {
  status=$?; trap - EXIT INT TERM
  if [[ "$ACTIVATION_SENT" == 0 ]]; then
    [[ "$REMOTE_STAGE_CREATED" == 0 ]] || remote rm -rf -- "$REMOTE_STAGE" || true
    [[ "$REMOTE_UNIT_CREATED" == 0 ]] || remote rm -f -- "$REMOTE_UNIT" || true
    [[ "$LOCK_HELD" == 0 ]] || remote rmdir "$REMOTE_LOCK" || true
  elif [[ "$LOCK_HELD" == 1 ]]; then
    echo "Activation outcome is uncertain; deployment lock intentionally retained: $REMOTE_LOCK" >&2
  fi
  rm -rf "$WORK"; exit "$status"
}
trap cleanup EXIT INT TERM

if [[ -z "$BUNDLE" ]]; then
  echo "Building fresh $RID combined stack bundle..."
  "$ROOT/tools/package-server.sh" --rid "$RID" --output "$WORK/package"
  BUNDLE=$WORK/package
fi
[[ -d "$BUNDLE" && ! -L "$BUNDLE" ]] || { echo "Bundle must be a real directory" >&2; exit 1; }
BUNDLE=$(cd "$BUNDLE" && pwd -P)
python3 "$ROOT/tools/validate-deploy-inputs.py" --bundle "$BUNDLE" --rid "$RID"
for executable in "$BUNDLE/ProjectPrimeServer" "$BUNDLE/worker/ProjectPrime.Server.Worker" "$BUNDLE/backend/ProjectPrime.Backend"; do
  description=$(file -b "$executable")
  case "$RID:$description" in linux-x64:*x86-64*|linux-arm64:*ARM*aarch64*|linux-arm64:*ARM64*) ;; *) echo "Bundle/RID mismatch: $description" >&2; exit 1 ;; esac
done
bash "$ROOT/tools/check-no-game-assets.sh" "$BUNDLE"
bash "$ROOT/tools/check-maps-shipped.sh" "$BUNDLE"
mkdir "$WORK/stage"
rsync -a -- "$BUNDLE/" "$WORK/stage/"
python3 - "$WORK/stage" <<'PY'
import hashlib,pathlib,sys
root=pathlib.Path(sys.argv[1]); lines=[]
for path in sorted(p for p in root.rglob('*') if p.is_file()):
    lines.append(hashlib.sha256(path.read_bytes()).hexdigest()+'  '+path.relative_to(root).as_posix())
(root/'.deploy-manifest.sha256').write_text('\n'.join(lines)+'\n',encoding='ascii')
PY
python3 - "$ROOT/tools/systemd/projectprime-stack.service" "$WORK/projectprime-stack.service" "$DEPLOY_USER" "$DEPLOY_DIR" <<'PY'
from pathlib import Path
import sys
source,target,user,root=sys.argv[1:]
text=Path(source).read_text().replace('__USER__',user).replace('__ROOT__',root)
Path(target).write_text(text)
PY
UNIT_SHA=$(python3 -c 'import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$WORK/projectprime-stack.service")
REQUIRED_KB=$(($(du -sk "$WORK/stage" | awk '{print $1}') * 3 + 102400))

echo "Running read-only whole-stack preflight on $DEPLOY_HOST..."
SNAPSHOT=$(ssh_run "bash -s -- $(quote "$DEPLOY_DIR") $(quote "$DEPLOY_USER") $(quote "$RID") $(quote "$DEPLOY_VERSION") $(quote "$REQUIRED_KB") $(quote "$PUBLIC_HOST") $(quote "$PUBLIC_CONTROL_URI")" <<'REMOTE_PREFLIGHT'
set -eu
root=$1; user=$2; rid=$3; version=$4; required_kb=$5; public_host=$6; public_control=$7
app=$root/app; state=$root/state; data=$root/AMHE1; releases=$root/releases; env_file=$state/dev.env
for name in python3 rsync curl; do command -v "$name" >/dev/null || { echo "Missing remote command: $name" >&2; exit 1; }; done
sudo -n true >/dev/null; sudo -n systemctl --version >/dev/null
if [[ "$(uname -m):$rid" != x86_64:linux-x64 && "$(uname -m):$rid" != amd64:linux-x64 && "$(uname -m):$rid" != aarch64:linux-arm64 && "$(uname -m):$rid" != arm64:linux-arm64 ]]; then echo "Remote architecture/RID mismatch" >&2; exit 1; fi
python3 - "$root" "$user" "$version" "$public_host" "$public_control" <<'PY'
import json,os,pathlib,pwd,shlex,stat,sys
root=pathlib.Path(sys.argv[1]); user=sys.argv[2]; version=sys.argv[3]; public_host=sys.argv[4]; public_control=sys.argv[5]
if root != pathlib.Path('/srv/project-prime') and str(root) in ('/','/srv','/home','/opt','/tmp'): raise SystemExit('unsafe deploy root')
if root.is_symlink() or not root.is_dir(): raise SystemExit('deploy root must be a real directory')
st=root.stat()
if st.st_uid != 0 or st.st_gid != 0 or stat.S_IMODE(st.st_mode) != 0o755: raise SystemExit('deploy root must remain root:root 0755')
for name in ('app','state','AMHE1'):
    path=root/name
    if path.is_symlink() or not path.is_dir(): raise SystemExit(name+' must be a real protected directory')
state=root/'state'
if state.stat().st_uid != pwd.getpwnam(user).pw_uid or stat.S_IMODE(state.stat().st_mode) != 0o700: raise SystemExit('state must remain service-user owned mode 0700')
if not os.access(root/'AMHE1',os.R_OK|os.W_OK|os.X_OK): raise SystemExit('AMHE1 must be readable and writable by the service user')
if not (state/'dev.env').is_file() or (state/'dev.env').is_symlink(): raise SystemExit('state/dev.env is missing or unsafe')
public={}
for raw in (state/'dev.env').read_text(encoding='utf-8').splitlines():
    line=raw.strip()
    if not line or line.startswith('#'): continue
    key,separator,value=line.partition('=')
    if not separator or key not in ('PRIME_NODE_PUBLIC_HOST','PRIME_NODE_PUBLIC_CONTROL_URI'): continue
    if key in public: raise SystemExit('state/dev.env has duplicate '+key)
    parsed=shlex.split(value,posix=True)
    if len(parsed)!=1: raise SystemExit('state/dev.env has invalid '+key)
    public[key]=parsed[0]
if public.get('PRIME_NODE_PUBLIC_HOST') != public_host \
        or public.get('PRIME_NODE_PUBLIC_CONTROL_URI') != public_control:
    raise SystemExit('state/dev.env public rebooty endpoint does not match deployment settings')
# Keep the already-running VPS layout as an explicit migration boundary. Split
# the pre-rename names so broad branding replacements cannot rewrite them.
layouts=(
    (('Fruity'+'PrimeServer'),'start-stack-dev.sh','worker/'+('Fruity'+'Prime.Server.Worker'),'backend/'+('Prime'+'Hunters.Backend')),
    ('ProjectPrimeServer','start-stack-dev.sh','worker/ProjectPrime.Server.Worker','backend/ProjectPrime.Backend'),
)
complete=[]
for layout in layouts:
    paths=[root/'app'/relative for relative in layout]
    if all(path.is_file() and not path.is_symlink() for path in paths): complete.append(layout)
if len(complete)!=1: raise SystemExit('expected exactly one complete legacy/current app layout; found '+str(len(complete)))
releases=root/'releases'
if releases.exists() and (releases.is_symlink() or not releases.is_dir()): raise SystemExit('releases is unsafe')
manifest=root/'AMHE1/server-content.json'
if manifest.is_file() and json.loads(manifest.read_text()).get('Version') != version: raise SystemExit('content version mismatch')
PY
load=$(sudo -n systemctl show -p LoadState --value projectprime-stack 2>/dev/null || true)
python3 - "$root" "$load" <<'PY'
import pathlib,sys
root=pathlib.Path(sys.argv[1]); load=sys.argv[2]; app=root/'app'; state=root/'state'; env_file=state/'dev.env'
matches=[]
for entry in pathlib.Path('/proc').iterdir():
    if not entry.name.isdigit(): continue
    try:
        argv=[value.decode() for value in (entry/'cmdline').read_bytes().split(b'\0') if value]
        if argv != ['bash','./start-stack-dev.sh']: continue
        cwd=(entry/'cwd').resolve()
        environ=dict(value.split(b'=',1) for value in (entry/'environ').read_bytes().split(b'\0') if b'=' in value)
        start=(entry/'stat').read_text().split()[21]
    except (OSError,UnicodeError,ValueError): continue
    if cwd != app: raise SystemExit('legacy supervisor cwd mismatch')
    if environ.get(b'PRIME_DEV_ENV_FILE',b'').decode() != str(env_file) \
            or environ.get(b'PRIME_DEV_STATE_DIR',b'').decode() != str(state):
        raise SystemExit('legacy supervisor state/env mismatch')
    matches.append((int(entry.name),start))
def descendants(parent):
    selected={parent}; changed=True
    while changed:
        changed=False
        for entry in pathlib.Path('/proc').iterdir():
            if not entry.name.isdigit() or int(entry.name) in selected: continue
            try: ppid=int((entry/'stat').read_text().split()[3])
            except (OSError,ValueError): continue
            if ppid in selected: selected.add(int(entry.name)); changed=True
    return selected-{parent}
if load == 'loaded':
    if matches: raise SystemExit('manual and systemd stack instances overlap')
    print('systemd:0:0')
elif load == 'not-found':
    layouts=(
        {
            'node':app/('Fruity'+'PrimeServer'),
            'backend':app/'backend'/('Prime'+'Hunters.Backend'),
            'worker':app/'worker'/('Fruity'+'Prime.Server.Worker'),
        },
        {
            'node':app/'ProjectPrimeServer',
            'backend':app/'backend/ProjectPrime.Backend',
            'worker':app/'worker/ProjectPrime.Server.Worker',
        },
    )
    complete=[layout for layout in layouts if all(path.is_file() and not path.is_symlink() for path in layout.values())]
    if len(complete)!=1: raise SystemExit('expected exactly one complete process layout; found '+str(len(complete)))
    selected=complete[0]
    required=set(selected.values())
    owners=[]
    for candidate in matches:
        found=set()
        for pid in descendants(candidate[0]):
            try: executable=(pathlib.Path('/proc')/str(pid)/'exe').resolve()
            except OSError: continue
            if executable in required: found.add(executable)
        if found == required: owners.append(candidate)
    if len(owners) != 1: raise SystemExit('expected exactly one verified legacy stack owner; found '+str(len(owners)))
    owner=owners[0]; owned=descendants(owner[0])
    if any(pid != owner[0] and pid not in owned for pid,_ in matches):
        raise SystemExit('parallel legacy stack launcher is outside the verified supervisor tree')
    found=set()
    for pid in owned:
        proc=pathlib.Path('/proc')/str(pid)
        try:
            executable=(proc/'exe').resolve()
            argv=[value.decode() for value in (proc/'cmdline').read_bytes().split(b'\0') if value]
        except (OSError,UnicodeError): continue
        if executable in required: found.add(executable)
        if executable == selected['node'] and str(state) not in argv:
            raise SystemExit('legacy Node contentRoot does not use persistent state')
        if executable == selected['worker'] and str(root/'AMHE1') not in argv:
            raise SystemExit('legacy Worker content path mismatch')
    if found != required: raise SystemExit('legacy supervisor ancestry is missing Backend, Node, or Worker')
    print('legacy:%d:%s'%owner)
else:
    raise SystemExit('unsupported projectprime-stack unit state: '+(load or 'unknown'))
PY
probe=$root
available=$(df -Pk "$probe" | awk 'NR==2 {print $4}')
[ "$available" -ge "$required_kb" ] || { echo "Insufficient remote disk space" >&2; exit 1; }
REMOTE_PREFLIGHT
)
case "$SNAPSHOT" in legacy:[0-9]*:[0-9]*|systemd:0:0) ;; *) echo "Invalid remote stack snapshot" >&2; exit 1 ;; esac
echo "Remote stack snapshot: ${SNAPSHOT%%:*}"
if [[ "$PREFLIGHT_ONLY" == 1 ]]; then echo "Preflight complete; no upload or stop occurred."; exit 0; fi

echo "Acquiring deployment lock under releases..."
ssh_run "bash -s -- $(quote "$DEPLOY_DIR") $(quote "$DEPLOY_USER")" <<'REMOTE_LOCK'
set -eu
root=$1; user=$2; releases=$root/releases; lock=$releases/.deploy-lock
if [ ! -e "$releases" ]; then sudo -n install -d -o "$user" -g "$(id -gn "$user")" -m 700 "$releases"; fi
[ ! -L "$releases" ] && [ -d "$releases" ] && [ -w "$releases" ] || { echo "releases is not deployment-owned/writable" >&2; exit 1; }
if ! mkdir -m 700 "$lock" 2>/dev/null; then echo "Deployment lock exists: $lock; verify no deploy is active before rmdir" >&2; exit 75; fi
REMOTE_LOCK
LOCK_HELD=1
remote mkdir -m 700 "$REMOTE_STAGE"; REMOTE_STAGE_CREATED=1
rsync_upload "$WORK/stage/" "$REMOTE_STAGE/"
ssh_run "python3 -c 'import pathlib,sys; pathlib.Path(sys.argv[1]).write_bytes(sys.stdin.buffer.read())' $(quote "$REMOTE_UNIT")" < "$WORK/projectprime-stack.service"
REMOTE_UNIT_CREATED=1; remote chmod 600 "$REMOTE_UNIT"

echo "Verifying upload before whole-stack downtime..."
ssh_run "bash -s -- $(quote "$REMOTE_STAGE") $(quote "$REMOTE_RELEASE") $(quote "$REMOTE_UNIT") $(quote "$UNIT_SHA")" <<'REMOTE_VERIFY'
set -eu
stage=$1; release=$2; unit=$3; unit_sha=$4
python3 - "$stage" "$unit" "$unit_sha" <<'PY'
import hashlib,pathlib,sys
root=pathlib.Path(sys.argv[1]); manifest=root/'.deploy-manifest.sha256'; expected={}
for line in manifest.read_text().splitlines():
    digest,name=line.split('  ',1); path=pathlib.PurePosixPath(name)
    if path.is_absolute() or '..' in path.parts: raise SystemExit('unsafe manifest')
    expected[name]=digest
actual={}
for path in root.rglob('*'):
    if path.is_symlink() or not (path.is_file() or path.is_dir()): raise SystemExit('unsafe upload')
    if path.is_file() and path != manifest: actual[path.relative_to(root).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
if actual!=expected:
    missing=sorted(expected.keys()-actual.keys()); extra=sorted(actual.keys()-expected.keys())
    changed=sorted(name for name in expected.keys()&actual.keys() if expected[name]!=actual[name])
    for label,names in (('missing',missing),('extra',extra),('changed',changed)):
        if names: print('upload '+label+': '+', '.join(names[:20]),file=sys.stderr)
    raise SystemExit('uploaded bundle hash mismatch')
if hashlib.sha256(pathlib.Path(sys.argv[2]).read_bytes()).hexdigest()!=sys.argv[3]: raise SystemExit('uploaded unit hash mismatch')
PY
chmod -R go-rwx "$stage"; mv "$stage" "$release"
REMOTE_VERIFY
REMOTE_STAGE_CREATED=0

ACTIVATION_SENT=1
if ! ssh_run "bash -s -- $(quote "$DEPLOY_DIR") $(quote "$RELEASE_ID") $(quote "$SNAPSHOT") $(quote "$REMOTE_UNIT") $(quote "$NODE_HEALTH") $(quote "$BACKEND_HEALTH") $(quote "$KEEP_RELEASES") $(quote "$HEALTH_TIMEOUT")" <<'REMOTE_ACTIVATE'
set -Eeuo pipefail
root=$1; release_id=$2; snapshot=$3; unit_stage=$4; node_health=$5; backend_health=$6; keep=$7; health_timeout=$8
releases=$root/releases; release=$releases/$release_id; current=$root/current; lock=$releases/.deploy-lock; rollback=$releases/.rollback-$release_id
prior_target=app; had_unit=0; downtime=0; mode=unknown; legacy_pid=0
mkdir -m 700 "$rollback"
if [ -L "$current" ]; then prior_target=$(readlink "$current"); case "$prior_target" in app|releases/*) ;; *) echo "Unsafe current target" >&2; exit 1 ;; esac; fi
if sudo -n test -f /etc/systemd/system/projectprime-stack.service; then sudo -n cp /etc/systemd/system/projectprime-stack.service "$rollback/unit"; had_unit=1; fi
rollback_stack() {
  status=$?; trap - ERR EXIT; [ "$status" -ne 0 ] || status=1; ok=1
  if [ "$downtime" -eq 0 ]; then
    if [ "$mode" = legacy ] && kill -0 "$legacy_pid" 2>/dev/null; then
      rm -rf "$rollback"; rm -f "$unit_stage"; rmdir "$lock"; exit "$status"
    fi
    rm -rf "$rollback"; rm -f "$unit_stage"; rmdir "$lock"; exit "$status"
  fi
  if [ "$mode" = legacy ] && kill -0 "$legacy_pid" 2>/dev/null; then
    echo "CRITICAL: legacy supervisor stop is uncertain; lock/snapshot retained at $rollback" >&2
    exit "$status"
  fi
  sudo -n systemctl stop projectprime-stack >/dev/null 2>&1 || true
  sudo -n journalctl -u projectprime-stack -n 100 --no-pager > "$rollback/journal.txt" 2>&1 || true
  sudo -n ln -sfn "$prior_target" "$root/.current.rollback.$release_id" || ok=0
  sudo -n mv -Tf "$root/.current.rollback.$release_id" "$current" || ok=0
  if [ "$had_unit" -eq 1 ]; then sudo -n install -m 644 "$rollback/unit" /etc/systemd/system/projectprime-stack.service || ok=0
  else sudo -n install -m 644 "$unit_stage" /etc/systemd/system/projectprime-stack.service || ok=0; fi
  sudo -n systemctl daemon-reload || ok=0
  sudo -n systemctl enable projectprime-stack >/dev/null || ok=0
  sudo -n systemctl start projectprime-stack || ok=0
  sudo -n systemctl is-active --quiet projectprime-stack || ok=0
  healthy=0; health_deadline=$((SECONDS + health_timeout))
  while [ "$SECONDS" -lt "$health_deadline" ]; do
    if curl -fsSk --connect-timeout 2 --max-time 4 "$backend_health" >/dev/null 2>&1 \
        && curl -fsSk --connect-timeout 2 --max-time 4 "$node_health" >/dev/null 2>&1; then healthy=1; break; fi
    sleep 1
  done
  [ "$healthy" -eq 1 ] || ok=0
  if [ "$ok" -ne 1 ]; then echo "CRITICAL: stack activation and rollback failed; lock/snapshot retained at $rollback" >&2; exit "$status"; fi
  rm -rf "$rollback"; rm -f "$unit_stage"; rmdir "$lock"
  echo "Activation failed; complete prior stack restored through projectprime-stack." >&2; exit "$status"
}
trap rollback_stack ERR EXIT

mode=${snapshot%%:*}; rest=${snapshot#*:}; legacy_pid=${rest%%:*}; legacy_start=${rest##*:}
case "$mode" in
legacy)
  python3 - "$legacy_pid" "$legacy_start" "$root" <<'PY'
import pathlib,sys
pid,start,root=sys.argv[1:]; proc=pathlib.Path('/proc')/pid; app=pathlib.Path(root)/'app'
if (proc/'stat').read_text().split()[21]!=start: raise SystemExit('legacy supervisor identity changed')
argv=[x.decode() for x in (proc/'cmdline').read_bytes().split(b'\0') if x]
if argv != ['bash','./start-stack-dev.sh'] or (proc/'cwd').resolve()!=app: raise SystemExit('legacy supervisor command/cwd changed')
PY
  downtime=1
  kill -TERM "$legacy_pid"
  for unused in $(seq 1 70); do kill -0 "$legacy_pid" 2>/dev/null || break; sleep 1; done
  ! kill -0 "$legacy_pid" 2>/dev/null || { echo "Legacy stack did not stop within 70 seconds" >&2; false; }
  ;;
systemd)
  downtime=1
  sudo -n systemctl stop projectprime-stack
  ! sudo -n systemctl is-active --quiet projectprime-stack || { echo "projectprime-stack remained active" >&2; false; }
  ;;
*) echo "unsupported deployment snapshot: $snapshot" >&2; false ;;
esac

# A launcher that predates projectprime-stack may have left a child reparented
# to PID 1. Stop only known package executables below this deployment root;
# unrelated Project Prime installations are deliberately outside this set.
python3 - "$root" <<'PY'
import os,pathlib,re,signal,sys,time
root=pathlib.Path(sys.argv[1]); releases=root/'releases'
relative=(
    ('Fruity'+'PrimeServer'),
    'backend/'+('Prime'+'Hunters.Backend'),
    'worker/'+('Fruity'+'Prime.Server.Worker'),
    'ProjectPrimeServer',
    'backend/ProjectPrime.Backend',
    'worker/ProjectPrime.Server.Worker',
)
release_pattern=re.compile(r'^\d{8}T\d{6}Z-linux-(?:x64|arm64)-\d+$')
packages=[root/'app']
if releases.is_dir() and not releases.is_symlink():
    packages.extend(path for path in releases.iterdir()
                    if path.is_dir() and not path.is_symlink() and release_pattern.fullmatch(path.name))
allowed={path.resolve() for package in packages for name in relative
         if (path:=package/name).is_file() and not path.is_symlink()}
targets=[]
for entry in pathlib.Path('/proc').iterdir():
    if not entry.name.isdigit() or int(entry.name)==os.getpid(): continue
    try:
        executable=(entry/'exe').resolve(); start=(entry/'stat').read_text().split()[21]
    except (OSError,ValueError): continue
    if executable in allowed: targets.append((int(entry.name),start,executable))
def alive(target):
    pid,start,_=target
    try: return (pathlib.Path('/proc')/str(pid)/'stat').read_text().split()[21]==start
    except (OSError,IndexError): return False
if targets:
    print('Stopping stale Project Prime processes: '+', '.join(f'{pid}:{path.name}' for pid,_,path in targets))
for target in targets:
    try: os.kill(target[0],signal.SIGTERM)
    except ProcessLookupError: pass
deadline=time.monotonic()+15
while any(alive(target) for target in targets) and time.monotonic()<deadline: time.sleep(.2)
for target in targets:
    if alive(target):
        try: os.kill(target[0],signal.SIGKILL)
        except ProcessLookupError: pass
deadline=time.monotonic()+5
while any(alive(target) for target in targets) and time.monotonic()<deadline: time.sleep(.1)
survivors=[str(target[0]) for target in targets if alive(target)]
if survivors: raise SystemExit('stale Project Prime processes did not stop: '+', '.join(survivors))
PY

sudo -n ln -sfn "releases/$release_id" "$root/.current.$release_id"
sudo -n mv -Tf "$root/.current.$release_id" "$current"
sudo -n install -m 644 "$unit_stage" /etc/systemd/system/projectprime-stack.service
sudo -n systemctl daemon-reload
sudo -n systemctl enable projectprime-stack
sudo -n systemctl start projectprime-stack
echo "Waiting up to ${health_timeout}s for Backend and Node health..."
healthy=0; health_deadline=$((SECONDS + health_timeout))
while [ "$SECONDS" -lt "$health_deadline" ]; do
  if curl -fsSk --connect-timeout 2 --max-time 4 "$backend_health" >/dev/null 2>&1 \
      && curl -fsSk --connect-timeout 2 --max-time 4 "$node_health" >/dev/null 2>&1; then healthy=1; break; fi
  sleep 1
done
[ "$healthy" -eq 1 ] || { echo "Backend or Node health check failed" >&2; false; }
main_pid=$(sudo -n systemctl show -p MainPID --value projectprime-stack)
python3 - "$main_pid" "$release" <<'PY'
import os,pathlib,sys
main_pid=int(sys.argv[1]); release=pathlib.Path(sys.argv[2]).resolve(); deploy=release.parent.parent
current=deploy/'current'; proc=pathlib.Path('/proc'); seen={main_pid}; changed=True
while changed:
    changed=False
    for entry in proc.iterdir():
        if not entry.name.isdigit() or int(entry.name) in seen: continue
        try: ppid=int((entry/'stat').read_text().split()[3])
        except (OSError,ValueError): continue
        if ppid in seen: seen.add(int(entry.name)); changed=True
main=proc/str(main_pid)
argv=[x.decode() for x in (main/'cmdline').read_bytes().split(b'\0') if x]
expected_argv=['bash',str(current/'start-stack-dev.sh'),'--with-backend','--package-dir',str(current),
               '--content-dir',str(deploy/'AMHE1'),'--state-dir',str(deploy/'state')]
if not current.is_symlink() or current.resolve()!=release or argv!=expected_argv or (main/'cwd').resolve()!=release:
    raise SystemExit('systemd MainPID is not selected release supervisor')
required={release/'ProjectPrimeServer',release/'backend/ProjectPrime.Backend',release/'worker/ProjectPrime.Server.Worker'}; found=set()
for pid in seen-{main_pid}:
    try: exe=(proc/str(pid)/'exe').resolve()
    except OSError: continue
    if exe in required: found.add(exe)
if found!=required: raise SystemExit('systemd stack descendants do not contain the selected Backend, Node, and Worker')
PY
rm -f "$unit_stage"
python3 - "$releases" "$release_id" "$prior_target" "$keep" <<'PY'
import pathlib,re,shutil,sys
root=pathlib.Path(sys.argv[1]); current=sys.argv[2]; previous=pathlib.PurePosixPath(sys.argv[3]).name; keep=max(2,int(sys.argv[4]))
pattern=re.compile(r'^\d{8}T\d{6}Z-linux-(?:x64|arm64)-\d+$'); protected={current,previous,'app','state','AMHE1'}
items=[p for p in root.iterdir() if p.is_dir() and not p.is_symlink() and pattern.fullmatch(p.name) and p.name not in protected and (p/'.deploy-manifest.sha256').is_file()]
items.sort(key=lambda p:(p.stat().st_mtime,p.name),reverse=True)
for path in items[max(0,keep-len({x for x in protected if pattern.fullmatch(x)})):]:
    if path.parent==root and path.name not in protected: shutil.rmtree(path)
PY
rm -rf "$rollback"; rmdir "$lock"; downtime=0; trap - ERR EXIT
REMOTE_ACTIVATE
then
  if remote test ! -d "$REMOTE_LOCK" 2>/dev/null; then LOCK_HELD=0; fi
  echo "Whole-stack activation failed; inspect retained rollback/lock evidence if automatic recovery also failed." >&2
  exit 1
fi
LOCK_HELD=0; REMOTE_UNIT_CREATED=0
echo "Deployment complete: $REMOTE_RELEASE"
echo "projectprime-stack now owns Backend, Node, and managed Workers; app, state, and AMHE1 were preserved."
