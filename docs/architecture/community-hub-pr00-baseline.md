# Community Hub 3.0: PR-00 baseline and compatibility ledger

**Repository:** AntiNotAnti/Project-Prime  
**Reviewed main SHA:** 16b1d89bfb6ed7776b801b3e39d513e740ee5e58 (2026-10-13)  
**Change type:** tests and documentation only. No runtime, protocol, persistent data, or HTTP changes.

## Current ownership map

| Responsibility | Production owner | Planned extraction |
|---|---|---|
| Browse/publish/download and HTTPS validation | `MapCommunityClient` | UI-neutral facade over same client |
| Hunter License ticket/401 refresh | `MapStudioSharing.WithCommunityAuthentication` | Injected credential supplier |
| Creator revisions and rollback | `MapCommunityRevisionCatalog` | Unchanged MapId-specific v2 adapter |
| Upload sessions, archive transport | `MapCommunityServer` | Unchanged for PR-00/PR-01 |
| Community dashboard | `MapStudioCommunityDashboard` nested in `MapStudioScreen` | Independent route after PR-02 |
| Build/package | `MapBuildScheduler`, `MapPackageBuilder` | Preserve strict builder and byte identity |
| Install/runtime ownership | `MapPackageInstaller`, `MapRuntimeUsage` | Preserve staging/atomic publication |
| Host rotation and Ready | `HostedMapRequests`, `LobbyMapPreparation` | Remains authoritative |

Current main constructs the embedded Map Studio via `PrimeRoute.Forge` and Avalonia. Historical Studio PR #367 was merged but cannot be assumed to own the active UI; historical RmlUi PR #424 is closed unmerged.

## Overlapping open PR review

At the baseline read the open PRs were #472-#474 (Netcode 2.1 pickup authority), #522/#524/#526/#532 (scene lifecycle), #523/#527/#528 (AGX allocations), and #525/#529-#531 (OpenGL optimization). No open PR in this inventory directly replaced the Community client, server routes or Studio dashboard. Recheck immediately before code changes because scene/network changes affect acceptance fixtures.

## Existing endpoint inventory (compatible)

- `GET /health`, `GET /maps`, `GET /maps/<sha256>`, `GET /packages/<sha256>` (immutable archive/Range).
- `GET /v2/maps`, `GET /v2/maps/{mapId}`, `GET /v2/maps/{mapId}/revisions`.
- `POST/GET/PUT/DELETE /uploads/<sha256>` and `POST /uploads/<sha256>/complete`.
- Existing v2 promotion/archive/restore/delete/purge; favorites, reports and collaborators.
- Legacy one-body `POST /maps` remains for older clients.

MapId, content SHA-256 and package SHA-256 are distinct. Published revisions are immutable; catalog revision numbering and visibility do not authorize rewriting historical bytes. `NetworkMapIdentity` exactness and replay bootstrap must remain intact. Current package constraints: 512 MiB compressed, 1 GiB expanded, 256 MiB/entry, 32 MiB project JSON, 2,048 entries. Current upload chunks are at most 50 MiB, resumable at a single offset.

## Baseline harness

`tools/community-baseline-check` generates a deterministic GUID-stable, asset-free package and measures two builds, hash, strict reader validation, loopback upload, v2 catalog, exact download, prepare/build/commit and installed identity lookup. Report has archive SHA, size, protocol, process peak working set, timings and limitations. `--stress` checks the over-512 MiB size fence using a sparse file, **not** near-limit valid-archive performance.

The source GUIDs and byte content are frozen. Current CI showed different exact package bytes for the identical source across platforms: **Linux/macOS** `ddf15bed5b8bb0f3ca8a3521a69be8bd3afcdf9f2fe469998f3f68e2d063a17e`; **Windows** `51a4cba62497f756e1d9d176f4e17ce1f343713021489969f89a6d93cf430b20` (observed on two Windows runs). Each platform reproduces its own hash. Store these as two required platform golden files. **Cross-platform byte parity is NOT established**, and the exact cause of the difference is unverified. These facts must not be described as a universal deterministic package hash or silently normalized. The exact SHA-256 of the archive downloaded remains the authoritative multiplayer identity. Capture `canonicalContentHash` on each platform to narrow whether serialization or compression differs; follow up as an independent change with clear package-hash migration and replay compatibility. No historical archive is rewritten.

To run:

```bash
dotnet run --project tools/community-baseline-check -c Release -- --output artifacts/community-baseline.json --stress
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet run --project tools/map-multiplayer-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

Existing Community suites cover map revisions, ownership, rollback and HTTP error cases. Multiplayer acceptance covers exact package preparation, rotations and historical replay lookup. CI adds this harness to the existing map-editor matrix and uploads JSON results without replacing those tests.

## Measurement interpretation and gates

| Requirement | Required evidence |
|---|---|
| Deterministic frozen package and SHA-256 identity | Byte-for-byte repeat, strict reader, later pinned golden |
| Backward-compatible Community revisions | `map-community-check` and `map-editor-check --community-only` |
| Host rotation/replay recovery | Existing `map-multiplayer-check` |
| No runtime behavior changes | PR-00 changes only tools/workflow/docs |
| Comparable initial stage timings | Per-platform JSON, compare only equivalent environments |
| Real cold/warm lobby Ready and near-limit valid archive | **NOT_RUN by this harness**. Require separate campaigns and disk budgets |
| Android hardware and public server behavior | **NOT_RUN by this loopback harness** |

Loopback results include JIT and warm-cache effects; prepare/install is not an authoritative live-lobby Ready measurement. Peak process working set is not isolated managed allocation.

Rollback: remove this test/CI/docs without affecting game files. PR-01 may only proceed after reviewed baseline tests; PR-02 follows facade acceptance.
