#!/usr/bin/env bash
set -euo pipefail
[[ $# == 1 || $# == 2 ]] || { echo 'usage: sign-macos.sh <publish-directory> [ProjectPrime|ProjectPrimeStudio]' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: signing requires macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
root=$(cd "$1" && pwd)
executable=${2:-ProjectPrime}
case "$executable" in ProjectPrime|ProjectPrimeStudio) ;; *) echo 'error: unknown managed executable' >&2; exit 1 ;; esac
[[ -x "$root/$executable" ]] || { echo "error: no $executable executable in $root" >&2; exit 1; }

# Inspect every file, including extensionless framework binaries and helpers.
# Materialize find's output so traversal errors cannot disappear in a subshell.
list=$(mktemp)
trap 'rm -f "$list"' EXIT
find "$root" -type f -print0 > "$list"
while IFS= read -r -d '' component; do
    [[ "$component" != "$root/ProjectPrime" && "$component" != "$root/ProjectPrimeStudio" ]] || continue
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
for managed in ProjectPrime ProjectPrimeStudio; do
    [[ -f "$root/$managed" ]] || continue
    if [[ "$managed" == ProjectPrime ]]; then
        entitlements="$repo/src/MphRead/Platforms/macOS/ProjectPrime.entitlements"
    else
        entitlements="$repo/src/ProjectPrime.Studio/Platforms/macOS/ProjectPrimeStudio.entitlements"
    fi
    plutil -lint "$entitlements"
    codesign --force --sign - --entitlements "$entitlements" "$root/$managed"
    codesign --verify --strict --verbose=4 "$root/$managed"
done
