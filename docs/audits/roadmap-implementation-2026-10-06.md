# Engineering roadmap implementation

Work started from `main` commit `a68627d0f7299c873cce9ad8046afbc6af0c46d5`, which remained the remote main revision at the last fetch on October 6. Changes are isolated on `codex/engineering-roadmap`; the original checkout's active asset and UI edits are preserved. This document tracks the findings in the [October 5 audit](project-prime-engineering-audit-2026-10-05.md).

**Status: all eight implementation slices are complete and local integration validation passes.** The changes are organized into nine dependent draft PRs. Native authority, replay preparation and legacy extraction, verified updater recovery, Community ownership, authored mips, platform lifetimes and exact-revision release contracts are implemented. Physical platform, real-network and gateway gates below remain requirements for release. Nothing has been deployed or merged.

The implementation preserves protocol 41, client-owned movement, independently decodable snapshots, one dedicated process per lobby, immutable map identity, private replay worlds, and the GL/GLES fallback. There is no movement rollback or reconciliation.

## Correctness coverage

| ID | Change | Evidence / remaining gate |
| --- | --- | --- |
| B01 | Repair acceptance-check collection and private-setter compilation | Release client and dedicated builds; Android compile |
| B02 | Bound collision candidate seed/idle storage and clear object references | 4,096-candidate ordering/reference regression; full collision fixtures |
| B03 | Refuse installation while the old process is alive | Actual current-process wait timeout; no installation mutation |
| B04 | Validate portable owned paths and every symlink ancestor | Traversal, sibling directory, existing/dangling symlink tests |
| B05 | Correct synthetic macOS positive fixture and test required native libraries | Signing, architecture, entitlement and packaged-resource gates pass |
| B06 | Match Hunter License fixture to its actual latest accepted room | Native PrimeUi 170 and asset-free gamepad 1,006 pass; configured-assets gamepad 1,008 pass |
| B07 | Separate incompatible archived world layout from current layout with an old producer version | Replay control/schema regression; unknown contract remains rejected |
| B08 | Require accepted attack identity and native historical trajectory/area evidence for rescue claims | 503 pure policy checks passed in three independent processes, 57 actual native combat checks and 162 migrated combat checks; protocol-41 real-network loss soak remains a release gate |
| B09 | Authority owns weapon inventory, ammo, charge and cadence admission | 57 actual private ServerSim resource/geometry checks pass; owner source pose remains a stated trust boundary |
| B10 | Reject nonfinite intent and shot pose before ordering, storage or relay | Pure ingress policy checks |
| B11 | Idempotently replay the exact hosting request result | Exact request/hash/result bounded cache tests |
| B12 | Prove advertised UDP endpoint, bound registry/challenges/query responses | Directory challenge, churn, source-fence and farewell tests |
| B13 | Clean up dedicated transport/authority/workers on every exit | Constructor-failure cleanup, stable socket lifetime, 96 rapid-disposal cycles across three processes and actual prerequisite-refusal smoke |
| B14 | Clean up directory transport/children on every exit | Lifecycle/fault checks; UDP farewell coverage |
| B15 | Persist exact custom replay package/session identity | Detached bootstrap identity tests |
| B16 | Reject failed initial restore and clean every unpublished constructor phase | Current-contract absent/mismatched origin tests; session cleanup review |
| B17 | Require supported v4 origin before map/host publication | 2,957 replay-format checks; explicit bounded packet-origin v2/v3 range support retained |
| B18 | Bound shot-fact identities and historical fire-capability life keys | 100,000-frame retained-window/cap/seek tests |
| B19 | Charge passive checkpoint caches for pooled capacity | Allocation/lifetime contracts and source review |
| B20 | Include recoverable partials in retention quota; protect active writers | Aged/fresh/orphan/live-writer retention tests |
| B21 | Bind Android load ownership and release scene leases on failure | Android compile and owner-cleanup fixtures; physical device gate |
| B22 | Wake surface owner on completion, bound handshake and handle stop during load | Lifecycle fixtures; physical rotate/pause/resume gate |
| B23 | Reclaim retained atlas ranges while preserving shared geometry | 20,000 randomized range operations and native scene/resource cycles |
| B24 | Conservative odd-dimension HiZ reduction | CPU reference for 1,365×767 and 801×603; native shader compile |
| B25 | Restrict raw mouse mode to captured scenes | Capture/transition source and native lifecycle coverage |
| B26 | Preserve authored RGBA mip suffix/channel content in modern and GL paths | Stable narrow source snapshot; native decoder and 93 exact per-mip readbacks on each Metal/GL path pass |
| B27 | Report modern resource capacity and explicit coverage | Native allocations/deletes; category upload measurements |
| B28 | Idempotent Android construction and cleanup after partial failure | Cleanup fault fixtures and Android compile; physical device gate |
| B29 | Disable unsafe temporal occlusion while retaining frustum/indirect paths | Explicit diagnostic policy; dynamic occlusion hardware gate |
| B30 | Roll back failed texture publication and reject untextured model output | Transaction/pin/admission fixtures and native lifecycle smoke |
| B31 | Preserve career reports on opaque authentication/configuration failures | Real handler via local SQL adapter; relay gate remains |
| B32 | Token-aware detached prepare followed by owner-fenced UI commit | Cancellation/owner regression cases |
| B33 | Explicit editor-role process mutex and failure presentation | Guard/role fixture; real pop-out window gate remains |
| B34 | Honor map-use leases in Forge publication | Active lease byte-preservation tests |
| B35 | Remap every flipbook frame and validate staged project | Multi-frame import/roundtrip fixtures |
| B36 | Byte-bound catalog persistence and preserve unavailable moderation history | Unicode, size, failed persistence and restart fixtures |
| B37 | Expire upload sessions while the service runs | Controlled TTL tests |
| B38 | Reserve declared upload size against owner quota | Concurrent reservation/reconciliation tests |
| B39 | Reference-count bounded per-upload and cache-key locks | Lock churn and active-key protection tests |
| B40 | Separate transfer/control admission and independent health responses | Eight transfer lanes; ninth transfer 429; health responsiveness |
| B41 | Cancel stale Play discovery and restart on reentry | Generation-fenced injected discovery regression |
| B42 | Bound streamed Edge JSON bytes before parse | Ten Node tests including empty and one-byte chunks; five frozen strict graphs |
| B43 | Bound and coalesce community identity tickets | Cache cap, expiry and single-flight tests |
| B44 | Serialize music generations and cancel/release predecessor loads | Delayed/stale/failed workers and shutdown ownership fixtures |
| B45 | Shared verified installation journal, rollback and startup recovery | 76 updater checks, exception injection and 14 killed-process recovery cases |
| B46 | Smoke owns exact argv/PIDs and complete process trees | Four synthetic process tests and actual product refusal smoke |
| B47 | Build/release contracts use the resolved packaging SHA | Reusable workflow checkout assertion and graph review |
| B48 | Refuse published tag/assets replacement; allow draft repair | Nine actual-shell release-policy behavioral tests; existing tags never force-moved |
| B49 | Require stable Android signing key for public releases | Actual shell missing-key public/draft fixtures |
| B50 | Name throughput honestly and record production phase/return intervals | Nine ledger checks; 562 native Metal production frames |

