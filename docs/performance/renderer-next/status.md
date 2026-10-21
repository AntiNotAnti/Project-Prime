# Renderer implementation and acceptance ledger

Prepared 2026-10-21 on `codex/renderer-next`, base `9885b151cd99e598b48871d8c7cd615d7979130b`.
Audit source: `07f23c014a408d134d1cc67e2f1d7074fc7f7942`.
The supplied [plan](audit-plan.md) and [finding reconciliation](reconciliation.md) are retained.
Existing character/lobby work and unrelated untracked `.vscode/` files were preserved.
No commits, PRs, releases or preset promotions were performed.

**Overall acceptance: INCOMPLETE.** Implementation, focused validation, native
qualification and rollout are separate states. All executed tests used Windows.
An Android package built on Windows is not a physical Android test. macOS,
Linux and device execution are NOT_RUN at the user's direction. AGX remains
**UNRESOLVED_P0**; program reuse remains **NOT_ELIGIBLE**. No universal FPS gain is claimed.

## Package disposition

| Package | Implemented scope | Validation / remaining gate |
|---|---|---|
| R00 | Audit reconciliation, fixture/source/binary manifests, durable receipts | No pristine baseline build attestation; failures preserved. |
| R01 | Schema-3 hitch/overflow retention, whole callback/provider-wait attribution, lifecycle/failed-present boundaries | [Measurement](measurement.md); real FrameTiming and strict analyzer regressions PASS. Scheduler/scanout attribution and capture overhead UNVERIFIED. |
| R02 | Display/numeric/Unlimited coverage and actual render-size reporting; diagnostic launcher/input isolation repaired | Integrated matrix/soak pending. Development/interrupted runs remain diagnostic; no speculative scheduler replacement. |
| R03 | Android requested versus observed refresh/interval ownership, first/resume Unlimited and request retry | [Android](android.md), 70 Windows source/policy checks PASS. Physical mode/request tests NOT_RUN. |
| R04 | Cancellable bounded EGL backoff, failed-present classification, generation reset and orderly context-loss exit | Offline/build checks PASS. Physical surface/context-loss lifecycle NOT_RUN. |
| R05 | Authoritative mip completeness through upload/direct/retained/companion sampling; failed stable-ID promotion restores native data | Windows material/native fixtures PASS. GLES pixels NOT_RUN. |
| R06 | Actual prepared GLES compiler text in binary identity, translator/link ABI/driver, bounded disk pruning | Windows source/key regressions and Android build PASS. Native cache images/corruption/driver transitions NOT_RUN. |
| R07 | Unit-zero UI initialization/resize, separate stencil state, transactional temporary ownership | Optional Android UI real C# Compile PASS. Native GLES state/failure matrix NOT_RUN. |
| R08 | Shared framebuffer/dimension/format descriptor, checked RGBA8 readback/repacking and complete pack/FBO/PBO restoration | Windows exact-edge/state/failure fixtures PASS. Device/export workflow qualification pending. |
| R09 | Transactional scene/depth allocation, lazy cel, singular depth ownership, estimated per-scene byte ledger, bounded scratch | 1,680 Windows failure/recovery scenarios PASS. Total driver/process and concurrent Studio/launcher memory UNVERIFIED. |
| R10 | Release portal capacity checks and conservative draw-all fallback | [CPU preparation](cpu-preparation.md), dense/cyclic/malformed/capacity native fixtures PASS. |
| R11 | Bounded per-room/view scratch, adjacency reuse, one view-projection refresh | Controlled 100,000 clips: 38,400,000 versus zero warmed managed bytes, exact polygon parity. Production map/frame gain UNVERIFIED. |
| R12 | Complete model/context-local compile identity, publish-after-success, shared lease retirement, desktop A/B opt-out | Synthetic five/four objects, 960/768 VBO/IBO bytes, exact pixels/reload/failure ownership PASS. GLES sharing NO_ROLLOUT: mutable inherited normals. |
| R13 | Exact same-process warmup anchor required by AGX assessor | Synthetic regression PASS. Native macOS attribution NOT_RUN; AGX UNRESOLVED_P0. |
| R14 | NO_ROLLOUT | R13 reuse eligibility unsatisfied; no global program cache workaround. |
| R15 | Combined-build miss/hit/fallback/byte and inclusive build/upload instrumentation | Submission strategy unchanged; optimization conditional on controlled attribution. |
| R16 | Decode reservations, bounded request examination, attempted-byte/worst-upload timing; coalesced per-binding settings jobs, resident sampler freeze, native mip prewarm and cancellation | [Settings work](texture-settings.md), Windows GPU rapid-toggle/partial-companion/stale-job/native-mip checks PASS. Individual uploads remain indivisible; visible priority/incremental work NOT_IMPLEMENTED; HD-pack tails/memory UNVERIFIED. |
| R17 | Authored RGBA mip retention/lower-level selection; default-off, capability-checked desktop BC7 candidate | [Compression](desktop-compression.md), Windows native authored container/content and actual BC7 storage/pixel checks PASS (Debug functional scope). Pack/channel quality, transcode peak and other drivers UNVERIFIED; NO_ROLLOUT. |
| R18 | Bounded preallocated detached report worker; telemetry v2 current successful completion interval/stages/GC | Windows policy/source and mixed-version comparator PASS. Device report overhead/thermal/lifecycle NOT_RUN. |
| R19 | NO_ROLLOUT | No attachment invalidation without proven last consumers and native comparison. |
| R20 | Optional opaque-world shadow receiver, cutouts, emissive/late/HUD/viewmodel exclusions, native fallback and pause projection | [Shadows](shadow-receivers.md), 14,822 integrated preparation/receiver checks PASS; pause negative control reproduced/closed. Full scene/motion/device qualification pending. |
| R21 | Footprint/slope bias, range fade, cutout casters | Detached offscreen dynamic collection in progress. Static reuse NO_ROLLOUT. |
| R22 | Default-off camera-relative specular and robust tangent/normal correction candidate | [Materials](materials.md), native synthetic camera/mirrored/degenerate checks PASS. Full transfer-function/alpha/mip/linear blend/output migration NOT_IMPLEMENTED; NO_ROLLOUT. |
| R23 | Default-off spatial AA before HUD; bounded transactional scratch/state restoration/fallback | [Spatial AA](spatial-aa.md), native synthetic pixels/ownership/zero-warmed-allocation PASS; 720p cost recorded. Real still/motion and other devices pending; NO_ROLLOUT. |
| R24 | NO_ROLLOUT | Optional local lighting requires R22 and justified CPU/GPU budget. |
| R25 | NO_ROLLOUT | Optional bloom requires accepted color/target/cost contracts. |
| R26 | NO_ROLLOUT | Optional AO requires accepted depth/normal/material contracts. |
| R27 | NO_ROLLOUT | No adaptive resolution without GPU-bound, timed, user-selected qualification. |
| R28 | Whole-callback/launcher attribution, shared capture descriptors, bounded targets, standalone startup ownership fix | Full chamber/lobby/Community/replay/four-view Studio responsiveness UNVERIFIED. Async export ring conditional on a measured bottleneck. |
| R29 | NO_ROLLOUT | Compatibility context preserved; no backend replacement. |
| R30 | Integrated builds, regression/negative receipts, explicit fallback/feature policy | Final immutable Windows cadence/soak pending. Missing hardware, R21/R22 gaps and real-scene image/cost gates prevent full acceptance. |

