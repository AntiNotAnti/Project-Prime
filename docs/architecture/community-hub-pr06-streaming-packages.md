# Community Hub 3.0 | PR-06 Deterministic Streaming Packages

**Source baseline:** `AntiNotAnti/Project-Prime` at `c42631221fed200ff63b82bf598be274da0642e5`, after PR-05 #538.  
**Scope:** reduce asset-sized allocations during `.ppmap` creation, input hashing and strict verification, while retaining exact existing package bytes and all multiplayer/replay identities.

## Prior pipeline / performance bottlenecks

Before PR-06, `MapPackageBuilder.WriteValidated` populated a `SortedDictionary<string, byte[]>` with **every asset and generated payload**. `MapAssets.Read` loaded loose texture/audio files and bundled assets completely into managed memory. The authoring dependency/fingerprint pass also used `SHA256.HashData(MapAssets.Read(...))` and allocated an entire array for each asset. After writing the archive, `MapPackageReader` strictly verified content by decompressing entire ZIP entries into byte arrays via `ReadRequired`.

With hundreds of megabytes of textures/audio, peak resident memory could include multiple asset copies. Archive compression, the compile cache and network upload are separate costs.

## Implemented design

1. **Input stream ownership:** `MapAssets.WithPackageRead` exposes a bounded stream callback. It checks the same project-root/canonical pathname and per-entry limits as the previous file reader. For sources inside an existing bundle, `MapBundle.WithReadEntryStream` performs the existing strict initial validation and uses the validated ZIP source stream during a scoped callback; ZIP and entry streams cannot escape it.
2. **Streaming dependency fingerprints:** use `MapAssets.HashPackageAsset` (64 KiB chunks) instead of creating asset-sized `byte[]` for `MapDependencyAnalyzer.Analyze`.
3. **Source descriptors:** replace the in-memory dictionary with ordinally sorted `Payload` descriptors, each storing expected byte length and a callback that opens a fresh source stream. Loose assets are read on demand; generated/trimmed BSP and collision bytes are immediately spooled to a private scratch file if larger than 1 MiB.
4. **Deterministic content digest:** pass 1 streams the old SHA-256 input framing in ordinal entry order: **4-byte little-endian UTF-8 path length, UTF-8 path, 8-byte little-endian data length, raw uncompressed data**. It detects missing, truncated or growing files.
5. **Deterministic ZIP:** pass 2 emits the same ordinal names, unchanged `CompressionLevel.SmallestSize`, ZIP entry timestamp 1980-01-01 UTC, attributes 0, 64 KiB write cadence and exactly the old manifest metadata. `manifest.json` is inserted in its previous sorted position. No new `.ppmap` version or migration is introduced.
6. **Strict verifier:** `MapPackageReader` now computes the identical SHA-256 framing from decompressed ZIP streams through a pooled 64 KiB buffer rather than materializing each entry. The constructor retains its entry-count, compressed/expanded size, project/manifest, runtime name, compatibility and reference checks. The bounded `ReadRequired` still handles explicit project/preview or individual asset retrieval.
7. **Atomic publication:** staged ZIP lives beside the destination and passes the strict reader before replacing any existing package; failures and cancellation delete the private stage/scratch without publishing any archive.

## Compatibility and source guarantees

- **No change** to any previously published map's `MapId`, content SHA-256, exact archive SHA-256, ZIP entry set, ZIP compression level, entry sort, timestamp, or network protocol.
- Existing historical replay packages remain byte-addressable, not converted. Map streaming does not change map download manifests or cache/Ready decisions.
- Keep current **platform-specific** frozen archive goldens from PR-00. Windows and Linux/macOS previously produced different exact archive bytes for identical authoring inputs; this PR must preserve each platform's prior identity, not introduce a third variant.
- The input is passed twice to reproduce manifest ordering. If an authoring asset changes in length, either pass refuses it. If bytes change at equal length, the post-write strict hash check rejects the ZIP before publication. `MapBuildScheduler.RequireUnchanged` continues checking the source fingerprint before final move.
- Assets re-exported from verified older `.ppmap` bundles are streamed from a scoped ZIP reader. The existing `MapBundle.ReadEntry` runtime asset API is not replaced.

## Bounded limitations

- Quake BSP trimming and legacy collision code still use byte arrays to produce a generated result; PR-06 spools large generated results immediately, but **does not claim those upstream parsers are fully streaming**.
- `MapAssets.Validate(...checkFiles:true)` or content-specific texture decoder paths may independently allocate raw/image buffers. This PR targets the package writer, authoring dependency hash and strict digest verifier, not GPU asset decoding.
- Strict output verification still reads all ZIP bytes, because accepting a package without validating the digest is unsafe. It simply avoids holding the whole decompressed asset in memory.
- A large content payload is read twice (hash then ZIP write). Disk reads and CPU could increase versus a fully spooled one-pass implementation. A/B timing and allocation receipts must be reviewed; prefer a proven memory win with correct hashes over unverified claims of lower total wall-clock time.

## Acceptance and regression matrix

| Gate | Required proof |
|---|---|
| `tools/community-baseline-check` | Existing 1980-time ZIP golden SHA-256 **unchanged**, separately on Linux/Windows/macOS |
| `tools/map-package-stream-check` | Large loose audio assets: streaming package and historical in-memory reference produce identical raw ZIP bytes and content hash; bundle repackage parity; cancellation cannot overwrite package; sparse oversize entry refused; record elapsed and thread allocations |
| `tools/map-editor-check` | Existing generated/PK3/BSP import packages, snapshot/undo and all custom map build contracts |
| `tools/map-community-check` | Published immutable revision identities and map catalog contracts |
| `tools/map-multiplayer-check` | Host/client exact map hash, historical replay, 1v1/8-player Ready, rotation and faulted HTTP peer tests |
| `tools/community-transfer-check`, `tools/community-cache-check` | Existing durable transfers, CAS leases, downloads, hashes and evictions |
| full CI | Windows/Linux/macOS, Android build, Linux server compile and no-proprietary-assets gate |

### Local commands

```bash
dotnet run --project tools/map-package-stream-check -c Release -- --mib 8 --output artifacts/map-package-stream.json
dotnet run --project tools/community-baseline-check -c Release -- --output artifacts/community-baseline.json --stress
dotnet run --project tools/map-editor-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-multiplayer-check -c Release
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/community-cache-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

## Measurement interpretation

`tools/map-package-stream-check` compares the exact same 3 synthetic 8 MiB (24 MiB total) loose-asset inputs with the historical in-memory algorithm on the **same process and platform**. It records reported same-thread allocations and elapsed time; JIT, GC, filesystem caching and compiler generation outside the writer can influence results. Compare baseline content hash and complete ZIP bytes, not just decoded data.

Expected qualitative effect: replacing `SortedDictionary<string,byte[]>` with stream references removes the sum of large assets from retained managed allocations; the strict reader no longer allocates a ZIP-entry-sized array for content hash verification. The old and new ZIP sha256 **must match**. Do not publish “2× faster” or live Ready speed claims until actual measurements show it.

## Rollback

Revert the incremental streaming writer, source hash helper, strict-reader digest, tests and CI slice. Existing package files, installed archives, revisions and replay histories remain unchanged. Rollback needs no server changes or database migration.

## Following work

PR-07: service transport admission, zero-copy hosting/CDN integration and request metrics. Maintain separate gates for generic texture/model content v3, NetRadiant formats and optional visual layers.