Detailed implementation notes: [community](roadmap-community.md), [rendering](roadmap-render.md), [network authority](network-authority-roadmap-validation-2026-10-06.md), and [replay staging](roadmap-replay-staging.md).

## Performance decisions

Measurements are workload observations, not universal speed claims. Numerical artifacts are in [roadmap-evidence](roadmap-evidence/).

| ID | Decision and evidence |
| --- | --- |
| P01 | DNS resolution is detached from the authority loop; publication remains on its owner. |
| P02 | Measure existing server pacing before retuning. Local 120-deadline run used about 169 ms process CPU over 2,008 ms wall time, p99 lateness 0.016 ms. No Windows/Linux threshold change is justified by this Mac observation. |
| P03 | Added reproducible independent-cursor workload. Three-reader p99 random seeks were about 1.5 ms in generated 30/120-minute warm-filesystem archives. Three-reader full 120-minute decode took 4.14 s and allocated 3.47 GB in total. Cost is real, but these measurements alone do not justify a new shared cache. Cold filesystem and real effect-heavy archives remain profile cases. |
| P04 | Bounded detached preparation and generation-fenced owner adoption are implemented. The actual eight-player six-minute world passed synchronous/indexed/staged/linear hash parity. Staged opening median 17.78 ms included 13.81 ms total owner work; maximum opening callback across samples was 22.64 ms. Same-player staged seek medians at frames 1,800/18,000/backward 1,800 were 26.44/17.05/16.29 ms. Fresh-player indexed seek at 18,000 was 23.09 ms versus 806.12 ms without the existing index. Warm staged and fresh synchronous samples are different workloads. Whole-process construction max 152.39 ms and restore max 34.27 ms demonstrate remaining monolithic owner cost; the simulation step budget is not a construction deadline. |
| P05 | Bounded installed build cache with key leases, owner gate, safe tagged staging cleanup and pruning. A generated 1,000-edit workload retained 251,256 bytes/12 keys under a 256 KiB quota; independent restarted warm p95 2.98 ms. |
| P06 | Stream strict package digests and allocate exact required entry once. Generated 256 MiB fixture dropped measured allocations from 512.35 MiB to 0.03 MiB and wall time from 244 to 113 ms. The package writer still retains expanded source arrays. |
| P07 | Two preparation lanes with cancellable admission. Burst peak fell from 27 simultaneous preparations to 2; aggregate/wall time varies with machine load. |
| P08 | Index favorites and owner-map reads after successful persistence. Generated 100,000-favorite/1,000-map read p50 fell from 193 ms to about 0.1 ms. Mutations still rewrite the bounded catalog under its lock. |
| P09 | Added actual DebugLog.Line benchmark with ring/local file and explicitly artificial delayed-flush sensitivity cases. Local-file p99 was 0.029 ms for ten lines and 0.137 ms for 100 lines per sample. Delayed flushes show sensitivity, not a measured player disk problem. Preserve crash logging; no async writer rewrite is justified by the local results. |
| P10 | Native Metal Combat Hall emitted 149 packets, two templates and about 1.90 MB of buffer writes per frame; category attribution identifies ordinary and retained uniform uploads. CPU API work was about 0.38–0.44 ms in the 30-sample test. No speculative packet rewrite or credible 1% low is claimed. |
| P11 | Instrument/retry bounded room prewarm and fix generation-owned audio readiness. Real cold process-to-first-present and physical platform startup measurements remain gates. |
| P12 | Native Metal adapter/device callbacks completed inline (0.032/0.019 ms observed). Other drivers' delayed callback behavior remains a hypothesis; no new asynchronous platform layer is justified by this sample. |