## Verification scope

SDK pin remains 10.0.401. The local SDK/workload prerequisites were installed
without changing the pin. Client, server and normal Android ARM64 AOT/package
builds passed on Windows; optional Android UI was separately forced through C#
Compile and its type presence/hash recorded. Intermediate receipts retain their
own MVID/source scope and do not substitute for the final binary.

Focused checks cover real timing/capture, desktop/Android pacing policy, strict
native-FPS/AGX evidence, material/mip residency, native authored KTX levels,
shader/target failure recovery, portal/geometry, shadow receivers, material math
and spatial AA. The native Python suite passed 117 tests with five explicit
platform/Windows symlink-privilege skips before startup/input tests were added.
Two material symlink fixtures are NOT_RUN for Windows privilege error 1314.
Historical evidence was restored to exact HEAD bytes; validators were not
relaxed to conceal missing records or CRLF hash differences.

Large raw captures and invented image outputs remain under
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-next-20261021`.
Proprietary fixture assets remain outside the repository. Small reports, hashes,
exact commands and failed receipts are preserved here.

## Remaining acceptance

Finish the integrated Windows cadence and 30-minute stability gate, preserving
every failure. Finish detached offscreen-caster preparation and the full color/
alpha migration before calling R21/R22 implemented. Qualify real scenes/motion,
scales, side views and transitions, then physical Android/macOS native gates
when the Windows-only restriction is lifted. No build or elapsed time closes
those gates. Existing Original and compatibility fallbacks remain available.
