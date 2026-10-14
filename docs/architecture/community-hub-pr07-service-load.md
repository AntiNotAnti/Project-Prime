# Project Prime Community Hub 3.0: PR-07 Service Load Isolation & Upload Preflight

**Reviewed baseline:** `AntiNotAnti/Project-Prime` main `b24f8dccebf18c23954816de3bc753856b3cdf6f`, including merged streaming-package PR #539.  
**Related in-flight PRs:** #532, #531–#522 (scene, AGX, OpenGL) and #474–#472 (timed-pickup netcode). None of the open branches inspected at PR start replaces the Community HttpListener, creator catalog, or upload storage accounting.

## Root causes addressed

The previous server admitted at most eight in-flight requests through a single `SemaphoreSlim(8)`, and held that slot until the entire handler completed. Eight slow PUT streams or large archive GETs could block catalog browsing, login/identity operations and health checks. Upload starts rescanned all partial metadata and PUTs rescanned every `upload-*.part` to estimate quota use, which was both O(upload sessions) on every chunk and not an atomic reservation. The legacy one-body `POST /maps` held the global publication lock while receiving a potentially long HTTP body.

## Implemented service boundaries

- **Metadata lane:** eight slots for catalog browsing, creator/auth commands, capabilities and health, independent from transfer capacity.
- **Creator upload lane:** three slots for sequential `PUT /uploads/{sha256}`. Excess upload work responds immediately with HTTP 503 and `Retry-After: 1`, never queueing indefinitely.
- **Player download lane:** 16 independent slots for immutable `GET /packages/{sha256}`/`GET /maps/{sha256}` and bounded preview reads. Saturated creator uploads therefore cannot deny admission to player map downloads/prewarming.
- **Verification lane:** one slot for `POST /uploads/{sha256}/complete` and legacy one-shot archive publication `POST /maps[/.../versions]`; separate from catalog, upload and download traffic.
- **Publication lock:** still serializes final catalog/immutable package registration and lifecycle changes. A legacy POST now acquires it **after** receiving the archive bytes, not while waiting on the network.
- **Per-peer request rate:** configurable with `PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE` (60–100000); uses actual peer socket IP, never untrusted `X-Forwarded-For`. **Disabled by default** because many Cloudflare/reverse-proxy visitors may share one origin peer IP. Metadata/transfer/verification lanes have separate bounded windows.
- **Moderator diagnostics:** `GET /v2/metrics` is protected by existing moderator authentication and reports admitted, active, completed and rejected counts by lane, aggregate verified download bytes, quotas and published storage. It never exposes bearer tokens, peer addresses, private map lists or individual creator identities.

The server's per-request linked cancellation and timeout remain. Active handlers drain on shutdown and release lane slots. Admission/rejection counters are bounded, in-memory diagnostics and reset on restart.

## Upload quotas and storage accounting

- On each new sequential upload session, reserve its full declared package length, not only bytes already written. Per-creator budgets: at most **8 sessions and 1 GiB reserved**; service total: **16 sessions and 2 GiB reserved**. Size guard remains 512 MiB per archive, 50 MiB per upload PUT, with strict sequential offsets.
- A small start/PUT disk-budget check requires enough actual free volume space for the upcoming bounded chunk. If storage is exhausted, respond 507 and preserve the existing upload session/offset.
- Delete/publish completion releases reservations; stale sessions are cleaned with the existing 24-hour retention. At service startup, reconcile retained `upload-*.json` metadata, count session reservations, and account for orphaned `.part` files before accepting new uploads.
- No more `upload-*.part` directory rescan on every chunk; the per-session lock and reserved full length make each append safe within its budget.
- Published archive byte accounting starts from the verified catalog and increments on new immutable publication, decrements on actual lifecycle purge; the 50 GiB default and `PROJECT_PRIME_MAP_STORAGE_GIB` override remain.
- Existing owner/revision optimistic concurrency, retained conflict partials, immutable archive identity and collaborator rules are unchanged.

Quota reservation tracking is in-memory but startup reconstructs it from the existing metadata files, so no new storage schema or migration is needed. The external filesystem may still run out of space under unrelated processes; I/O error handling remains mandatory.