## Validation and release limits

The final product source compiles as a Release client (101 warnings, zero errors), dedicated server (37 warnings, zero errors) and Android arm64 Compile target (117 warnings, zero errors). The isolated complete contract suite and targeted final transport delta pass; actual dedicated prerequisite refusal passes eight checks, including genuine exit code 1 and an empty answering directory. The client's missing generated Avalonia resource was resolved by a clean rebuild; the failed attempt is preserved and is not counted as a pass. See [isolated source proof and counts](roadmap-isolated-contracts-2026-10-06.md) and [final validation summary](roadmap-evidence/final-validation.json).

Final replay validation passes 2,957 format checks, 24 asset-free preparation checks and 80 actual asset-world checks. Live capture passes 1,801 accepted-fact frames with 251 frozen comparisons; killcam passes 2,354 assertions. The asset matrix covers quiet modern carriers, actual source-frame shots, a shot first recovered 15 frames late, marker deduplication, owner failure cleanup, superseded publication and v2/v3 extracted/nested synchronous/staged parity. Late accepted fire repairs an unpublished private world from a strictly earlier valid capsule; missing history or exhausted bounds makes instant history explicitly unavailable. It never applies the shot late to the foreground world.

The asset-free fast suite initially stopped after 7,684 lobby assertions on a custom-map admission timeout. A quiet isolated full rerun passed 7,736 assertions. The simulated client had sent one unsequenced Hello without retry; it now follows the production client's one-second retry cadence, and deliberately dropping the first Hello passes under the original four-second deadline. This correction does not enlarge deadlines or treat logged startup as successful admission.

Updater recovery was tested with thrown exceptions and actual child exits after each mutation, including both file/directory transitions. These are process-crash recovery tests; portable directory-fsync/power-loss guarantees are not claimed.

The production frame trace is CPU/driver-return timing. Its 562-frame Metal shell script includes cold loads, resize/fullscreen, pause/rematch and screenshot work, with one missed UI step. Present-return p50/p95/p99 were 11.96/28.88/396.57 ms. These are not GPU durations, display scanout intervals or steady-state game 1% lows. The bounded trace contains no per-frame file writes and is disabled by default.

Required gates before release:

- Physical Android Vulkan/GLES rotate/pause/resume, stopped-load and repeated-match ownership tests.
- Windows DX12/Vulkan and Linux Vulkan native resource/visibility/frame runs, plus actual updater/process-tree smoke on those platforms.
- Native high-refresh steady-state and effect-heavy 60/120/144/240 Hz traces with enabled/disabled instrumentation comparison.
- A real local Supabase relay test of the registered opaque reporter credential and per-function JWT configuration. Docker's local daemon was unavailable; the handler adapter and frozen typechecks are distinct evidence. See [the exact local acceptance procedure](../../tools/edge-check/README.md).
- Real protocol-41 delayed/lost/reordered native homing, ricochet, cluster and anonymous alt-attack soak; the deterministic native matrix and authored mip readbacks pass. Owner movement/source pose remain supplied by the client.

## Review boundaries

The nine focused draft changes are: baseline contracts; verified updater transactions; smoke process ownership; renderer/Android/audio lifetime and authored mips; Community/Forge/UI ownership and bounded services; combat/directory authority; replay identity/history/retention/preparation; production phase measurements; exact-revision CI, locked Edge dependencies, diagnostics and evidence. Each member builds on its predecessor. Keep production deployment and main merges outside this implementation run.

The open RmlUi PR #347 (`Polish RmlUi home for Retina and cinematic menu stage`) remains independently owned; its October 6 observed head was `cbe8f81acb3b4c48d0005db9cde705c1d0569c01`. The audit's PR-specific layout, thumbnail publication and shutdown observations remain integration review items. This branch does not duplicate its RCSS/stage work.
