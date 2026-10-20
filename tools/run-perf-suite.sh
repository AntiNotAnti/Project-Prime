#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: run-perf-suite.sh <ProjectPrime executable> [output-directory] [room]" >&2
  exit 2
fi

exe="$(realpath "$1")"
out="${2:-perf-results}"
room="${3:-MP3 PROVING GROUND}"
mkdir -p "$out"

stamp="$(date +%Y%m%d-%H%M%S)"
for hz in 60 120 144 240; do
  file="$out/perf-$stamp-${hz}hz.json"
  echo "== Project Prime CPU draw-work check: ${hz} Hz requested workload =="
  "$exe" -perfcheck "$room" -players 8 -seconds 20 -hz "$hz" -output "$file"
  echo "$file"
done

echo
echo "60 Hz, 120 Hz, 144 Hz, and 240 Hz CPU draw-work reports written to $out"
echo "Compare matching reports with:"
echo "  python3 tools/compare-perf.py BASELINE.json CURRENT.json"
