# Custom map pipeline audit and measurements

Audit date: October 21, 2026. Initial source: `dd6d8deac`.
Measurements are synthetic local fixtures on Windows; remote throughput, actual
gameplay-ready latency and other platforms require their own acceptance runs.

## Existing lifecycle and prioritized bottlenecks

| Stage | Current implementation and ownership | Finding / action |
|---|---|---|
| Website selection | Website `mapUploadFormats`, bulk uploader, guest upload flow | Mixed selection, per-file cancellation and manual retry already exist. **P2:** drag/drop is missing and routing starts from filename extensions. Add a shared structural ZIP preflight and route both selection methods through it; services remain authoritative. |
| Upload | Website `mapPublishing` / `pk3Publishing`; `MapCommunityClient`; `pk3-upload/service.mjs` | Bounded chunks, offset recovery, exact digests, authentication and quotas exist. **P2:** full owner catalog fetched for every file in a batch; share only within an authenticated batch with explicit invalidation. |
| Conversion | `Q3ImportService`, `Pk3DiscoveryService`, `Q3Convert`, `MapTextureBake` | **P1:** conversion keys contain job directory paths and hash the source twice. Child process HOME also makes the cache job-local. Use portable content keys, keep dependency precedence, explicit generator invalidation and a bounded persistent service cache. Analysis and texture extraction still repeat archive indexing/parsing. |
| Packaging | `MapBuildScheduler.PackageAsync`, `MapPackageBuilder`, `MapPackageReader` | Deterministic streamed asset payloads and strict readback already exist; preserve golden archive bytes. Packages intentionally remain source-compatible and platform-neutral. |
| Storage / CDN | `MapCommunityServer`, `CommunityObjectStore` | Immutable exact SHA-256 objects, request admission lanes, Range, conditional catalogs and origin fallback already exist. Production CDN configuration/missing-object recovery needs deployment evidence. |
| Download | `MapCommunityClient`, `CommunityExactArchiveAcquisition`, `CommunityArchiveCache` | **P2:** full synchronous disk reread after healthy streamed download. Hash while writing; rehash persisted prefixes before resuming; retain exact final digest and bounded retries. Shared handlers and per-hash cross-process acquisition already exist. |
| Installation | `MapPackageInstaller`, `PreparedMapInstallation` | Private snapshots/leases, bounded atomic copy, rollback and exact reinstall skip exist. Commit still verifies/publishes synchronously under the scene gate; substantial cold-commit work needs owner-lifecycle redesign and live frame evidence. |
| Build / cache | `MapBuildScheduler`, `MapCompilationCache`, `MapBuildManifest`, maintenance service | Bounded workers, single-flight jobs, content/version/generator fingerprints, persistent output verification and bounded in-memory geometry already exist. Warm verification still hashes all dependencies and output files; timestamps alone cannot safely replace integrity checks. |
| Prewarm / loading | `RoomPrewarm`, `Read.PrepareRoomModel`, `MatchStart`, texture preparation queue | CPU prewarm and one active advertised-room owner already exist. GPU resources remain graphics-thread owned. Cold GPU uploads, source geometry and collision costs need actual small/medium/large-map frame captures before changing scheduling or geometry. |
| Community | Native discovery controllers, `NativeCommunityDiscoverPage`, bounded thumbnail workers | Metadata-first background reads, paging, stale-request cancellation and bounded preview work exist. Legacy activation fixes also exist. Verify large installed libraries and native UI frame times on hardware. |
| Multiplayer / rotations | `MapSynchronization`, `MapVoteReadiness`, hosted archive acquisition | Exact MapId/content/package agreement, generation-fenced Ready, detached acquisition and selected-map lobby prewarm exist. Preserve all barriers and historical replay resolution. No speculative metered-device prefetch is added. |
| Editing / replay | Map Studio snapshots/scheduler; historical package acquisition | Existing shared compiler and immutable identities must remain authoritative. Fresh imports have independent GUIDs; reimport preserves creator identity. Reusing conversion artifacts must never reuse a new publication's identity implicitly. |

