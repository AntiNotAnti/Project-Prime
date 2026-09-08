#!/usr/bin/env bash
set -euo pipefail
bench_root=$(cd "$(dirname "$0")/../.." && pwd)
bench_tmp=$(mktemp -d /tmp/codex-collision-bench.XXXXXX)
bench_revision=${COLLISION_BASELINE_REVISION:-HEAD}
python3 - "$bench_root" "$bench_tmp" "$bench_revision" <<'PY'
import hashlib, json, pathlib, subprocess, sys
root, output, revision = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
path = 'src/Game/Content/Formats/CollisionDetection.cs'
commit = subprocess.check_output(['git','-C',str(root),'rev-parse',revision],text=True).strip()
source = subprocess.check_output(['git','-C',str(root),'show',f'{commit}:{path}'],text=True)
if 'private static readonly HashSet<CollisionData> _seenData' not in source:
    raise SystemExit('Selected baseline is not the historical shared-scratch implementation.')
# Keep the original algorithm byte-for-byte except class identity. Reuse the
# current identical public result/candidate types so values can be compared.
start = source.index('    public static class CollisionDetection')
baseline = source[:source.index('namespace MphRead.Formats')] + 'namespace MphRead.Formats\n{\n' + source[start:]
baseline = baseline.replace('public static class CollisionDetection','public static class BaselineCollisionDetection',1)
(output/'BaselineCollisionDetection.cs').write_text(baseline)
manifest = {'baselineCommit':commit,'baselineSourceSha256':hashlib.sha256(source.encode()).hexdigest(),
            'currentSourceSha256':hashlib.sha256((root/path).read_bytes()).hexdigest(), 'baselineComparison':'serial only; historical static scratch is unsafe concurrently'}
(output/'manifest.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest))
PY
dotnet build "$bench_root/tools/collision-bench/collision-bench.csproj" -c Release -p:BaselineSource="$bench_tmp/BaselineCollisionDetection.cs" -p:NuGetAudit=false -v:q
printf 'Benchmark metadata: %s\n' "$bench_tmp/manifest.json"
if [[ "${1:-}" == "--prepare-only" ]]; then exit 0; fi
if [[ -z "${GAME_DATA_DIRECTORY:-}" ]]; then printf '%s\n' 'Set GAME_DATA_DIRECTORY to extracted AMHE1 content.' >&2; exit 1; fi
dotnet "$bench_root/tools/collision-bench/bin/Release/net10.0/nettest.dll" "${@}"
