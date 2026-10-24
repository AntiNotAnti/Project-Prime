# Replay camera editing implementation evidence — 2026-10-22

This covers RPL-05/06/07; the opt-in channel extension is specified in the linked channel contract. Source-linked checks use synthetic worlds/bytes. Windows native acceptance also uses real NVIDIA OpenGL and extracted assets with a generated recording. These checks do not establish representative pointer latency, captured-source presentation parity, or other-platform acceptance.

## Editing and recovery

Advanced tools retain a source-bound document with native camera bits. Pointer updates build a memory-only draft and overlay its evaluated camera onto the frozen replay scene. They perform no seek, simulation, file hashing, sidecar write, or confirmation modal. A valid release commits once in memory. Timeline, curves, transforms, and direct saved-key choices share that document. Backends without the memory contract retain their existing confirmation behavior.

The editor retains 100 undo transactions and 100 redo transactions, a dirty marker, explicit Save, and explicit Discard. Close checks dirty state before Studio teardown; failed close resets the closing flag. Explicit Refresh rebinds retained keys/history after a completed seek or motion change only after checking the original source content hash and camera sidecar revision. Save uses the reviewed atomic publication path. Conflicts retain the document; Discard remains available even for a retired source.

Basic camera reload cannot overwrite a dirty advanced document. Both basic native persistence and the shared legacy keyboard/Android key-saving path reject dirty advanced state, so unrelated key commands cannot accidentally publish it. Explicit Refresh of a clean document can adopt a newly saved camera baseline after strict source/owner validation; changed saved revision/keys reset Undo/Redo. Dirty refresh retains its original baseline and history, and rejects a backend that unexpectedly changes that baseline. Discard permits a verified fresh capture, including retry after a transient picture fence.

Committed edits enqueue a separate `.camera.draft` recovery journal. Preview never queues a journal. A bounded latest-only background writer writes, flushes and atomically publishes checksummed authoring state. Recovery requires matching logical replay, complete source content hash, clip range and original camera sidecar revision. Invalid or malformed recovery is ignored. Save/Discard generation-fence pending writes; in-process retirement tombstones prevent immediate reopen from recovering a draft before asynchronous deletion completes. Recovery failures are visible and explicit Save remains available. Recovery contains the last completed background journal; an immediate crash can precede the latest journal write.

## Versioning and scaling

All-inherit sidecars use v6: existing logical source/hash/range binding, an optional crop window, 32-bit bounded authored key count, complete native tangent/lens records, and SHA-256. Independent channel metadata opts into v7/state v3 with six additional bytes per key; inherited tracks retain v6/state v2 bytes. Authored keys are bounded at 1024. Versions 1–5 retain their original 64-key reader bounds and identity rules; v6/state v2 also remain readable. Portable authoring state uses a 32-bit count and a size derived from the key record; state v1 remains readable. Import/export, project/bundle/detached witnesses, and basic/advanced editor bounds accept the new envelope.

Cropping retains the source evaluator/window rather than fitting new controls. Timeline handles are virtualized into 64 visible frame bins; the focused exact key wins within its bin and zoom resolves dense bins. Curves show a 64-key window around focus; previous/next/nearest navigation reaches later keys. Input tickets continue to bind each displayed exact frame.

## Evaluator and measured comparison

Interval location uses binary search. Constant-speed Spline and Bezier use a bounded lazy per-segment arc-length cache invalidated on edits/restoration. Spline retains 32 subdivisions and Bezier 64, preserving historical evaluation. Orientation, lens/roll, easing, Hold cuts, look-at and fractional double time keep their existing semantics. Existing Bezier scalar handles remain independently editable.

`tools/replay-camera-benchmark/compare.ps1` compiles exact `HEAD` baseline camera sources in a temporary project and compares them with the working tree. Recorded Windows/SDK 10.0.401 run: identical 64-key curved tracks, constant speed, 100,000 warm samples, 400 batches of 128 fractional samples. Both processes use `DOTNET_TieredCompilation=0` to compare optimized JIT phases. Values are nanoseconds per sample derived from batch timings.

| Track | Baseline p50 / p95 / p99 | Current p50 / p95 / p99 |
|---|---:|---:|
| Spline | 435.16 / 601.56 / 5662.50 | 91.41 / 99.22 / 368.75 |
| Bezier | 1193.75 / 1256.25 / 3735.16 | 99.22 / 101.56 / 133.59 |

Both report zero measured allocations and matching sample checksums: Spline `215790.27826213837`, Bezier `266069.0905032158`. Concurrent host builds add timing noise; these are synthetic CPU measurements, not reference-device promises. Short default-tiered runs initially compared different asynchronous JIT promotion phases; the retained benchmark documents the controlled JIT policy rather than advertising those startup results as steady state.

## Validation and outstanding evidence

Pinned SDK 10.0.401:

- `tools/rmlui-camera-tools-check`: 412 controller checks (100 undo/100 redo equivalence, preview isolation, dirty seek rebind, clean baseline adoption, failed-save recovery, independent channels) and 754 presenter/action/markup checks, including full parity of all 317 managed/native replay actions with the original indices retained and exact memory button ID/action mappings.
- `tools/rmlui-timeline-check/camera-owner`: 1187 linked production checks with synthetic replay bytes: sidecar v1–v5 migration; portable state v1 including crop windows; 1024-key v6/state-v2 round trips; crop equality; preview source/sidecar/clock invariance; source ABA/conflicts; dirty seek rebind; restart recovery; malformed recovery; immediate retirement tombstone; 100 queue/retire loops; basic and legacy dirty persistence/reload guards.
- `tools/rmlui-camera-tools-check/owner`: 40 native source/matrix/identity/publication/channel/refresh checks. Previously missing state/window links and active-player fixture types were repaired.
- `tools/rmlui-camera-check`: 37 camera ownership/pose/seek/retry checks and 33 matrix decode/cache checks.

Before schema edits the pinned SDK was absent (only 10.0.400 installed); that baseline limitation was recorded. The orchestrator subsequently installed 10.0.401. The later controlled evaluator comparison compiles exact pre-change HEAD sources separately.

Windows native acceptance passes all 16 independent channel transactions, exact selected-key metadata, Undo/Redo, draft recovery, Save/reload and export capture. Actual graph drag/cancel/commit and unchanged canonical-world checks pass on the generated recording. All six channel controls pass native visibility, minimum size and nonoverlap checks, and their rendered screenshot was inspected. Preview and export camera/view/projection equality passes at all eight export rates. See [the complete local acceptance scope](../performance/replay-studio-fidelity-status.md).

Outstanding: measured drag/cancel/release latency and crash/kill-process soak; gameplay/checkpoint hash invariance on representative recordings; macOS/Linux GPU and Android runtime validation. Independent orientation/lens modes and easing, time-aware safe SQUAD/spherical-cubic orientation, and monotone FOV/roll are implemented as opt-in v7/state-v3 metadata. See [the channel contract and math evidence](../performance/replay-camera-channels.md) for its 78,279 checks, bounds, continuity guarantees and explicit-ease exceptions. All-inherit paths retain their prior evaluator and v6/state-v2 encoding.
