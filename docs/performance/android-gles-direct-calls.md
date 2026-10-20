# Android GLES direct-call submission experiment

A small subset of the Android GLES hot path now uses cached native C ABI
function pointers when the Android performance profile is enrolled:
`glBindVertexArray`, `glDrawElements`, `glUniform1i`, and `glUniform4f`.
Other GL operations are unchanged. Pointer discovery occurs on the active
GL owner and caches the context generation. A missing symbol falls back to
OpenTK GLES. No function pointer is kept across GL binding-generation changes.

## Rollback and profiling

- `PROJECT_PRIME_ANDROID_GLES_DIRECT=0` selects the previous OpenTK path.
- `PROJECT_PRIME_ANDROID_PERF_OPT_IN=0` selects the full Android baseline.
- Compare identical builds on Adreno and Mali with direct mode off/on.
  Log CPU submission p50/p95/p99, GL call counts, allocations, GPU scene time,
  throughput, shader errors, and visual captures. If native calls are no faster
  or less stable, leave this off; it is not an unconditional optimization.
- Exercise initial launch, scene changes, context reset, overlays, depth/alpha,
  animations, and 1v1/8-player bot matches. Android API 24+ is supported.

This does not change render state, simulation, gameplay, or network protocol.
