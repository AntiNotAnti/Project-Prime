#!/usr/bin/env bash
# Deploy only the map library; never restart a match or directory service.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
usage() {
  cat <<'HELP'
Usage: ./deploy-map-service.sh [--help]
Defaults: ubuntu@51.161.113.128, /home/ubuntu/prime-maps, loopback port 8091.
Environment:
  MPH_SERVER_HOST, MPH_SERVER_USER       VPS SSH destination
  MPH_SERVER_SSH_KEY                     optional private-key file
  MPH_SERVER_PASS                        optional SSH password (not logged)
  MPH_SERVER_SSH_CONTROL                 optional existing SSH control socket
  MPH_MAP_DIR, MPH_MAP_PORT              isolated installation directory/port
  MPH_MAP_DOMAIN                        optional public HTTPS hostname
  MPH_MAP_PROXY                         auto (default), caddy, or nginx
  MPH_MAP_CERTIFICATE, MPH_MAP_CERTIFICATE_KEY
                                        existing remote PEM paths; default LetsEncrypt
  DOTNET                                SDK executable (default dotnet or ~/.dotnet/dotnet)
Requires noninteractive sudo on the VPS. Detects ARM64/x64 automatically.
Without MPH_MAP_DOMAIN, deploys only the private service for proxy setup later.
With a domain, uses the existing Caddy or nginx installation. Caddy manages TLS;
nginx requires an existing certificate.
HELP
}
if [[ "${1:-}" == --help ]]; then usage; exit 0; fi
[[ $# == 0 ]] || { usage >&2; exit 2; }
VPS_HOST="${MPH_SERVER_HOST:-51.161.113.128}"
VPS_USER="${MPH_SERVER_USER:-ubuntu}"
MAP_DIR="${MPH_MAP_DIR:-/home/$VPS_USER/prime-maps}"
MAP_PORT="${MPH_MAP_PORT:-8091}"
MAP_DOMAIN="${MPH_MAP_DOMAIN:-}"
[[ "$VPS_HOST" =~ ^[A-Za-z0-9.-]+$ && "$VPS_HOST" != -* && "$VPS_USER" =~ ^[a-z_][a-z0-9_-]*$ ]] || { echo 'Invalid SSH destination' >&2; exit 2; }
[[ "$MAP_DIR" =~ ^/[A-Za-z0-9_./-]+$ && "$MAP_DIR" != / && "$MAP_DIR" != *'/../'* && "$MAP_DIR" != */.. ]] || { echo 'Invalid map service directory' >&2; exit 2; }
[[ "$MAP_PORT" =~ ^[1-9][0-9]{3,4}$ ]] && (( MAP_PORT <= 65535 )) || { echo 'Use an unprivileged map port (1024–65535)' >&2; exit 2; }
(( MAP_PORT >= 1024 )) || exit 2
[[ -z "$MAP_DOMAIN" || ( "$MAP_DOMAIN" =~ ^[A-Za-z0-9][A-Za-z0-9.-]+\.[A-Za-z]{2,}$ && "$MAP_DOMAIN" != *..* ) ]] || { echo 'Invalid HTTPS hostname' >&2; exit 2; }
CERT="${MPH_MAP_CERTIFICATE:-/etc/letsencrypt/live/$MAP_DOMAIN/fullchain.pem}"
CERT_KEY="${MPH_MAP_CERTIFICATE_KEY:-/etc/letsencrypt/live/$MAP_DOMAIN/privkey.pem}"
for pem in "$CERT" "$CERT_KEY"; do
  [[ "$pem" =~ ^/[A-Za-z0-9_./-]+$ ]] || { echo 'Invalid certificate path' >&2; exit 2; }
done
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/prime-maps-deploy.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
SSH_OPTIONS=(-o ConnectTimeout=15 -o StrictHostKeyChecking=accept-new)
if [[ -n "${MPH_SERVER_SSH_CONTROL:-}" ]]; then SSH_OPTIONS+=(-o "ControlPath=$MPH_SERVER_SSH_CONTROL"); fi
if [[ -n "${MPH_SERVER_SSH_KEY:-}" ]]; then SSH_OPTIONS+=(-i "$MPH_SERVER_SSH_KEY" -o IdentitiesOnly=yes); fi
if [[ -n "${MPH_SERVER_PASS:-}" ]]; then
  cat > "$STAGE/askpass" <<'ASKPASS'
#!/bin/sh
printf '%s\n' "$MPH_SERVER_PASS"
ASKPASS
  chmod 700 "$STAGE/askpass"
  export MPH_SERVER_PASS SSH_ASKPASS="$STAGE/askpass" SSH_ASKPASS_REQUIRE=force DISPLAY="${DISPLAY:-:0}"
  SSH_OPTIONS+=(-o NumberOfPasswordPrompts=1)
else
  SSH_OPTIONS+=(-o BatchMode=yes)
fi
remote() { ssh "${SSH_OPTIONS[@]}" "$VPS_USER@$VPS_HOST" "$@"; }
echo "Checking $VPS_USER@$VPS_HOST"
ARCH="$(remote 'sudo -n true && uname -m')"
case "$ARCH" in aarch64|arm64) RID=linux-arm64;; x86_64|amd64) RID=linux-x64;; *) echo "Unsupported server architecture: $ARCH" >&2; exit 1;; esac
remote 'command -v curl >/dev/null && command -v openssl >/dev/null && command -v flock >/dev/null && command -v tar >/dev/null'
PROXY_KIND=none
if [[ -n "$MAP_DOMAIN" ]]; then
  PROXY_KIND="${MPH_MAP_PROXY:-auto}"
  if [[ "$PROXY_KIND" == auto ]]; then PROXY_KIND="$(remote 'if command -v caddy >/dev/null; then echo caddy; else echo nginx; fi')"; fi
  if [[ "$PROXY_KIND" == caddy ]]; then
    remote 'sudo -n test -f /etc/caddy/Caddyfile && sudo -n caddy validate --config /etc/caddy/Caddyfile'
  elif [[ "$PROXY_KIND" == nginx ]]; then
  remote "sudo -n test -f '$CERT' && sudo -n test -f '$CERT_KEY' && sudo -n nginx -t && test -d /etc/nginx/sites-enabled"
  else echo "Unsupported proxy: $PROXY_KIND" >&2; exit 2; fi
