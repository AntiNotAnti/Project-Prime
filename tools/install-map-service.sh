#!/usr/bin/env bash
# Runs on the VPS from an isolated deployment archive. Never touches game units.
set -euo pipefail
MAP_DIR="${1:?installation directory required}"
MAP_PORT="${2:?loopback port required}"
MAP_DOMAIN="${3:-}"
PROXY_KIND="${4:-none}"
[[ "$PROXY_KIND" == none || "$PROXY_KIND" == caddy || "$PROXY_KIND" == nginx ]] || exit 2
STAGE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT=/etc/systemd/system/prime-maps.service
PROXY=/etc/nginx/sites-available/prime-maps.conf
PROXY_LINK=/etc/nginx/sites-enabled/prime-maps.conf
[[ "$MAP_DIR" =~ ^/[A-Za-z0-9_./-]+$ && "$MAP_DIR" != / && "$MAP_DIR" != *'/../'* && "$MAP_DIR" != */.. ]] || exit 2
[[ "$MAP_PORT" =~ ^[1-9][0-9]{3,4}$ ]] && (( MAP_PORT >= 1024 && MAP_PORT <= 65535 )) || exit 2
sudo -n true
mkdir -p "$MAP_DIR/releases" "$MAP_DIR/data/packages"
exec 9>"$MAP_DIR/.deploy.lock"
flock -n 9 || { echo 'Another map deployment is running' >&2; exit 1; }
chmod 750 "$MAP_DIR/data" "$MAP_DIR/data/packages"
RELEASE="$MAP_DIR/releases/$(date -u +%Y%m%dT%H%M%SZ)-$$"
mkdir "$RELEASE"
install -m 755 "$STAGE/ProjectPrime" "$RELEASE/ProjectPrime"
OLD_TARGET="$(readlink "$MAP_DIR/current" || true)"
if [[ -e "$MAP_DIR/current" && ! -L "$MAP_DIR/current" ]]; then echo 'current must be a release symlink' >&2; exit 1; fi
HAD_UNIT=0; HAD_PROXY=0; HAD_PROXY_LINK=0; SWITCHED=0; PROXY_CHANGED=0
WAS_ACTIVE=0; WAS_ENABLED=0
systemctl is-active --quiet prime-maps && WAS_ACTIVE=1
systemctl is-enabled --quiet prime-maps 2>/dev/null && WAS_ENABLED=1
if sudo -n test -f "$UNIT"; then sudo -n cp -p "$UNIT" "$STAGE/unit.previous"; HAD_UNIT=1; fi
if [[ "$PROXY_KIND" == nginx ]]; then
  if sudo -n test -f "$PROXY"; then sudo -n cp -p "$PROXY" "$STAGE/proxy.previous"; HAD_PROXY=1; fi
  if sudo -n test -L "$PROXY_LINK"; then
    [[ "$(readlink "$PROXY_LINK")" == "$PROXY" ]] || { echo 'An unrelated proxy occupies the map site link' >&2; exit 1; }
    HAD_PROXY_LINK=1
  elif sudo -n test -e "$PROXY_LINK"; then echo 'An unrelated proxy occupies the map site path' >&2; exit 1; fi
fi
HAD_CADDY_SITE=0; CADDY_CHANGED=0
if [[ "$PROXY_KIND" == caddy ]]; then
  sudo -n cp -p /etc/caddy/Caddyfile "$STAGE/caddy.previous"
  if sudo -n test -f /etc/caddy/prime-maps.caddy; then
    sudo -n cp -p /etc/caddy/prime-maps.caddy "$STAGE/caddy-site.previous"; HAD_CADDY_SITE=1
  fi
