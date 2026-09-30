#!/usr/bin/env bash
set -euo pipefail

[[ $# -ge 1 && $# -le 2 ]] || {
    echo "usage: check-macos-modern-vulkan.sh <ProjectPrime binary> [--window]" >&2
    exit 2
}

binary=$1
mode=${2:-}
[[ "$mode" == "" || "$mode" == "--window" ]] || {
    echo "error: second argument must be --window" >&2
    exit 2
}
[[ -x "$binary" ]] || {
    echo "error: Project Prime binary is not executable: $binary" >&2
    exit 2
}

log=$(mktemp)
trap 'rm -f "$log"' EXIT

set +e
"$binary" -renderbackendprobe -renderer vulkan 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e

if [[ $status -ne 0 ]]; then
    # GitHub's hosted macOS runners expose an Apple Paravirtual Metal device
    # but, depending on the image/hypervisor, no Vulkan portability physical
    # devices to MoltenVK. That is an environment capability gap, not a broken
    # loader/package. Only exempt this exact condition in Actions; every other
    # Vulkan failure stays fatal, and local/real-Mac runs never skip it.
    if [[ "${GITHUB_ACTIONS:-}" == "true" ]] \
       && grep -Fq "Vulkan instance enumerated zero adapters" "$log"; then
        echo "::warning::MoltenVK is packaged and initialized, but this hosted macOS runner exposes zero Vulkan portability adapters; skipping hardware draw/present smoke."
        exit 0
    fi
    exit "$status"
fi

if [[ "$mode" == "--window" ]]; then
    "$binary" -renderwindowcheck -renderer vulkan
fi
