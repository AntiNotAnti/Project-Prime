# Community Hub 3.0: PR-04 immutable archive cache

**Base:** stacks on PR #536's durable-transfer branch, itself based on merged PR #535. This remains draft until PR-03 passes its exact-head transfer/compatibility matrix.

## Source-level scope

- `CommunityArchiveCache`: immutable `.ppmap` storage keyed by full SHA-256; strict `MapContentIdentity.FromPackage` verification on cache acquisition and promotion; safe staging on the cache volume; corrupted objects quarantined and repaired only after exact revalidation.
- Shared read leases are represented by open file handles against `locks/<sha>.lease`; eviction takes an exclusive handle and skips readers from other processes. Callers must retain the lease while preparing/installing an archive.
- Cross-process per-hash download gates (independent `.download` lock files) avoid concurrent writes to one partial archive. A second downloader rechecks the strict cache after waiting on the gate and can avoid archive network bytes.
- Bounded LRU eviction of *only private CAS archives*. This does not delete installed maps, replay archives, gameplay output folders, or partial download bytes. Cache access information is advisory, does not affect archive bytes, and is not treated as trust.
- `CommunityTransferManager` uses this CAS before network, promotes downloads under an exclusive file lock, and returns a held `LeaseVerifiedArchive` for map installation. The Game Community UI keeps that lease until the existing installer returns. The authoritative lobby/map Ready path remains unchanged.
- New `tools/community-cache-check` verifies strict identity, cross-instance reader/download lock behavior, LRU under a held lease, byte-preserving repair/quarantine, and failure on wrong source/package identity.

## Compatibility and limits

- `MapId`, content SHA-256, exact archive SHA-256, all existing `.ppmap` bytes, v2 HTTP routes, revision history, historical replay lookup, and the 60 Hz gameplay protocol are unchanged.
- Existing `MapPackageInstaller` is unchanged and performs its own private build and commit under `MapRuntimeUsage`.
- `CommunityTransferManager` remains a **single-process journal owner**. The cache's archive/download locks are cross-process, but sharing one `jobs-v1.json` between concurrently running Game and Studio processes is not yet supported. Studio should use a separate transfer journal root if it adopts this manager. Do not present the whole transfer journal as cross-process safe until multi-writer semantics are tested.
- This slice does **not** optimize away redundant installer copy/build I/O (PR-05) or alter host/spectator/replay downloads to use CAS until those owners have compatibility tests.
- A cross-process downloaded archive is not playable unless strict identity validation, compiler readiness, and the existing authoritative Ready barrier all succeed.
- Quotas and retention must be measured against real map packages and Android storage. Cache LRU sizes are configurable, with no claim of a measured speedup until A/B runs.

## CI / acceptance

```bash
dotnet run --project tools/community-cache-check -c Release
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet run --project tools/map-multiplayer-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

Required platform matrix: Windows/Linux/macOS desktop, Android build + actual device/emulator runtime/low-storage smoke where available. CI must be inspected by exact commit SHA; if not run, mark NOT_RUN. Run a separate real multiprocess lease/GC test before promising platform-wide concurrency.

## Rollback

The CAS is additive and private under its own root. Revert cache integration and the new test/tool files to restore PR-03's durable transfer path. Do not rewrite or remove existing installed map archives. Quarantine files are user-data troubleshooting artifacts, not installed runtime content. No server-side or network migration is required.
