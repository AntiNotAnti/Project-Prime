#!/usr/bin/env bash
# Check each deployment's own exact dependency graph without rewriting locks.
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
for function in career-report career-ticket community-map-ticket hunter-cosmetics hunter-license social presence social-invites; do
  deno check --frozen-lockfile \
    --config "$ROOT/supabase/functions/$function/deno.json" \
    "$ROOT/supabase/functions/$function/index.ts"
done