## Additive API improvements (without generic v3 content migration)

| Endpoint | Behavior |
|---|---|
| `GET /v3/capabilities` | **Only** capability negotiation is v3 in PR-07. Advertises existing v2 compatibility, supported `map` kind, SHA-256/package bounds, 50 MiB maximum, **4 MiB recommended trial** upload chunk, Range/sequential-offset support, preview size and service concurrency; parallel upload and generic v3 content remain disabled |
| `GET /v2/maps[?page=n&pageSize=n]` | Existing opt-in paging retained (maximum 100/page); adds `X-Total-Count`, ETag and `If-None-Match` handling |
| `GET /maps[?page=n&pageSize=n]` | Legacy response stays an array; adds matching `X-Total-Count` and conditional ETag behavior |
| `GET /v2/maps/{mapId}[ /revisions]` | Existing authorization rules; stable ETag over the **specific viewer's** permitted JSON |
| `GET /maps/{sha256}/preview` or `/packages/{sha256}/preview` | Presentation-only embedded PNG, maximum 1 MiB uncompressed; 404 if no preview, 413 if oversized. Published previews can be cached briefly; draft/private previews require existing publisher authorization |
| `GET /packages/{sha256}` | Existing Range/206/416 behavior stays unchanged; adds immutable-blob ETag/conditional 304 and suitable private/public cache policy |
| `GET /v2/metrics` | Moderator only, no private creator data exposed |

Existing endpoints and clients do not need to call v3. New `MapCommunityClient.GetCapabilitiesAsync` treats a 404 as a supported legacy service, and does **not** automatically alter proven 50 MiB upload chunk sizes, download URLs, or client retries without separate benchmarks. No CDN or generic texture/model endpoints are introduced in PR-07.

## Automated evidence and gates

`tools/community-service-check` creates an asset-free loopback HttpListener and exercises real service startup, v3 capability document, preview bytes, legacy package/modern catalog ETags, authenticated moderation access, sequential 512 MiB quota reservations, a full service restart with durable reservations, quota release on upload deletion, **three intentionally stalled PUT request bodies** alongside responsive health/catalog requests **and a successful concurrent immutable player download**, with a fourth upload refused using Retry-After, and optional per-peer rate rejection.

The CI matrix runs this test on Linux, Windows and macOS alongside the existing immutable map goldens, streaming ZIP parity, map-editor check, `map-community-check`, `map-multiplayer-check`, community transfer/cache acceptance, and dedicated server compile/Android release gates.

**Performance baseline:** The pre-PR-07 server had one 8-slot shared gate; the new test checks that the catalog **and a separate exact package download** stay responsive with *all three creator upload slots actively stalled*. This is a live server backpressure test, **not** an internet latency or large-CDN throughput measurement. Real network campaigns with multi-hundred-MiB maps and several creators should be measured separately before claiming upload speedups.

### Review commands

```bash
dotnet run --project tools/community-service-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-multiplayer-check -c Release
dotnet run --project tools/community-baseline-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

## Security and backward compatibility

The existing creator bearer-ticket, draft/unlisted visibility, revision parent hashing, Range, PK3/map package limits and protocol identity remain. Cache-Control for creator-personalized catalog responses is private; ETags are calculated over the already authorized JSON. Package SHA-256 and historical replay references are unchanged, and the lobby Ready barrier still requires exact package validation and private build/prewarm.

The preview handler never treats display bytes as a source of gameplay trust. No hash/metadata shortcuts are introduced to the existing `.ppmap` validation. The service still needs a trusted HTTPS reverse proxy in public deployment.

## Rollback

Revert PR-07's admission class, reconciled quota ledger, capability endpoints, optional client support, metrics/ETag/preview additions, new acceptance harness and CI step. Existing `.ppmap` archives, `upload-*.json` sessions, content hashes and v2 requests continue to work under the old HttpListener handling path, without data migration.

## Explicit future slices

PR-08 introduces generic typed content and proper `v3/content` routes, not this PR. CDN/object-storage proxying, signed draft URLs and dependency lockfiles also remain separately gated. If needed, measure additional streamed preview transport/caching after this slice rather than adding a second service implementation.
