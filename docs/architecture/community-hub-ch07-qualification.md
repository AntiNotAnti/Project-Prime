# CH-07 | Community benchmark and release-hardening gates

**Stack:** CH-02 PR #602 -> CH-07 qualification tooling. After CH-01/03/04/05/06 PRs merge, run the same workflow on the integrated exact commit. No changes to immutable map identities, multiplayer packets or replay formats.

## Automated matrix

The dedicated `community-qualification.yml` workflow runs asset-free on Windows, Linux and macOS. It exercises the full service/transfer/cache/CDN/revision/multiplayer suite and publishes per-run JSON from:

- `tools/community-qualification`: compares the original linear-scan favorites algorithm with the new indexed implementation at 100/500/2000 projects (2k/5k/10k favorite records), alternating execution order, recording p50/p95 and same-thread allocations. Each iteration validates exact favorite count/membership parity.
- Real local `MapCommunityServer` + `MapCommunityClient` catalog, publication and exact package reads. Loopback p50/p95 and the exact SHA-256 package hash are recorded, not estimated internet throughput.
- Existing `map-install-io-benchmark`: matched old/new archive-copy allocation and time receipts.

The workflow must be run again after merging the Community slices, not only on CH-07's development branch. CI queued/running/cancelled is not passing; attach each exact-head run URL and machine-readable artifacts to release evidence.

## Acceptance checklist

| Gate | Requirement | State |
|---|---|---|
| Exact package SHA-256 / MapId / content hash | Unchanged cross-platform golden and roundtrip | PENDING final-head CI |
| 100/500/2000 project favorite catalog | Exact parity + p50/p95 and allocations on matched OS | PENDING CI |
| Resumable 32/128/512 MiB package transfers | Cancel/restart/Range, corrupted input repair, per-creator upload quotas | PENDING staged network |
| Cross-process Game + Studio | Exclusive job leases, remote Pause/Resume/Cancel, crash adoption | PENDING final-head CI and manual |
| Anonymous/owner API scope | Drafts/archived private, old v2 route compatible, no cross-user auth headers | PENDING final-head CI |
| CDN and mirror quota | Trusted HTTPS only; origin fallback; corrupt/stale mirror rejected | PENDING staging CDN |
| Lobby readiness/rotation | One and eight players, spectator/disconnect, no early Ready | PENDING live gameplay |
| Historical replay | Archived old exact package playback/export in actual Game | PENDING device |
| Android arm64 | Slow network, 512 MiB cache budget, storage pressure, touch nav | PENDING physical device |
| Desktop creator and NetRadiant | Launch/return, revised visibility, conflicts, saves and editor bridge | PENDING Windows/macOS/Linux GUI |
| Visual browsing | Fast selected preview changes, missing/oversized PNG, 830x390 and controller navigation | PENDING hardware UI |

Do not mark an item PASS based on compilation alone.

## Rollback
All changes remain in separately reviewable PRs. If a network/storage/creator change fails hardware testing, revert that PR or toggle its existing supported feature switch; keep immutable archive data, old v2 endpoints and historical replay retention. Never purge shared creator data automatically.

## Benchmark interpretation
A performance comparison is valid only with the same machine, package hash, concurrency and server/CDN configuration. Loopback and synthetic favorite-list results establish code-path effects; they cannot predict end-to-end join-to-Ready, frame-time or live CDN throughput. No fixed universal speedup threshold is asserted.
