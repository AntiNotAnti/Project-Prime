# Project Prime Community Hub 3.0 | Integration status

Historical PR-07 integration base: `AntiNotAnti/Project-Prime` main `989709d58c745178860398c36953e83e88de7470`. Current integration inventory verified on October 14, 2026; later main commits exist. All unmerged slices remain stacked and drafts.

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
| PR-08 #542: typed v3 content catalog | **Merged** | Additive typed read model and immutable v2 map projection; historical acceptance details below |
| PR-09 #545: typed asset publishing | **Merged** | Authenticated asset publishing and sandboxed content validation; game/map identities unchanged |
| PR-10 #546: optional exact-hash visual dependencies | **Open draft**, main base | Immutable lockfile/optional-HD and dedup design; must merge before descendants |
| PR-11 #547: object-store/CDN | **Open draft**, PR-10 base | Optional immutable mirror and origin fallback, off by default; staging testing deferred |
| PR-12 #549: external NetRadiant bridge | **Open draft**, PR-11 base | Original gamepack and optional external q3map2 bridge; actual fork license/build + GUI not accepted |
| PR-13 #551: editable .map interchange | **Open draft**, PR-12 base, head `7a2ea3d` | Supported brush/UV/patchDef2/entity roundtrip, 56 focused checks + 12 real package checks, new cross-platform CI reported green; real editor/gameplay testing deferred |
| PR-14: release/cutover preparation | **Staged implementation branch**, PR-13 base | Opt-in independent creator workspace, frozen durable publish handoff, shared transfer session, rollback switch and release matrix; exact-head automated CI and manual acceptance outstanding |

## Historical PR-08 safety gates

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

## Current rollout discipline (October 14, 2026)

- PR-10 -> PR-11 -> PR-12 -> PR-13 -> PR-14 is a **dependency stack**.
  Do not merge out of order or assume an earlier draft was retargeted to main.
- The end-user requested that real NetRadiant, gameplay and device testing
  be done later. Automated CI may still run; these manual test rows remain
  **DEFERRED**, not green.
- PR-14 introduces `PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW=1` as an
  **off-by-default** Studio creator preview. Keeping it unset retains the
  existing creator dashboard and advanced revision controls. It does not
  toggle the player's Game Community route or change the default editor.
- The PR-14 release/cutover matrix, version-unassigned candidate notes,
  operator rollback and explicit approval gates are in
  [community-hub-pr14-cutover.md](community-hub-pr14-cutover.md).
- No phase is a claim of actual production deployment, Android GUI
  acceptance, final replay certification or live-load improvement.