fi
SDK="${DOTNET:-dotnet}"
if ! command -v "$SDK" >/dev/null && [[ -x "$HOME/.dotnet/dotnet" ]]; then SDK="$HOME/.dotnet/dotnet"; fi
mkdir -p "$STAGE/publish"
echo "Building map library for $RID"
"$SDK" publish "$ROOT/src/MphRead/MphRead.csproj" -c Release -r "$RID" \
  -p:MphReadServer=true --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o "$STAGE/publish"
test -s "$STAGE/publish/ProjectPrime"
# Package the executable only: the map library needs no game data or shipped maps.
cp "$STAGE/publish/ProjectPrime" "$STAGE/ProjectPrime"
sed -e "s|__USER__|$VPS_USER|g" -e "s|__DIR__|$MAP_DIR|g" -e "s|__PORT__|$MAP_PORT|g" \
  "$ROOT/tools/systemd/prime-maps.service" > "$STAGE/prime-maps.service"
sed -e "s|__DOMAIN__|$MAP_DOMAIN|g" -e "s|__PORT__|$MAP_PORT|g" \
  -e "s|__CERTIFICATE__|$CERT|g" -e "s|__CERTIFICATE_KEY__|$CERT_KEY|g" \
  "$ROOT/tools/nginx/prime-maps.conf" > "$STAGE/prime-maps.conf"
sed -e "s|__DOMAIN__|$MAP_DOMAIN|g" -e "s|__PORT__|$MAP_PORT|g" \
  "$ROOT/tools/caddy/prime-maps.caddy" > "$STAGE/prime-maps.caddy"
cp "$ROOT/tools/install-map-service.sh" "$STAGE/install-map-service.sh"
tar -czf "$STAGE/map-service.tar.gz" -C "$STAGE" ProjectPrime prime-maps.service prime-maps.conf prime-maps.caddy install-map-service.sh
REMOTE_STAGE="$(remote 'mktemp -d /tmp/prime-maps-deploy.XXXXXX')"
[[ "$REMOTE_STAGE" =~ ^/tmp/prime-maps-deploy\.[A-Za-z0-9]+$ ]] || { echo 'Unexpected staging path' >&2; exit 1; }
scp "${SSH_OPTIONS[@]}" "$STAGE/map-service.tar.gz" "$VPS_USER@$VPS_HOST:$REMOTE_STAGE/package.tar.gz"
remote "tar -xzf '$REMOTE_STAGE/package.tar.gz' -C '$REMOTE_STAGE' && bash '$REMOTE_STAGE/install-map-service.sh' '$MAP_DIR' '$MAP_PORT' '$MAP_DOMAIN' '$PROXY_KIND'"
remote "rm -rf '$REMOTE_STAGE'"
if [[ -n "$MAP_DOMAIN" ]]; then
  if ! curl --fail --silent --show-error --max-time 30 "https://$MAP_DOMAIN/health"; then
    echo "Map service is deployed, but public HTTPS is pending. Check DNS for $MAP_DOMAIN and the proxy certificate." >&2
    exit 2
  fi
  echo
  echo "Map library ready: https://$MAP_DOMAIN/"
else
  echo "Map service healthy on VPS loopback port $MAP_PORT. Configure HTTPS before sharing it publicly."
fi