The order prioritizes avoidable repeat conversion, then transfer work and bulk
control traffic. Existing caches are extended; there is no second map system.
Each change is a separately reviewable commit. Existing platform CI remains a
merge gate; local Windows checks do not establish Android/macOS/Linux runtime
acceptance.

## Reproduction

Use the SDK pinned by `global.json` and run on the same hardware/power settings:

```powershell
dotnet run --project tools/community-baseline-check -c Release -- --output community-baseline.json
dotnet run --project tools/map-download-check -c Release -- --repeat 20 --output map-download.json
node --test tools/pk3-upload/service.test.mjs
dotnet run --project tools/map-editor-check -c Release
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/community-cache-check -c Release
dotnet run --project tools/map-multiplayer-check -c Release
```

`map-download-check` uses seeded 32 MiB bytes and primes JIT before timing.
It covers full exact reuse without HTTP, correct/corrupt persisted prefixes,
corrupt responses, bounded integrity retries and cancellation/recovery. Reported
process CPU and working set include its local HTTP fixture. Percentiles over
20 samples describe this microbenchmark; they do not measure real map loading.

## Initial measurements

The existing frozen Community fixture passed, preserving its exact Windows
archive SHA-256 `23290c12262ac9cd2dfd29bbef74867fcc066a3277d8e9a97c8ae44d1f3e98fc`
and historical protocol-53 package. Its 1,448-byte package is a compatibility
fixture, not a representative throughput workload. Cold prepare/build/install
was 181.213 ms; repeated exact reinstall was 8.1852 ms. JIT and cache effects
are included; this is not an optimization comparison.

Before changing download verification, 20 primed 32 MiB transfers measured
64.9242 ms P50, 87.679 ms P95 and 89.5752 ms P99. All 29 contract assertions
passed. Raw samples are retained with the final performance evidence.

## Streamed download verification

`MapCommunityClient` computes SHA-256 incrementally while writing a healthy
response, using the existing 64 KiB transfer bound. It hashes a persisted prefix
through the exclusive append handle before accepting a resumed response. A
complete existing file is still fully hashed with cancellable asynchronous I/O.
Strict package/MapId/content verification at installation remains unchanged.
Corrupt results are discarded with the same two-attempt integrity limit.

| Windows 32 MiB transfer stage, 20 primed samples | Baseline P50 / P95 (ms) | Streamed P50 / P95 (ms) |
|---|---|---|
| Initial run | 64.9242 / 87.6790 | 32.5084 / 34.0646 |
| Repeat using preserved baseline and optimized assemblies | 70.2221 / 100.9367 | 32.2090 / 38.1336 |

All four runs passed 29 assertions and produced identical SHA-256 bytes. The
second pair used the same compiled harness with the preserved pre-change
production assembly and the optimized assembly, restoring the optimized DLL
after the baseline run. Runs were sequential, not randomized; OS scheduling,
CPU power and warm filesystem effects remain limitations. Raw per-run samples,
process CPU and peak working set are in `map-pipeline-evidence/`. No memory,
remote-bandwidth or full-map-loading improvement is inferred from this result.

`community-transfer-check` additionally passed 61 checks, including actual
cross-process ownership, deduplication, crash recovery, pause/cancel, resumed
HTTP Range and corrupt archive repair.

## Reusable Q3 conversion artifacts

Conversion keys exclude temporary job paths and include source bytes/basename,
dependency bytes in precedence order, level, options and compiler/generator
identity. The source's archive role is explicit without hashing its bytes twice.
Reused artifacts contain relative filenames; fresh imports retain independent
map GUIDs and reimports preserve their authored identity. No package format,
published hash, collision algorithm or multiplayer protocol changes.

The web worker sets `PROJECT_PRIME_Q3_CACHE` to a service-owned persistent
directory rather than its short-lived job HOME. Existing eight-entry / 512 MiB
retention is protected by a cancellation-aware cross-process publication lease.
Only owned, recognizable pending artifacts are removed after a terminated
writer. Shutdown aborts and joins the child, retaining its verified input queued
for restart. OS isolation still owns forced parent/container termination.

