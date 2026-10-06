#!/usr/bin/env bash
set -euo pipefail
[[ $# == 2 || $# == 3 ]] || { echo 'usage: check-macos-build.sh <publish-directory> <rid> [ProjectPrime|ProjectPrimeStudio]' >&2; exit 1; }
[[ $(uname -s) == Darwin ]] || { echo 'error: validation requires macOS' >&2; exit 1; }
root=$(cd "$1" && pwd)
case "$2" in
    osx-arm64) arch=arm64 ;;
    osx-x64) arch=x86_64 ;;
    *) echo "error: unsupported RID: $2" >&2; exit 1 ;;
esac
executable=${3:-ProjectPrime}
case "$executable" in ProjectPrime|ProjectPrimeStudio) ;; *) echo 'error: unknown managed executable' >&2; exit 1 ;; esac
[[ -x "$root/$executable" ]] || { echo "error: $executable is not executable" >&2; exit 1; }
[[ -f "$root/libopenal.1.dylib" ]] || { echo 'error: missing OpenAL' >&2; exit 1; }
[[ -f "$root/libwgpu_native.dylib" ]] || { echo 'error: missing wgpu-native' >&2; exit 1; }
[[ -f "$root/libktx.dylib" ]] || { echo 'error: missing KTX runtime' >&2; exit 1; }
list=$(mktemp)
entitlements=$(mktemp)
trap 'rm -f "$list" "$entitlements"' EXIT
find "$root" -type f -print0 > "$list"
while IFS= read -r -d '' component; do
    description=$(file -b "$component")
    if [[ "$description" != *Mach-O* ]]; then
        if [[ "$component" == *.dylib || "$component" == "$root/$executable" ]]; then
            echo "error: not Mach-O: $component" >&2
            exit 1
        fi
        continue
    fi
    echo "$component: $description"
    # Universal binaries are valid as long as the target slice exists.
    lipo "$component" -verify_arch "$arch"
    otool -L "$component"
    codesign --verify --strict --verbose=4 "$component"
done < "$list"
codesign -d --entitlements :- "$root/$executable" > "$entitlements"
cat "$entitlements"
# Bash 3.2 does not reliably apply errexit to a failed [[ ... ]] command.
# Both an unreadable plist and a false/missing entitlement must fail explicitly.
jit=$(/usr/libexec/PlistBuddy -c 'Print :com.apple.security.cs.allow-jit' "$entitlements") \
    || { echo 'error: missing JIT entitlement' >&2; exit 1; }
[[ "$jit" == true ]] || { echo 'error: JIT entitlement is not true' >&2; exit 1; }
echo "Validated $executable architecture, signatures and JIT entitlement for $2."
