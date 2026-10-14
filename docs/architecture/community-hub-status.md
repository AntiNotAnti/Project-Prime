# Community Hub 3.0 | Integration status

Reviewed integration base: `AntiNotAnti/Project-Prime`, main commit `7a51490e9242f91607eeb40cb78d8bc320fb0fdd`, including PR #537 (October 13, 2026 US Central).

| Slice | Status | Verified work / outstanding gate |
|---|---|---|
| PR-00 #533 | **Merged** | Package identity golden fixtures, source ownership and baseline instrumentation. |
| PR-01 #534 | **Merged** | UI-neutral Community service facades and legacy API compatibility. |
| PR-02 #535 | **Merged** | Independent Game Community tab and exact package install. |
| PR-03 #536 | **Merged** | Durable upload/download jobs, verified resume and UI controls. |
| PR-04 #537 | **Merged** at `7a51490e` | Leased immutable archive CAS, quota LRU and cross-process download locking. |
| PR-05 #538 | **Draft; implementation committed; exact-head CI pending** | Owned private downloads and CAS archives skip redundant snapshots, bounded atomic archive/runtime file writes, verified warm archive/output reuse, rollback on runtime publication error, cross-platform A/B microbenchmark. |
| PR-06 onward | **Not started** | Streaming package builder, service load isolation, generic typed v3 assets and optional CDN. |

## PR-05 CI acceptance

- Existing exact `MapId`, content SHA-256, package SHA-256 and 60 Hz network / Ready barriers must not change.
- Mutating/deleting a caller-owned source must not alter a frozen prepared install. Preparation/Commit only publish after current scene/prewarm fences release.
- Existing archive rollback must succeed even when a later runtime publication fails. No `.staging` or `.backup` files remain on success or handled failure.
- Historical replay download, host rotation, dedicated-server and 1v1/8-player map preparation need their existing acceptance gates.
- Compare 32 MiB local legacy-vs-streaming microbench per platform. Measured time and managed allocations are **copy-stage only** and cannot establish end-to-end lobby Ready improvements.
- Android real-device low-storage and runtime smoke is separate from an emulator/compile-only success.

The earlier platform goldens differ across Windows and Linux/macOS for the same authoring source. Preserve both exact package byte identities; never rewrite old archives. Do not label any PR acceptance as PASS until its exact head and named check actually passed. No future-phase PR should be merged solely to bypass a failing predecessor.
