#!/usr/bin/env bash
# Build and deploy the dedicated game server. Use deploy-map-service.sh for maps only.
#
# The service is stopped before the binary is replaced: systemd holds the
# executable open while it runs, so overwriting it in place fails.
#
# Credentials come from the environment, not this file:
#   MPH_SERVER_HOST=51.161.113.128 MPH_SERVER_USER=ubuntu ./deploy-server.sh
# With an SSH key installed, no password is needed at all -- which is the
# setup worth moving to.
set -euo pipefail

HOST="${MPH_SERVER_HOST:-51.161.113.128}"
USER="${MPH_SERVER_USER:-ubuntu}"
REMOTE_DIR="${MPH_SERVER_DIR:-/home/$USER/fruityprime-server/current}"
SERVICE="mphread-server"
# The normal public architecture is allocator-only: the master stays resident
# and starts one authoritative child per lobby lifetime. Set
# MPH_DEPLOY_GAME_SERVER=1 only for a permanent official/rated Continuous lane.
MASTER_SERVICE="mphread-master"
DEPLOY_MASTER="${MPH_DEPLOY_MASTER:-1}"
DEPLOY_GAME_SERVER="${MPH_DEPLOY_GAME_SERVER:-0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/MphRead"
STAGE="" # selected after the VPS architecture is detected
# The project used to be MphRead and the Pi has been running a binary of that
# name under systemd since before the rename. Both names appear below: the new
# one is what gets installed, the old one is what has to be cleaned up and what
# the existing units still point at until they are rewritten.
BINARY="ProjectPrime"
OLD_BINARY="MphRead"
CAREER_KEY="${PROJECT_PRIME_CAREER_SERVER_KEY:-${MPH_CAREER_SERVER_KEY:-}}"
if [ -n "$CAREER_KEY" ] && [[ ! "$CAREER_KEY" =~ ^ppsrv_[A-Za-z0-9_-]{32,}$ ]]; then
  echo "PROJECT_PRIME_CAREER_SERVER_KEY has an invalid format" >&2
  exit 1
fi

# SSH agent/key or an existing authenticated control socket. Passwords stay out of argv.
SSH_OPTIONS=(-o StrictHostKeyChecking=accept-new -o ConnectTimeout=15)
if [ -n "${MPH_SERVER_SSH_CONTROL:-}" ]; then SSH_OPTIONS+=(-o "ControlPath=$MPH_SERVER_SSH_CONTROL"); fi
if [ -n "${MPH_SERVER_SSH_KEY:-}" ]; then SSH_OPTIONS+=(-i "$MPH_SERVER_SSH_KEY" -o IdentitiesOnly=yes); fi
ssh_run() {
  if [ -n "${MPH_SERVER_PASS:-}" ]; then
    SSHPASS="$MPH_SERVER_PASS" sshpass -e ssh "${SSH_OPTIONS[@]}" "$USER@$HOST" "$@"
  else
    ssh "${SSH_OPTIONS[@]}" "$USER@$HOST" "$@"
  fi
}
scp_put() {
  if [ -n "${MPH_SERVER_PASS:-}" ]; then
    SSHPASS="$MPH_SERVER_PASS" sshpass -e scp "${SSH_OPTIONS[@]}" "$1" "$USER@$HOST:$2"
  else
    scp "${SSH_OPTIONS[@]}" "$1" "$USER@$HOST:$2"
  fi
}

RID="${MPH_SERVER_RID:-}"
if [ -z "$RID" ]; then
  case "$(ssh_run 'uname -m')" in
    x86_64|amd64) RID=linux-x64;;
    aarch64|arm64) RID=linux-arm64;;
    *) echo 'Unsupported VPS architecture' >&2; exit 1;;
  esac
fi
STAGE="$ROOT/publish/server-$RID"
echo "==> building $RID"
# -p:MphReadServer=true: this box runs the server and the directory and nobody
# plays on it, so the launcher and the UI toolkit behind it are left out.
"${DOTNET:-dotnet}" publish "$PROJECT" -c Release -r "$RID" -p:MphReadServer=true \
  --self-contained true -p:PublishSingleFile=true -o "$STAGE"

test -f "$STAGE/$BINARY" || { echo "build produced no $BINARY" >&2; exit 1; }

