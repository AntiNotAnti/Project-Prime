#!/usr/bin/env bash
set -euo pipefail
[[ $# == 1 || $# == 2 ]] || { echo 'usage: sign-macos.sh <publish-directory> [apphost-name]' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: signing requires macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
root=$(cd "$1" && pwd)
apphost=${2:-ProjectPrime}
entitlements="$repo/src/MphRead/Platforms/macOS/ProjectPrime.entitlements"
[[ -x "$root/$apphost" ]] || { echo "error: no executable in $root" >&2; exit 1; }
plutil -lint "$entitlements"

# Inspect every file, including extensionless framework binaries and helpers.
# Materialize find's output so traversal errors cannot disappear in a subshell.
list=$(mktemp)
trap 'rm -f "$list"' EXIT
find "$root" -type f -print0 > "$list"
while IFS= read -r -d '' component; do
    [[ "$component" != "$root/$apphost" ]] || continue
    description=$(file -b "$component")
    if [[ "$description" == *Mach-O* ]]; then
        codesign --force --sign - "$component"
        codesign --verify --strict --verbose=2 "$component"
    fi
done < "$list"
# Seal framework containers inside out after their code has been signed.
find "$root" -depth -type d -name '*.framework' -print0 > "$list"
while IFS= read -r -d '' framework; do
    codesign --force --sign - "$framework"
    codesign --verify --strict --verbose=2 "$framework"
done < "$list"
codesign --force --sign - --entitlements "$entitlements" "$root/$apphost"
codesign --verify --strict --verbose=4 "$root/$apphost"
