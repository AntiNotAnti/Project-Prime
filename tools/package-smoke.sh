#!/usr/bin/env bash
# Extract a server package into a new temporary directory before running the
# real WSS -> lobby -> Worker -> routed UDP admission smoke. The caller supplies
# private AMHE1 content; no credentials or content are stored in the bundle.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUNDLE=""
CONTENT=""
TIMEOUT="90"
while (($# > 0)); do
  case "$1" in
    --bundle) [[ $# -ge 2 ]] || { echo "--bundle requires a value" >&2; exit 2; }; BUNDLE="$2"; shift 2 ;;
    --content-dir) [[ $# -ge 2 ]] || { echo "--content-dir requires a value" >&2; exit 2; }; CONTENT="$2"; shift 2 ;;
    --timeout) [[ $# -ge 2 ]] || { echo "--timeout requires a value" >&2; exit 2; }; TIMEOUT="$2"; shift 2 ;;
    -h|--help) echo "Usage: tools/package-smoke.sh --bundle DIRECTORY --content-dir DIRECTORY [--timeout SECONDS]"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$BUNDLE" && -d "$BUNDLE" ]] || { echo "--bundle directory is required" >&2; exit 2; }
[[ -n "$CONTENT" && -d "$CONTENT" ]] || { echo "--content-dir directory is required" >&2; exit 2; }

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/fruity-prime-package-smoke.XXXXXX")"
ARCHIVE="$TMP_ROOT/server.tar.gz"
EXTRACTED="$TMP_ROOT/extracted"
# Invoked indirectly by the EXIT trap below; ShellCheck cannot follow that call.
# shellcheck disable=SC2329
cleanup() { rm -rf "$TMP_ROOT"; }
trap cleanup EXIT

mkdir -p "$EXTRACTED"
tar -C "$BUNDLE" -czf "$ARCHIVE" .
tar -xzf "$ARCHIVE" -C "$EXTRACTED"
set +e
dotnet run --project "$ROOT/tools/package-smoke/package-smoke.csproj" -c Release -- \
  --bundle "$EXTRACTED" --content-dir "$CONTENT" --timeout "$TIMEOUT"
status=$?
set -e
exit "$status"
