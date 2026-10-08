#!/usr/bin/env bash
# Real project boundaries plus controller/native-DOM contracts on one bridge.
set -euo pipefail
[[ $# == 3 ]] || { echo 'usage: full-source-check.sh <native-bridge> <rid> <new-evidence-directory>' >&2; exit 2; }
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
library=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
rid=$2
evidence=$3
mkdir -p "$evidence"
evidence=$(cd "$evidence" && pwd)
cd "$root"
export LD_LIBRARY_PATH="$(dirname "$library")${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export PROJECT_PRIME_USER_DATA="$evidence/isolated-user-data"
export PROJECT_PRIME_UI_PERF="$evidence/disabled-production-services.json"
mkdir -p "$PROJECT_PRIME_USER_DATA"
run() {
    local name=$1
    shift
    if ! "$@" > "$evidence/$name.log" 2>&1; then
        tail -80 "$evidence/$name.log" >&2
        echo "FAIL full-source check: $name" >&2
        return 1
    fi
    echo "PASS full-source check: $name"
}
project() {
    local name=$1
    shift
    local label=$name
    [[ $# == 0 ]] || label="$name-native"
    [[ ${1:-} != --live-queue ]] || label="$name-live-queue"
    run "$label" dotnet run --project "tools/$name" -c Release \
        -p:MphReadRmlUi=true -p:MphReadAvalonia=false -- "$@"
}

run native-client python3 tools/native-client-check/check.py --work "$evidence/native-source"
run transitional-client python3 tools/native-client-check/check.py --legacy --work "$evidence/transitional-source"
run default-client python3 tools/native-client-check/check.py --default-client --work "$evidence/default-source"
run dedicated-server python3 tools/native-client-check/check.py --server --work "$evidence/server-source"
native_assembly="$evidence/native-source/src/MphRead/bin/Release/net10.0"
transitional_assembly="$evidence/transitional-source/src/MphRead/bin/Release/net10.0"
assets="$native_assembly/rmlui"
run native-package dotnet publish "$evidence/native-source/src/MphRead/MphRead.csproj" \
    -c Release -r "$rid" --self-contained false -p:PublishSingleFile=false \
    -p:MphReadRmlUi=true -p:MphReadAvalonia=false -o "$evidence/native-package"
run native-package-audit python3 tools/rmlui/verify-runtime.py --package "$evidence/native-package" "$rid"

project rmlui-runtime-check --native "$library" "$assets"
project rmlui-page-check --native "$library"
run rmlui-home-ux dotnet run --project tools/rmlui-ux-check -c Release \
    -p:PrimeAssemblyDirectory="$native_assembly" -- \
    "$library" "$assets" "$evidence/home-ux"
project launcher-ui-policy-check
project rmlui-settings-check
project rmlui-settings-engine-check
project rmlui-settings-engine-check --native "$assets"
project rmlui-setup-check
project rmlui-setup-engine-check "$assets"
project rmlui-multiplayer-check
project rmlui-multiplayer-check --live-queue
project rmlui-lobby-page-check "$library" --assets "$assets"
project rmlui-lobby-live-check "$library"
project rmlui-studio-check --native "$library"
project social-controller-check
project rmlui-social-page-check --native "$library"
for controller in offline community license news; do
    project "$controller-controller-check"
done
project offline-controller-check --native "$library" "$assets"
project adventure-controller-check --native "$library" "$assets"
project community-controller-check --native "$library" "$assets"
project license-controller-check --native "$library" "$assets"
project theatre-controller-check --native "$library" "$assets"
project news-controller-check --native "$library" "$assets"
project hud-controller-check --native "$library" "$assets"
project in-game-controller-check "$library" --assets "$assets"
project rmlui-accessibility-check "$library" "$assets"
project rmlui-drawlist-cache-check "$library" "$assets"
run compositor-vulkan xvfb-run -a --server-args='-screen 0 1920x1080x24' \
    dotnet run --project tools/rmlui-compositor-check -c Release \
    -p:MphReadRmlUi=true -p:MphReadAvalonia=false -- vulkan --recovery
run compositor-native-lifetime xvfb-run -a --server-args='-screen 0 1920x1080x24' \
    dotnet run --project tools/rmlui-compositor-check -c Release \
    -p:MphReadRmlUi=true -p:MphReadAvalonia=false -- vulkan \
    --lifetime "$assets" "$evidence/native-gpu-lifetime.json"
for mode in native transitional; do
    assembly="$native_assembly"
    [[ "$mode" != transitional ]] || assembly="$transitional_assembly"
    # Reference the exact full ProjectPrime.dll built above, with its actual
    # runtime dependencies. The routing harness does not replace engine types.
    run "shell-routing-$mode" dotnet run --project tools/rmlui-shell-routing-check \
        -c Release -p:PrimeAssemblyDirectory="$assembly" -- "$library" "$assembly/rmlui"
    # The fixture verifies the two diagnostic guard literals in this exact
    # real binary before invoking any account entry point; it uses empty state.
    run "account-diagnostic-$mode" dotnet run --project tools/rmlui-account-diagnostic-check \
        -c Release -p:MphReadRmlUi=true -p:MphReadAvalonia=false -- "$assembly/ProjectPrime.dll"
done
python3 - "$evidence" "$rid" "$library" <<'PY'
import hashlib,json,os,sys
from pathlib import Path
out=Path(sys.argv[1])
result={"revision":os.environ.get("GITHUB_SHA"),"rid":sys.argv[2],
        "native_sha256":hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest(),
        "passed_logs":[p.name for p in sorted(out.glob("*.log"))],
        "physical_reader_and_cartridge_gameplay_certified":False}
(out/"acceptance.json").write_text(json.dumps(result,indent=2)+"\n")
PY
echo 'PASS complete RmlUi full-source boundaries, modules, native DOM and actual shared Shell/lobby gates'
