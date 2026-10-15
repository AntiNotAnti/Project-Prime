# Project Prime Community Hub 3.0 | Integration status

Historical PR-07 integration base: `AntiNotAnti/Project-Prime` main `989709d58c745178860398c36953e83e88de7470`. Current integration inventory reconciled against `f17a5de74d82f6b201b90ef7017414108e1a6629` on October 14, 2026. PR-00 through PR-14 and activation #559 are merged. These services are implementation dependencies: **do not reimplement them for Studio**.

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
| PR-10 #546: optional exact-hash visual dependencies | **Merged** | Optional immutable visual lockfiles; complete HD rendering remains separate |
| PR-11 #547: object-store/CDN | **Merged** | Verified public-map mirror; default on in source after #559, trusted HTTPS CDN still requires configuration; staging pending |
| PR-12 #549: external NetRadiant bridge | **Merged** | Existing gamepack/compiler bridge; external fork GUI/platform certification pending |
| PR-13 #551: editable .map interchange | **Merged** | Bounded brush/UV/patchDef2/entity subset; broad hybrid workflow and real tool checks remain separate |
| PR-14 #556: release/cutover preparation | **Merged** | Frozen publish intents, coordinated receipt index, per-job leases and crash adoption; production/device acceptance pending |
| Activation #559 | **Merged** | Creator workspace and public mirror default on; external editor preference conditional on configured valid executable; explicit rollback flags retained |

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

- [Feature activation](community-hub-feature-activation.md) and `CommunityCutoverPolicy` define current defaults. The PR-14 cutover document retains its historical implementation evidence, not present-tense off/default status.
- Creator workspace and public-map mirroring are default on. Set `PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW=0` or `PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED=0` for explicit rollback. Leaving variables unset enables these features.
- `PROJECT_PRIME_NETRADIANT_DEFAULT=0` disables the configured external editor preference. A valid explicitly configured `PROJECT_PRIME_NETRADIANT_EXE` is still required. This is not certification of a bundled Radiant product or permission to overwrite existing native projects.
- Unconfigured trusted CDN hosts retain authenticated origin downloads. Draft/unlisted data stays private under default mirroring.
- Actual external-editor, device, live multiplayer, historical replay and paired-update acceptance remain **PENDING** until executable evidence is recorded. Source merge, automated tests, manual acceptance and deployment are separate statuses.
- [Studio/Radiant implementation ledger](studio-radiant-implementation.md) tracks the new ST/RD/MAP/REL work. Campaign CAMP-01..07 now on main are regression boundaries and must not be transplanted from the Studio backup.
