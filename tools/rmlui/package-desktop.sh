#!/usr/bin/env bash
set -euo pipefail

# Validate and complete a PUBLISHED opt-in RmlUi desktop directory.
root=$(cd "$(dirname "$0")/../.." && pwd)
if [[ $# -ne 2 ]]; then
    echo "usage: tools/rmlui/package-desktop.sh <linux-x64|win-x64|osx-x64|osx-arm64> <publish-directory>" >&2
    exit 2
fi
rid=$1
output=$2
case "$rid" in
    linux-x64) library=libProjectPrime.RmlUi.Native.so ;;
    win-x64) library=ProjectPrime.RmlUi.Native.dll ;;
    osx-x64|osx-arm64) library=libProjectPrime.RmlUi.Native.dylib ;;
    *) echo "error: unsupported RmlUi GL2 desktop target: $rid" >&2; exit 2 ;;
esac
[[ -d "$output" ]] || { echo "error: missing published directory: $output" >&2; exit 2; }
output=$(cd "$output" && pwd)
native="$root/artifacts/rmlui-native/$rid"
[[ -f "$native/$library" && -f "$native/PRIME-RMLUI.json" ]] || {
    echo "error: missing pinned native bridge, build $rid gl2 first" >&2
    exit 1
}
# Match the current source fingerprint, signed manifest values, SHA256, ABI,
# native exports, pinned dependency revisions, and license contents.
python3 "$root/tools/rmlui/verify-runtime.py" "$native/$library" "$rid"
python3 - "$native/PRIME-RMLUI.json" <<'PY'
import json,sys
with open(sys.argv[1],encoding="utf-8") as stream:
    metadata=json.load(stream)
if metadata.get("renderer")!="gl2":
    raise SystemExit("error: desktop RmlUi package requires GL2 OpenGL bridge")
PY
cp "$native/$library" "$output/$library"
cp "$native/PRIME-RMLUI.json" "$output/PRIME-RMLUI.json"
mkdir -p "$output/licenses"
for license in RmlUi-LICENSE.txt FreeType-LICENSE.txt FreeType-FTL.txt FreeType-GPLv2.txt; do
    cp "$native/licenses/$license" "$output/licenses/$license"
done
python3 "$root/tools/rmlui/verify-runtime.py" --package "$output" "$rid"
echo "Verified opt-in RmlUi desktop package at $output ($rid)"