The importer passes its already parsed BSP into conversion on misses. The public
file-based converter still validates/loads its source. Source-content mutation
checks before cache storage and final publication remain intact.

| Dust2 import, 2,709,119-byte source | Original | Optimized |
|---|---:|---:|
| Cold import (ms) | 313.78 | 295.61 |
| Cold same-thread allocated bytes | 142,296,016 | 101,919,560 |
| Repeat from relocated directory (ms) | 126.47 (miss) | 65.26 (hit) |
| Repeat same-thread allocated bytes | 132,287,016 | 82,081,184 |
| Fingerprint (ms, after warmup) | 2.38 | 1.20 |
| Fingerprint allocated bytes | 168,056 | 84,408 |

These are one before/after importer pair on the same Windows machine, not
statistical load-time percentiles. JIT/cache effects remain; the second import
is warm in both arms. The same compiled benchmark ran against the preserved
original production assembly, then the optimized assembly; it restores the
optimized DLL afterward. It measures import, not final package compilation or
gameplay readiness. Reproduce with:

```powershell
dotnet run --project tools/pk3-conversion-bench -c Release -- --source maps/dust2/df_dust2.pk3 --room PIPELINE_BENCH --map df_dust2
```

Use `--allow-relocated-miss` only when measuring the original implementation.
The benchmark makes private source copies and isolates/disposes its cache. No
input map or creator content is rewritten. Raw receipts are retained alongside
the download samples in `map-pipeline-evidence/`.

## Prewarm cancellation and real rendering smoke

`RoomPrewarm` now cancels the retired generation's runtime build wait, including
the cross-process generation lease. It checks cancellation again before starting
model decoding after file reads. The worker keeps its configured-files lease
until it really finishes, and only that worker disposes its cancellation source.
The existing scheduler preserves builds still needed by other waiters. An active
model decode or atomic publication is not interrupted unsafely; there are no new
OpenGL calls or threads. The regression holds a real generation lease, cancels
the waiting worker, and verifies no runtime manifest was published.

Actual Windows OpenGL 4.6 / NVIDIA 617.42 eight-player `-maptest DUST2` runs against
the preserved original and optimized assemblies produced identical MAPTEST
summaries: 1,048 frames, 8/8 spawned, 10 spawns, 30 item spawns and identical
visibility/remote-node counters and lighting samples. Both returned exit 0.
Both also report **burn probe FAIL**; this existing diagnostic failure remains
unresolved and prevents treating these smokes as all-green gameplay acceptance.
The summaries are retained in `map-pipeline-evidence/`; no proprietary assets
or captured pixels are included. These runs do not measure load-time percentiles.

The full combined Windows map-editor suite passed 510 checks. Archive-cache
checks passed 22 checks, transfer checks 61, and multiplayer acceptance completed
its isolated clients, rotation, restart, rejection and historical-package cases.

## Website upload and service recovery

The companion website now uses one structural archive preflight for mixed
selection and drag/drop. A root package manifest takes precedence over BSP
entries; an invalid manifest never falls back to conversion. Archive paths,
entry counts, sizes and symlinks are bounded before routing. Processing remains
sequential, with a maximum batch of 30 files, individual cancellation and retry.
The archive library still retains its central directory; this change only
removes an additional application-side entry array and duplicated inspection.

An authenticated batch shares its owner catalog and requests a fresh ticket for
each publication. The mock three-publication test uses one catalog read and
three tickets, preserving each published parent hash. At 30 publications this
removes 29 catalog reads (96.7%), or 29 of the previous 60 catalog/ticket requests
(48.3%). These are request counts, not measured live upload latency. Interrupted
chunk writes reconcile the authoritative server offset before retransmission,
with bounded exponential backoff, jitter and cancelable waits.

