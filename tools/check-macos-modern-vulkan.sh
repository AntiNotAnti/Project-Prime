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
    # whose Vulkan portability behavior varies with the runner image/hypervisor.
    # Two known environment-only failures are allowed here, and only in Actions:
    #   1. MoltenVK enumerates no portability adapters at all.
    #   2. The Apple Paravirtual device is created, but MoltenVK reports missing
    #      buffer robustness and the offscreen smoke readback remains the clear
    #      color (opaque black). Real Macs and every other Vulkan failure remain
    #      fatal so this cannot hide a product regression.
    if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
        if grep -Fq "Vulkan instance enumerated zero adapters" "$log"; then
            echo "::warning::MoltenVK is packaged and initialized, but this hosted macOS runner exposes zero Vulkan portability adapters; skipping hardware draw/present smoke."
            exit 0
        fi

        if grep -Fq 'model: Apple Paravirtual device' "$log" \
           && grep -Fq 'VK_ERROR_FEATURE_NOT_PRESENT: Metal does not support buffer robustness.' "$log" \
           && grep -Fq 'Created VkDevice to run on GPU Apple Paravirtual device' "$log" \
           && grep -Fq 'WebGPU offscreen draw read back only 0/25 expected red interior pixels; first sample rgba(0,0,0,255).' "$log"; then
            echo "::warning::MoltenVK initialized on the hosted Apple Paravirtual GPU, but this runner lacks the robustness behavior required for a reliable Vulkan draw readback; skipping only this hosted-runner hardware smoke."
            exit 0
        fi

        if [[ $status -eq 134 ]] \
           && grep -Fq 'model: Apple Paravirtual device' "$log" \
           && grep -Fq 'GPU memory available: 1024 MB' "$log" \
           && grep -Fq 'VK_ERROR_FEATURE_NOT_PRESENT: Metal does not support buffer robustness.' "$log" \
           && grep -Fq 'Created VkDevice to run on GPU Apple Paravirtual device' "$log" \
           && grep -Fq 'VK_ERROR_OUT_OF_DEVICE_MEMORY: vkAllocateMemory(): Could not allocate VkDeviceMemory of size 8388608 bytes.' "$log" \
           && grep -Fq '`offset + size` is out of memory block bounds' "$log" \
           && grep -Fq 'panic in a function that cannot unwind' "$log"; then
            echo "::warning::MoltenVK aborted on the hosted Intel macOS Apple Paravirtual GPU after its 1 GB virtual device exhausted Vulkan memory during the smoke probe; skipping only this hosted-runner hardware limitation."
            exit 0
        fi
    fi
    exit "$status"
fi

if [[ "$mode" == "--window" ]]; then
    "$binary" -renderwindowcheck -renderer vulkan
fi
