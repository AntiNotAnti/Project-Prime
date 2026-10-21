# Custom map pipeline audit and measurements

Audit date: October 21, 2026. Initial source: `dd6d8deac`.
Measurements are synthetic local fixtures on Windows; remote throughput, actual
gameplay-ready latency and other platforms require their own acceptance runs.

## Existing lifecycle and prioritized bottlenecks

| Stage | Current implementation and ownership | Finding / action |
|---|---|---|
| Website selection | Website `mapUploadFormats`, bulk uploader, guest upload flow | Mixed queues, drag/drop, per-file cancellation and retries already exist. Filename-based routing is a convenience; services validate archives. Structural browser preflight remains an acceptance gap. |
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

## Acceptance still required

The broad plan's 30% cold-load / 50% warm-load objectives remain unproven until
the same actual maps run before/after on target devices. Required campaigns
include representative near-limit valid archives, remote upload/download,
real render/collision/entity equivalence, gameplay-ready transitions and
rotations, frame stalls, Android lifecycle/memory pressure, Linux service
crash/resource isolation and Windows/macOS/Linux/Android CI. No component
microbenchmark substitutes for these end-to-end gates.