The conversion service stages durable job metadata before moving the verified
source. It promotes the complete directory atomically before linking the upload
session. Startup reconciles staged/final jobs and uniquely identifiable legacy
orphans; ambiguous source files are preserved and counted against disk quotas.
Fixtures exercise crashes before input movement, after movement and after final
promotion, plus exact job reuse, mismatched metadata and quota accounting.
The local service suite passes 13 tests. Linux crash and filesystem behavior
still requires the platform CI/deployment gate.

The website also pins the compatible Next.js 15.5.28 security patch. Its audit
record documents remaining transitive advisories and the major-version upgrade
needed to address them; this is not a clean dependency-audit claim.

## Delivery and validation status

| Pull request | Scope | Status at report update |
|---|---|---|
| [Project Prime #1023](https://github.com/AntiNotAnti/Project-Prime/pull/1023) | Audit and reproducible transfer baseline | Open; refreshed onto current main; CI running |
| [Project Prime #1024](https://github.com/AntiNotAnti/Project-Prime/pull/1024) | Streamed download verification | Open; stacked on #1023; CI running |
| [Project Prime #1025](https://github.com/AntiNotAnti/Project-Prime/pull/1025) | Portable conversion reuse and persistent worker cache | Open; stacked on #1024; CI running |
| [Project Prime #1026](https://github.com/AntiNotAnti/Project-Prime/pull/1026) | Retired prewarm cancellation | Open; stacked on #1025; CI running |
| [Project Prime #1027](https://github.com/AntiNotAnti/Project-Prime/pull/1027) | Crash-safe conversion job promotion | Open; stacked on #1026; CI running |
| [Website #16](https://github.com/projectprimegg/projectprime-website/pull/16) | Mixed bulk uploads and interrupted-chunk recovery | Merged after CI and independent review; merge `ca5697f7` |

The engine/service PRs remain unmerged at this update. Earlier CI cancellations are not passes.
The converter workflow now installs the exact SDK from `global.json`; installing
the latest 10.0 SDK had failed the repository's strict SDK pin. Current-main
replay test repairs are incorporated without duplicating them in this work.

CI also exposed pre-existing qualification drift on current main. The authored
Android action inventory includes one additional hidden button (two assertions),
and profiling storage had not included the two combined-geometry CPU passes.
Both draw and uniform attribution dimensions now include those passes and a
separate unknown slot, with a regression checking their actual array indices.
The render-graph check now verifies shadow input on World, matching the existing
inline shadow receiver, and its absence from the postprocess pass. The Android
capability harness passes 800 assertions and `-rendergraphcheck` passes locally.

Additional local checks pass: the frozen baseline with stress enabled, six
streamed-package checks using a 16 MiB asset size, and a server-only Release
build. Package checks retain byte parity, cancellation preservation and oversized
sparse-input rejection. These are deterministic local checks, not near-limit
valid-map gameplay or production load tests.

## Remaining acceptance and bottlenecks

The broad plan's 30% cold-load / 50% warm-load objectives remain unproven until
the same actual maps run before/after on target devices. Required campaigns
include representative near-limit valid archives, remote upload/download,
real render/collision/entity equivalence, gameplay-ready transitions and
rotations, frame stalls, Android lifecycle/memory pressure, Linux service
crash/resource isolation and Windows/macOS/Linux/Android CI. No component
microbenchmark substitutes for these end-to-end gates.

Startup previously called `CustomRooms.GenerateMissing()` for every registered
custom map, even when selecting one diagnostic map. A real DUST2 smoke built six
rooms. Preparation now occurs for the selected room before the shared scene
loader reads collision data, with an explicit material-report boundary and the
existing early launcher/server gates retained. Low-level model reads remain
unchanged to avoid recursive compiler entry. The isolated regression prepares
one of two missing custom rooms, leaves the other source/output untouched,
preserves output lengths/timestamps on repeat preparation, handles a registered
native room without custom generation, and reports failed selected recipes.
These work-count and correctness checks do not establish load-time percentiles.
Further priorities are cold commit work under the
scene gate, GPU upload/frame captures, dependency-integrity verification cost,
and production CDN fallback/load evidence. None has a measured improvement
claim in this report.
