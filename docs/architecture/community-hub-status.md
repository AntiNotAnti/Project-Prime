# Project Prime Community Hub 3.0 | Integration status

Verified integration base: `AntiNotAnti/Project-Prime` main `989709d58c745178860398c36953e83e88de7470` (merged PR-07 #540, October 14, 2026).

| Slice | Repository state | Evidence and remaining work |
|---|---|---|
| PR-00 #533: frozen baseline | **Merged** | Byte identities and measured Community transfer/install baseline; Windows and Linux/macOS package golden hashes are distinct and must stay exact |
| PR-01 #534: shared Community services | **Merged** | UI-independent authenticated map facade, 401 refresh and legacy/v2 adapters |
| PR-02 #535: independent Game Community tab | **Merged** | Map discovery, favorites, verified exact map install and active download visibility |
| PR-03 #536: durable transfer jobs | **Merged** | Session snapshot ownership, authenticated restart recovery and HTTP Range resumes |
| PR-04 #537: immutable archive cache | **Merged** | SHA-256 CAS, cross-process download/read leases and quota LRU |
| PR-05 #538: streamlined installer | **Merged** `c4263122` | Bounded streamed copies, direct owned-download/leased-archive preparation, cold/warm I/O metrics, exact rollback; all 29 CI jobs passed |
| PR-06 #539: streaming package builder | **Merged** `d1b9f1c4` | Exact package-byte parity with historical writer, lower measured asset-sized allocations and strict ZIP validation |
| PR-07 #540: service admission/quota | **Merged** `989709d5` | Independent metadata/upload/download/verification lanes, full-size upload reservations, conditional catalogs and capability preflight; **29/29 exact-head CI jobs passed** |
| PR-08 #542: typed v3 content catalog | **Draft, implemented, acceptance pending** | Read-only map/texture/material/model/prefab typed DTOs; maps projected from existing v2 state, generic atomically persisted metadata; v3 catalog/permissions/restart tests succeeded on initial Linux/Windows/macOS acceptance but final post-hardening exact-head CI remains required |
| PR-09 onward | **Not started** | Authenticated typed content publishing, strict subtype validators, shared dependencies, signed distribution, NetRadiant editor integration and UI cutover |

## PR-08 safety gates

- Preserve v2 map revision owner/current/latest semantics, exact `.ppmap` bytes, protocol 53 exact map identity and historical replay retrieval; **no dual map catalog**.
- No PR-08 public write/asset bytes endpoint. A trusted local non-map metadata store is not authority to install a generic asset or bypass game/map validation. PR-09 adds that separately.
- Public typed project, revision count, parent links, update time, title/tags, creator ID and dependencies must reveal **only published data**. Private drafts and unpublished parents remain creator-visible only.
- Reject invalid kind/MIME, stable GUID, hash, parent, creator ID, visibility, field lengths, tags, dependencies and oversized asset bytes before durable index mutation.
- Keep old-v2 client fallback; unlisted/deleted maps cannot appear anonymously through the new facade.
- Test real Linux/Windows/macOS metadata/service restart, legacy v2 map publication/replay and any newly introduced schema acceptance; track exact commit SHA and do not equate pending/cancelled with passing.
- Actual Android runtime UI is not covered by a cross-platform packaging build. No measured throughput/Ready benefit is asserted for a metadata-only feature.

## Deferred architecture decisions

SQLite is optional, **not included in PR-08**. The existing atomic map/creator catalogs remain sole source of truth for v2; the small separate v3 store holds *only non-map records* and is limited by project/revision/metadata byte budgets. Introducing a second database writer for maps without demonstrated load need would add migration and rollback risk. The PR-09 typed binary upload and PR-10 dependency-lockfile work are independent gates.

PR-08 rollback removes additive v3 routes, projections, metadata registry and client DTOs without rewriting stored or replayed maps. New `content_catalog_v3.json` and `content-v3/` files can be retained for a future migration; never silently delete community creator data.