fi
rollback() {
  local result=$?
  if (( result == 0 )); then return; fi
  trap - EXIT
  echo 'Map deployment failed; restoring the previous map service configuration.' >&2
  if (( CADDY_CHANGED )); then
    sudo -n cp -p "$STAGE/caddy.previous" /etc/caddy/Caddyfile
    if (( HAD_CADDY_SITE )); then sudo -n cp -p "$STAGE/caddy-site.previous" /etc/caddy/prime-maps.caddy; else sudo -n rm -f /etc/caddy/prime-maps.caddy; fi
    sudo -n caddy validate --config /etc/caddy/Caddyfile && sudo -n systemctl reload caddy || true
  fi
  if (( PROXY_CHANGED )); then
    if (( HAD_PROXY )); then sudo -n cp -p "$STAGE/proxy.previous" "$PROXY"; else sudo -n rm -f "$PROXY"; fi
    if (( ! HAD_PROXY_LINK )); then sudo -n rm -f "$PROXY_LINK"; fi
    sudo -n nginx -t && sudo -n systemctl reload nginx || true
  fi
  if (( SWITCHED )); then
    sudo -n systemctl stop prime-maps || true
    if (( ! WAS_ENABLED )); then sudo -n systemctl disable prime-maps 2>/dev/null || true; fi
    if [[ -n "$OLD_TARGET" ]]; then
      ln -s "$OLD_TARGET" "$MAP_DIR/current.rollback"
      mv -Tf "$MAP_DIR/current.rollback" "$MAP_DIR/current"
    else rm -f "$MAP_DIR/current"; fi
    if (( HAD_UNIT )); then sudo -n cp -p "$STAGE/unit.previous" "$UNIT"; else sudo -n rm -f "$UNIT"; fi
    sudo -n systemctl daemon-reload
    if (( WAS_ACTIVE )); then sudo -n systemctl restart prime-maps || true; fi
  fi
  exit "$result"
}
trap rollback EXIT
# Generate once on the VPS. Never print, transmit, or replace an existing token.
sudo -n install -d -m 700 /etc/project-prime
sudo -n sh -c 'if [ ! -e /etc/project-prime/maps.env ]; then
  umask 077
  token=$(openssl rand -hex 32) || exit 1
  printf "PROJECT_PRIME_MAP_UPLOAD_TOKEN=%s\n" "$token" > /etc/project-prime/maps.env
fi
if ! grep -q "^PROJECT_PRIME_MAP_STORAGE_GIB=" /etc/project-prime/maps.env; then
  printf "PROJECT_PRIME_MAP_STORAGE_GIB=50\n" >> /etc/project-prime/maps.env
fi
chmod 600 /etc/project-prime/maps.env'
SWITCHED=1
ln -s "$RELEASE" "$MAP_DIR/current.new"
mv -Tf "$MAP_DIR/current.new" "$MAP_DIR/current"
sudo -n install -m 644 "$STAGE/prime-maps.service" "$UNIT"
sudo -n systemctl daemon-reload
sudo -n systemctl enable prime-maps
sudo -n systemctl restart prime-maps
HEALTHY=0
for attempt in $(seq 1 20); do
  if systemctl is-active --quiet prime-maps && curl --fail --silent --max-time 3 "http://127.0.0.1:$MAP_PORT/health" > "$STAGE/health.json" && grep -q '"service"[[:space:]]*:[[:space:]]*"prime-maps"' "$STAGE/health.json"; then HEALTHY=1; break; fi
  sleep 1
done
if (( ! HEALTHY )); then
  sudo -n journalctl -u prime-maps -n 20 --no-pager >&2
  exit 1
fi
# Keep the game and directory processes running while nginx reloads its map site.
if [[ "$PROXY_KIND" == caddy ]]; then
  CADDY_CHANGED=1
  sudo -n install -m 644 "$STAGE/prime-maps.caddy" /etc/caddy/prime-maps.caddy
  if ! sudo -n grep -qxF 'import /etc/caddy/prime-maps.caddy' /etc/caddy/Caddyfile; then
    printf '\nimport /etc/caddy/prime-maps.caddy\n' | sudo -n tee -a /etc/caddy/Caddyfile >/dev/null
  fi
  sudo -n caddy validate --config /etc/caddy/Caddyfile
  sudo -n systemctl reload caddy
  curl --fail --silent --show-error --max-time 15 --resolve "$MAP_DOMAIN:443:127.0.0.1" "https://$MAP_DOMAIN/health" > "$STAGE/https-health.json"
  grep -q '"service"[[:space:]]*:[[:space:]]*"prime-maps"' "$STAGE/https-health.json"
elif [[ "$PROXY_KIND" == nginx ]]; then
  PROXY_CHANGED=1
  sudo -n install -m 644 "$STAGE/prime-maps.conf" "$PROXY"
  sudo -n ln -sfn "$PROXY" "$PROXY_LINK"
  sudo -n nginx -t
  sudo -n systemctl reload nginx
  curl --fail --silent --show-error --max-time 15 --resolve "$MAP_DOMAIN:443:127.0.0.1" "https://$MAP_DOMAIN/health"
  echo
fi
trap - EXIT
printf 'Installed map library release: %s\n' "$RELEASE"
printf 'Upload token retained privately in /etc/project-prime/maps.env\n'
