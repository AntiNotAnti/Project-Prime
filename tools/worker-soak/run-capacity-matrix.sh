#!/usr/bin/env bash
set -u -o pipefail

# Sequential capacity evidence runner. Every row is an independent invocation
# against caller-supplied frozen Release binaries; no density is inferred from
# a rolling p99 or from a previous row.

usage() {
    cat >&2 <<'EOF'
usage: run-capacity-matrix.sh --worker-assembly PATH --soak-assembly PATH --data-dir PATH --output DIR [--seconds N]

The matrix is fixed at matches-per-Worker / Workers / lanes:
  2/1/1, 4/1/1, 4/1/2, 8/1/2, 8/1/4, 16/1/4,
  plus explicit two-Worker scaling row 4/2/2.
Each row is run once for each fixed roster variant (players/bots/observers):
  1/2/1, 2/1/2, 4/0/0.
EOF
    exit 64
}

worker_assembly=""
soak_assembly=""
data_dir=""
output=""
seconds=180
while (($# > 0)); do
    case "$1" in
        --worker-assembly) (($# >= 2)) || usage; worker_assembly=$2; shift 2 ;;
        --soak-assembly) (($# >= 2)) || usage; soak_assembly=$2; shift 2 ;;
        --data-dir) (($# >= 2)) || usage; data_dir=$2; shift 2 ;;
        --output) (($# >= 2)) || usage; output=$2; shift 2 ;;
        --seconds) (($# >= 2)) || usage; seconds=$2; shift 2 ;;
        *) usage ;;
    esac
done

[[ -f "$worker_assembly" && -f "$soak_assembly" && -d "$data_dir" && -n "$output" ]] || usage
[[ "$seconds" =~ ^[1-9][0-9]*$ ]] || { echo "--seconds must be a positive integer" >&2; exit 64; }
mkdir -p "$output"

hash_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{print $1}'
    else
        sha256sum "$1" | awk '{print $1}'
    fi
}

worker_hash=$(hash_file "$worker_assembly")
soak_hash=$(hash_file "$soak_assembly")
host=$(hostname -s 2>/dev/null || hostname)
dotnet_version=$(dotnet --version 2>/dev/null || echo unknown)
manifest="$output/matrix.jsonl"
printf '%s\n' '{"kind":"matrix","format":1}' > "$manifest"
python3 - "$manifest" "$host" "$dotnet_version" "$worker_assembly" "$worker_hash" "$soak_assembly" "$soak_hash" "$data_dir" "$seconds" <<'PY'
import json
import hashlib
import os
import platform
import subprocess
import sys
from pathlib import Path

(manifest, host, dotnet_version, worker, worker_hash, soak, soak_hash,
 data_dir, seconds) = sys.argv[1:]

def command(*args):
    try:
        result = subprocess.run(args, check=False, capture_output=True, text=True, timeout=2)
    except (OSError, subprocess.SubprocessError):
        return None
    value = result.stdout.strip()
    return value if result.returncode == 0 and value else None

def dependency_manifest(root):
    root = Path(root).resolve()
    entries = []
    for current, directories, files in os.walk(root, followlinks=False):
        current_path = Path(current)
        directories[:] = sorted(name for name in directories
                                 if not (current_path / name).is_symlink())
        for name in sorted(files):
            path = current_path / name
            if path.is_symlink() or not path.is_file():
                continue
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            entries.append((path.relative_to(root).as_posix(), path.stat().st_size,
                            digest.hexdigest()))
    manifest = "".join(f"{name}\0{size}\0{digest}\n" for name, size, digest in entries).encode()
    return {"root": str(root), "fileCount": len(entries),
            "sha256": hashlib.sha256(manifest).hexdigest()}

system = platform.system()
os_name = platform.platform(aliased=True) or system or "unknown"
os_build = None
if system == "Darwin":
    product = command("sw_vers", "-productName")
    version = command("sw_vers", "-productVersion")
    os_name = " ".join(value for value in (product, version) if value) or os_name
    os_build = command("sw_vers", "-buildVersion")
elif system == "Linux":
    release = Path("/etc/os-release")
    if release.is_file():
        fields = {}
        for line in release.read_text(encoding="utf-8", errors="replace").splitlines():
            key, separator, value = line.partition("=")
            if separator:
                fields[key] = value.strip().strip('"')
        os_name = fields.get("PRETTY_NAME") or os_name
    os_build = command("uname", "-r")
else:
    os_build = platform.release() or None

cpu_model = platform.processor() or None
if system == "Darwin":
    cpu_model = command("sysctl", "-n", "machdep.cpu.brand_string") or cpu_model
elif not cpu_model and Path("/proc/cpuinfo").is_file():
    for line in Path("/proc/cpuinfo").read_text(encoding="utf-8", errors="replace").splitlines():
        if line.lower().startswith("model name") and ":" in line:
            cpu_model = line.split(":", 1)[1].strip()
            break
logical_cpu_count = os.cpu_count()
memory_bytes = None
if system == "Darwin":
    try:
        memory_bytes = int(command("sysctl", "-n", "hw.memsize") or "")
    except ValueError:
        memory_bytes = None
elif Path("/proc/meminfo").is_file():
    for line in Path("/proc/meminfo").read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("MemTotal:"):
            try:
                memory_bytes = int(line.split()[1]) * 1024
            except (IndexError, ValueError):
                pass
            break
Path(manifest).open("a", encoding="utf-8").write(json.dumps({
    "kind": "provenance", "host": host, "dotnet": dotnet_version,
    "os": os_name or "unknown", "osBuild": os_build or "unknown",
    "architecture": platform.machine() or "unknown", "cpuModel": cpu_model or "unknown",
    "logicalCpuCount": logical_cpu_count, "memoryBytes": memory_bytes,
    "workerAssembly": str(Path(worker).resolve()), "workerSha256": worker_hash,
    "soakAssembly": str(Path(soak).resolve()), "soakSha256": soak_hash,
    "workerRuntimeManifest": dependency_manifest(Path(worker).parent),
    "soakRuntimeManifest": dependency_manifest(Path(soak).parent),
    "dataDirectory": str(Path(data_dir).resolve()),
    "dataDirectoryManifest": dependency_manifest(data_dir), "seconds": int(seconds),
}, sort_keys=True) + "\n")
PY

# matches-per-Worker / Workers / lanes. Total matches is derived per row.
cases=(
    "2:1:1:matrix" "4:1:1:matrix" "4:1:2:matrix" "8:1:2:matrix"
    "8:1:4:matrix" "16:1:4:matrix" "4:2:2:two-worker-scaling"
)
rosters=("1:2:1" "2:1:2" "4:0:0")
overall=0
case_index=0

for case in "${cases[@]}"; do
    IFS=: read -r matches workers lanes family <<< "$case"
    [[ "$matches" =~ ^[1-9][0-9]*$ && "$workers" =~ ^[1-9][0-9]*$ && "$lanes" =~ ^[1-9][0-9]*$ ]] \
        || { echo "invalid matrix row: $case" >&2; exit 64; }
    total=$((matches * workers))
    for roster in "${rosters[@]}"; do
        IFS=: read -r players bots observers <<< "$roster"
        run_id=$(printf '%03d-%s-%s' "$case_index" "$family" "$roster" | tr ':' '-')
        run_dir="$output/$run_id"
        mkdir -p "$run_dir"
        args=(
            "$soak_assembly" --worker-assembly "$worker_assembly" --data-dir "$data_dir"
            --output "$run_dir" --seconds "$seconds" --matches "$matches" --workers "$workers"
            --lanes "$lanes" --round-seconds 30 --crash-seconds 0 --outages false
            --reconnects false --rematches false --players "$players" --bots "$bots"
            --observers "$observers" --require-no-failures true --require-drain true
            --require-crash-recovery false --require-reconnect-recovery false
            --require-outage-recovery false --require-rematch-recovery false
        )
        printf 'capacity row %s: matches-per-worker=%s total=%s workers=%s lanes=%s roster=%s\n' \
            "$run_id" "$matches" "$total" "$workers" "$lanes" "$roster"
        set +e
        dotnet "${args[@]}" >"$run_dir/stdout.log" 2>"$run_dir/stderr.log"
        status=$?
        set -e
        python3 - "$manifest" "$run_id" "$family" "$total" "$workers" "$matches" "$lanes" "$players" "$bots" "$observers" "$status" "${args[@]}" <<'PY'
import json
import sys
from pathlib import Path

(manifest, run_id, family, total, workers, matches, lanes, players, bots,
 observers, status, *args) = sys.argv[1:]
Path(manifest).open("a", encoding="utf-8").write(json.dumps({
    "kind": "run", "id": run_id, "family": family,
    "totalMatches": int(total), "workers": int(workers),
    "matchesPerWorker": int(matches), "lanes": int(lanes),
    "roster": {"players": int(players), "bots": int(bots), "observers": int(observers)},
    "args": args, "exitStatus": int(status),
}, sort_keys=True) + "\n")
PY
        (( status == 0 )) || overall=1
        ((case_index += 1))
    done
done

exit "$overall"