# Install a unit the first time, and leave a hand-edited one alone after that:
# an operator who changed the server name or the port on the box should not
# have it overwritten by a deploy.
#
# The rename is the one exception. A unit that still starts the old binary
# would keep starting it after this deploy -- the file would still be there,
# one release behind, refusing every client at Hello -- so a unit whose
# ExecStart names the old binary is rewritten in place. Only that line: an
# edited port or server name is preserved by patching rather than replacing.
install_unit() {
  local name="$1" template="$ROOT/tools/systemd/$1.service"
  if ssh_run "test -f /etc/systemd/system/$name.service"; then
    if ! ssh_run "grep -qxF 'EnvironmentFile=-$REMOTE_DIR/career.env' /etc/systemd/system/$name.service"; then
      echo "==> adding career.env to $name.service"
      ssh_run "sudo sed -i '/^WorkingDirectory=/a EnvironmentFile=-$REMOTE_DIR/career.env' /etc/systemd/system/$name.service && sudo systemctl daemon-reload"
    fi
    if ssh_run "grep -q '$REMOTE_DIR/$OLD_BINARY ' /etc/systemd/system/$name.service"; then
      echo "==> $name.service still starts $OLD_BINARY; pointing it at $BINARY"
      ssh_run "sudo sed -i 's|$REMOTE_DIR/$OLD_BINARY |$REMOTE_DIR/$BINARY |' /etc/systemd/system/$name.service && sudo systemctl daemon-reload"
    fi
    if [ "$name" = "$MASTER_SERVICE" ]; then
      if ! ssh_run "grep -q -- '-public ' /etc/systemd/system/$name.service"; then
        echo "==> adding public hosted-lobby address to $name.service"
        ssh_run "sudo sed -i '/^ExecStart=/ s|$| -public $HOST|' /etc/systemd/system/$name.service && sudo systemctl daemon-reload"
      fi
      if ! ssh_run "grep -q -- '-hostports ' /etc/systemd/system/$name.service"; then
        echo "==> adding hosted-lobby port pool to $name.service"
        ssh_run "sudo sed -i '/^ExecStart=/ s|$| -hostports 27900-27919|' /etc/systemd/system/$name.service && sudo systemctl daemon-reload"
      fi
    fi
    return 0
  fi
  echo "==> installing $name.service"
  sed -e "s|__USER__|$USER|g" -e "s|__DIR__|$REMOTE_DIR|g" \
      -e "s|__PUBLIC__|$HOST|g" "$template" \
    | ssh_run "cat > /tmp/$name.service"
  ssh_run "sudo mv /tmp/$name.service /etc/systemd/system/$name.service && sudo systemctl daemon-reload && sudo systemctl enable $name"
}

echo "==> stopping $SERVICE"
ssh_run "sudo systemctl stop $SERVICE" || true
if [ "$DEPLOY_MASTER" = "1" ]; then
  ssh_run "sudo systemctl stop $MASTER_SERVICE" || true
fi

echo "==> uploading"
scp_put "$STAGE/$BINARY" "$REMOTE_DIR/$BINARY.new"
ssh_run "chmod +x $REMOTE_DIR/$BINARY.new && mv $REMOTE_DIR/$BINARY.new $REMOTE_DIR/$BINARY"

if [ -n "$CAREER_KEY" ]; then
  echo "==> installing Hunter License career reporter credential"
  CAREER_TMP="$(mktemp)"
  trap 'rm -f "$CAREER_TMP"' EXIT
  printf 'PROJECT_PRIME_CAREER_SERVER_KEY=%s\n' "$CAREER_KEY" > "$CAREER_TMP"
  scp_put "$CAREER_TMP" "$REMOTE_DIR/career.env.new"
  ssh_run "chmod 600 $REMOTE_DIR/career.env.new && mv $REMOTE_DIR/career.env.new $REMOTE_DIR/career.env"
  rm -f "$CAREER_TMP"
  trap - EXIT
fi

# The units have to be pointing at the new binary before the old one is taken
# away, or a deploy that stops half way leaves a box with neither.
if [ "$DEPLOY_GAME_SERVER" = "1" ]; then
  install_unit "$SERVICE"
fi
if [ "$DEPLOY_MASTER" = "1" ]; then
  install_unit "$MASTER_SERVICE"
fi

if [ "$BINARY" != "$OLD_BINARY" ]; then
  if ssh_run "test -f $REMOTE_DIR/$OLD_BINARY"; then
    echo "==> removing the old $OLD_BINARY binary"
    ssh_run "rm -f $REMOTE_DIR/$OLD_BINARY"
  fi
fi

if [ "$DEPLOY_GAME_SERVER" = "1" ]; then
  echo "==> starting permanent official game server"
  ssh_run "sudo systemctl enable --now $SERVICE"
else
  echo "==> permanent game server disabled; lobbies are allocated on demand"
  ssh_run "sudo systemctl disable --now $SERVICE" || true
fi
if [ "$DEPLOY_MASTER" = "1" ]; then
  echo "==> starting $MASTER_SERVICE"
  ssh_run "sudo systemctl enable --now $MASTER_SERVICE"
fi
sleep 3
if [ "$DEPLOY_GAME_SERVER" = "1" ]; then
  ssh_run "systemctl is-active $SERVICE && journalctl -u $SERVICE -n 12 --no-pager | tail -10"
  ssh_run "journalctl -u $SERVICE -n 50 --no-pager | grep '\[career\]' | tail -6 || true"
fi
if [ "$DEPLOY_MASTER" = "1" ]; then
  ssh_run "systemctl is-active $MASTER_SERVICE \
    && journalctl -u $MASTER_SERVICE -n 8 --no-pager | tail -7"
fi

echo "==> done"
echo
echo "The browser and Create Lobby flow ask 51.161.113.128:27889 by default."
echo "UDP 27889 plus the hosted range 27900-27919 must reach this machine."
echo "Set MPH_DEPLOY_GAME_SERVER=1 only when you also want the permanent"
echo "Continuous official/rated server on UDP 27888."

# Optional map-library deployment. To avoid game restarts, invoke its script directly.
if [ "${MPH_DEPLOY_MAPS:-0}" = "1" ]; then
  MPH_SERVER_HOST="$HOST" MPH_SERVER_USER="$USER" "$ROOT/deploy-map-service.sh"
fi
