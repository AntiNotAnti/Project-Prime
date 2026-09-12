# Project Prime hardening acceptance ledger

Status: evidence ledger, 2026-09-11. This records the current implementation
boundary for the Backend, persistent Node, and client control path. It does not
turn local tests into deployed, WAN, device, or production evidence.

## Decisions and focused evidence

| Area | Current decision / evidence | Status and boundary |
| --- | --- | --- |
| Observability | Backend stable categories/EventIds and bounded operation counters cover account, directory, admission, career, and request rejection paths. Node stable categories/EventIds and bounded counters cover sessions, directory reporting, WSS rejection, Worker placement/readiness, and map downloads. Tags are finite operation/outcome values; no IDs, tokens, payloads, or identity labels are emitted. | FOCUSED: source and focused builds; exporter/alert thresholds are deployment work. |
| Vertical control path | `tests/Backend.Tests/BackendNodeVerticalTests.cs` uses the real Backend `WebApplicationFactory` HTTP endpoints, production `AccountSession` discovery/admission calls, production `NodeControlClient` over a real Kestrel Node TLS/WSS host, and the production `NodeDirectoryReporter` contract remains covered by its Node tests. It verifies registered and guest identities remain distinct, `node.session`, lobby creation, disconnect/resume, and continued Node use after Backend shutdown. Worker/game-content is intentionally outside this seam. | FOCUSED: one local vertical test; not deployed or WAN proof. |
| PostgreSQL | Existing opt-in tests continue to use only `PRIME_TEST_POSTGRES_FILE`. All 239 Backend tests passed against an isolated local PostgreSQL 17.11 instance through that boundary, including the PostgreSQL-backed coverage. | VERIFIED LOCAL: deployed durability, grants, backup/restore, and service-operation evidence remain open. |
| Leaderboards | `tools/leaderboard-bench` recorded the current RP/career query shapes with seed `20260911`, 10 iterations, and 10,000 player/aggregate rows: RP p50/p95 `2.278/2.469 ms` (plan prefix `cfbe4f...`), career `3.066/3.365 ms` (plan prefix `850dd6...`). Both are under the `<100 ms` target; no schema, projection, cache, or index change was justified. | VERIFIED LOCAL: 100k/1m aggregate stress and production-shaped concurrency remain benchmark evidence, not deployed/load/WAN acceptance. |
| Packaged smoke | A fresh local `osx-arm64` extraction passed content identity, Node health, authenticated WSS, lobby/start, authenticated UDP, match/artifact persistence, graceful drain, and no-orphan-Worker checks. | VERIFIED LOCAL: deployed, load, and WAN packaged runs remain open. |
| Public match export | The existing `GET /v1/matches/{id}/export` route is anonymous and API-rate-limited. It returns the persisted original report (base64), scoreboard, rating receipt, and signed result receipt; missing matches use `invalid_request`/404 and an unavailable signer uses `service_busy`/503. This tranche preserves that public, read-only policy; participant display names and report fields must therefore be treated as public. A future privacy change requires an explicit policy and route-contract update. | DECIDED FROM SOURCE: no new auth or export surface was invented. |
| Capabilities | No parallel capability owner or endpoint was introduced. Existing bounded `/v1/status` and `/v1/nodes` metadata remain the canonical status/catalog surfaces; protocol-specific negotiation stays in the existing protocol/discovery contracts. | FOCUSED: source/tests; capability evolution requires a versioned contract. |
| ETag/cache | Immutable Node map artifacts use content ETag plus `public,max-age=31536000,immutable` and Range processing. Mutable directory/status responses remain uncached/no-store at the Backend boundary; no ETag/cache projection was added without a measurement. | FOCUSED: route tests; proxy/cache behavior still needs deployment validation. |
| Admission-key rotation | Backend `GET /v1/node-admission-keys` is bounded to 1..8 JWK-compatible public keys and HTTPS/no-secret semantics. Node bootstrap keys remain trusted; an unknown `kid` causes one bounded, throttled, single-flight HTTPS refresh outside replay/admission state, dynamic keys are replaced while bootstrap keys are retained, and refresh failure keeps usable keys. Redirects and `jku`/remote key hints are rejected. Keep current and overlap keys deployed together until the maximum admission lifetime plus clock skew and reconnect drain have elapsed. | FOCUSED: rotation/concurrency/security tests; actual multi-deployment overlap and key rollback remain manual. |
| Readiness/deregistration | Node readiness is the existing map-validity plus usable-Worker projection. Capacity exhaustion is not infrastructure failure. A registered Node sends the authenticated incarnation-checked deregistration hint once on ready-to-unready and graceful stop; TTL remains the fallback. | FOCUSED: source/tests; process termination and reverse-proxy delivery remain open. |

## Remaining manual/live gates

The following are intentionally not claimed by local source/build/test results:

- deployed PostgreSQL migration, grants, TLS certificate validation, backup/
  restore, and real SMTP delivery;
- production HTTPS termination and trusted-proxy address behavior;
- Windows DPAPI, macOS Keychain, Android Keystore, and physical client
  reconnect/resume acceptance;
- geographic/WAN TLS and outage/failover runs, including the 20-cycle policy;
- deployed/load/WAN packaged Node + Worker runs with AMHE1 map content,
  map-range resume, and worker placement/reporting under load;
- production key rollout/overlap, unknown-`kid` flood telemetry, and rollback;
- independent privacy/legal review of the currently public match-export payload;
- production-shaped/concurrent PostgreSQL leaderboard runs and any resulting
  query/index decision beyond the recorded isolated benchmark.

Use `docs/CURRENT_RELEASE_GATES.md` for the broader release-gate ledger and
`docs/architecture/LEADERBOARD_QUERY_BENCHMARK.md` for the reproducible query
measurement procedure. Focused tests are evidence of the exercised seams only;
they are not a claim of physical platform or deployed-network acceptance.
