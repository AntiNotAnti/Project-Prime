# Bounded Replay Studio export admission

## Summary

- Goal: prevent repeated queue/retry requests from retaining unlimited export manifests while one job renders or encodes.
- Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-replay-export-queue`.
- Dependencies: baseline #489. Shader failure ownership #492, SFX lifecycle #494, and Music startup #495 are conceptual dependencies for the later real render/export lifecycle gate, not code prerequisites: this branch compiles independently against main. Include applicable scene-owner seam changes in integration validation.
- Changed boundary: ReplayExportQueue admission/retention/pump, its two UI producers, a pure exporter ownership predicate, an internal device-free scheduling test seam, and CI. No serializers, gameplay, checkpoint graphs, assets, frame sampling, or encoder process implementation change.

## Ownership and rejection contract

Eight manifests may wait; the single active render/encoder (including an in-progress start) is separate. A full queue rejects a new export or retry explicitly without replacing accepted FIFO entries or the last accepted retry target. Queue metadata contains paths/ranges/presentation options, not Scene/native resources. The existing last/failed retry slots retain at most one manifest each; failure history remains five messages, each error excerpt bounded to 180 characters. A stable failure snapshot avoids exposing the mutable history list to readers.

All production admission/clear/retry call sites are synchronous ReplayControlsView UI button handlers; the only other Enqueue call site is the exclusive ReplayExportCheck CLI diagnostic. Desktop embeds headless Avalonia and drains dispatcher work in the render-owned UiSurface. Android has a separate GL consumer thread, so the shared queue does not rely on a single-thread count/enqueue assumption. A short gate protects actual capacity check + enqueue, dequeue, retry, clear, and history metadata. Preflight is advisory; actual admission is atomic for every caller.

UI producers check capacity before CreatePresetManifest/CreateReelManifest, which otherwise create output directories/files. Those synchronous UI handlers cannot interleave another UI admission while preparing the manifest; the render consumer only reduces pending count. They also observe the final boolean admission result. The queue never deletes a rejected manifest's destinations: callers outside those UI paths retain ownership of prepared artifacts and could refer to user-owned/shared files.

Exporter polling, start, status retrieval, and cancellation run outside the queue gate. A start guard prevents recursive/concurrent pump ownership and is released in finally. The activity check before the gate can become stale, so the gate also rechecks a pure current ownership predicate (existing job/encoder fields, without polling). This prevents a caller with an old false snapshot from consuming the next job after another start completed.

## Evidence

Environment: macOS 27.0 / Darwin arm64, .NET SDK 10.0.401 (`/Users/jarrett/.dotnet/dotnet`), Release configuration. No real audio device, GL renderer, or encoder was started by the queue tool.

Before the capacity change, the existing void Enqueue body was factored into a boolean that always returned true, preserving admission semantics. The original pending/retry behavior reported **6 PASS / 8 FAIL**, exit 1: ninth jobs and retries were admitted, 1,000 more requests grew pending ownership, and rejected-job identity/failure feedback requirements failed.

The additional stale-activity witness pauses one caller after its false activity snapshot, completes another caller's start and guard release, then resumes the stale caller. Before the fresh serialized predicate check it reported **24 PASS / 1 FAIL**, exit 1: the stale caller consumed/started the next job.

Candidate queue tool: **26 PASS / 0 FAIL**, exit 0. It covers normal/failed retries, FIFO and manifest/segment identities (including v2 and v3 metadata), one thousand overflow requests, 256 parallel admissions, producer preflight, retained failure history, simulated active cancellation, capacity recovery, prepared destination preservation, WeakReference release of cleared non-retry/active metadata, recursive start prevention, thrown/rejected/cancelled start guard restoration, exporter callbacks outside the queue gate, stale activity rejection, and zero allocations for 1,000 warmed idle pump calls.

The active-job test injects metadata fields only, then calls existing cancellation; no render/encoder starts. It establishes metadata release, not GL/audio/video lifetime coverage. The injected scheduling callbacks establish queue transition behavior, not a native renderer integration result.

## Validation

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/replay-export-queue-check -c Release
/Users/jarrett/.dotnet/dotnet run --project tools/replay-timeline-check -c Release
/Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replayformatcheck
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

| Check | Status | Evidence / limitation |
|---|---|---|
| Original capacity regression | FAIL, expected | 6 PASS / 8 FAIL; unbounded admission retained |
| Stale activity regression | FAIL, expected | 24 PASS / 1 FAIL without serialized fresh ownership check |
| Final queue regressions | PASS | 26 PASS / 0 FAIL; file-only metadata and scheduling tests |
| Replay timeline | PASS | 46 checks, including quiet frontier zero allocation and pooled lifetime |
| Replay format compatibility | PASS | 3,034 checks: legacy/current formats, CRC, order, recovery, extraction, malformed records and storage backpressure |
| Desktop Release compilation | PASS | Final explicit incremental build: 0 warnings / 0 errors; preceding source rebuild emitted 30 warnings in unchanged files |
| Workflow YAML / exact CI step | PASS | Device-free queue check in existing renderer lifecycle policy job |
| Actual rendering/encoding/camera/HUD/HD arm-cannon identity and audio/video parity | NOT_RUN | Root owns the live native slot; metadata checks do not establish visual/media coverage |
| Windows/Linux/Android physical UI acceptance | NOT_RUN | Local file-only checks on macOS; cross-platform CI is separate |

Registered diagnostics: `-replayformatcheck` and `-replaycontrolcheck`; `-replayexportcheck` requires a source value and invokes actual export. There is no standalone `-replayhardeningcheck` CLI flag: its encoder subprocess fixtures are nested in control check. This slice did not invoke control/export checks or default unknown-flag launcher fallbacks.

## Initial Phase 6 / 9 boundary matrix

| Boundary | Existing source-backed ownership / limits | Decision |
|---|---|---|
| Replay writer | 4 active writers; 4,096 commands / 32 MiB per pump; overflow aborts with recoverable chunks | Reuse; format/backpressure checks pass |
| Replay file/world reader | Bounded headers/chunks/footer/events/checkpoints; declared raw length, CRC, object/collection budgets validated | Reuse; no serializer rewrite |
| Passive seek cache | 128 checkpoints / 64 MiB; maximum 120 fixed steps per host update; unpublished private replacement scenes | Reuse; actual cold/warm seek profiling remains an integration task |
| Storage jobs | Two active disk slots; producer-specific cancellation/stale results | Active bound verified; no blanket assertion that pending requests are bounded |
| Export pending queue | Previously unbounded Enqueue/Retry, one renderer/encoder | Fix here; eight waiting jobs plus active owner and constant retry metadata |
| Encoder pipes | stderr tail 16 KiB; cancellation worker owns kill/wait; stdout uses ReadLineAsync | Separate malformed-output/allocation audit; no process/parser change here |
| Map command history | 500 commands / 256 MiB estimate; undo/redo preserve state identity | Reuse; no editor snapshot rewrite |
| Compiled map cache/work | 8 compiled entries / 128 MiB estimate; 2 workers / 32 scheduled jobs; immutable output/hash publication | Reuse; preparation before scheduled admission remains a separate resource audit |
| Map package | 512 MiB compressed / 1 GiB expanded / 256 MiB entry / 32 MiB project / 2,048 entries; actual length, canonical paths, references, identities and digest checked | Reuse; trust/publication unchanged |
| PK3 / OBJ | PK3 size/count/ratio/path/link checks; OBJ 32 MiB source / 65,535 elements / 256 materials / 128 textures | Reuse; import/conversion and CSG behavior unchanged |
| Community installation | Private bounded snapshot, exact MapId/content/package hashes, owner-thread commit and rollback | Reuse; PROJECT_PRIME_MAP_COMMUNITY remains the correct endpoint setting |
| Updater | Trusted release digest, existing rollback; rejected/cancelled staging correction #490 | Independent completed family; no updater changes here |

## Risk, compatibility and next slice

The only new product limit is eight waiting exports; previously valid manifest shapes/content remain unchanged. Accepted job order, explicit retry behavior and IDs are retained. No protocol/replay version change, content-size limit, format migration, GPU resource lifetime change, FPS improvement, or native/process RSS claim is made.

The gate contains only queue/metadata work. Warming the scheduling facade confirms no idle per-frame allocation from its cached static delegates. Actual start/encoder cancellation races outside queue admission require their existing owner contract and native integration tests; this is not a broad Studio thread-ownership rewrite.

Independent workflow additions share the renderer lifecycle anchor; keep SFX, Music startup, and queue checks during integration. Rollback: revert this commit; existing pending jobs are in-memory only and need no migration.

Next eligible slice: separately prove malformed encoder-output behavior and any sidecar/parser failure before editing. Full Phase 6/9 and physical export acceptance are not declared complete.
