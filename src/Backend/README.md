# Backend account and ticket foundation

This net10.0 service references Game only. It currently provides registered identity, profile/license identity fields, confirmation, bearer login/refresh and signed server-bound game tickets. It does **not** implement career aggregates, Ranking Points, match submission, outbox ingestion or leaderboards. Public license responses intentionally contain no placeholder RP/statistics.

## Configuration

Use deployment environment variables or an operator-managed secret provider. No credentials or signing keys belong in appsettings, source control, command-line arguments, logs or demos.

| Environment variable | Meaning |
|---|---|
| `ConnectionStrings__Backend` | PostgreSQL connection string; required; use a least-privileged application role |
| `Accounts__RequireConfirmedEmail` | Defaults true; false enables private testing login only, never unconfirmed game tickets |
| `Accounts__DataProtectionKeyPath` | Durable Identity token/confirmation key directory, required outside Development/Testing |
| `Email__Host`, `Email__Port`, `Email__Sender` | SMTP delivery; port defaults587, TLS is required |
| `Email__Username`, `Email__Password` | Optional SMTP authentication from secret storage |
| `Tickets__Issuer` | Exact HTTPS issuer, ASCII, maximum128 characters; no credentials/query/fragment |
| `Tickets__KeyId` | Current signing key ID,1–32 ASCII letters/digits/underscore/hyphen |
| `Tickets__SigningKeyPemPath` | Operator-owned P-256 private PEM file; never returned to clients/servers |
| `Tickets__PreviousKeys__0__KeyId`, `...__PublicKeyPemPath` | Optional previous verification public keys during rotation |
| `GameServers__Servers__0__Id` | Provisioned server UUID |
| `GameServers__Servers__0__Enabled` | Must explicitly be true |
| `GameServers__Servers__0__PublicAddress` | Operator-provisioned canonical IPv4 address reached by account clients; no hostname or wildcard |
| `GameServers__Servers__0__PublicPort` | Matching externally reachable UDP port, 1..65535 |
| `GameServers__Servers__0__ApiKeySha256` | SHA256 hex digest of a separately generated high-entropy server credential |

Use distinct per-server secrets (at least32 characters of cryptographic entropy), and deliver the plaintext only to that server. A length check is not an entropy guarantee. Changing server configuration requires restarting this initial service. Server credentials authenticate incarnation registration only; they do not grant account access, ticket signing or official rating authority.

Persist and protect the Data Protection directory and private PEM with owner-only access and deployment storage protection. Losing Identity keys invalidates existing tokens; losing the ticket private key requires deliberate key rotation. This service does not provision secrets or choose a machine-wide key location in production. Development uses framework defaults; tests explicitly use ephemeral keys and delete temporary signing files.

Production rejects HTTP. Terminate TLS at Kestrel or explicitly configure and audit trusted proxy forwarding before deployment; this slice does not trust arbitrary forwarded headers. Do not expose a Development/Testing environment publicly. Request bodies are limited to16KiB at Kestrel; known oversized bodies are also rejected before binding. Authentication endpoints allow20 requests/minute per remote IP; read/profile APIs120; the service has a600/minute global cap. Queues do not accumulate. Reverse-proxy address/rate-limit tuning remains deployment work.

## API

- `POST /v1/auth/register` `{email,password,displayName}` returns201 `{playerId,confirmationRequired}`. Email is unique, at most254 characters; password is12–256 characters with Identity's default complexity; display name is1–16 trimmed printable ASCII characters. Account/profile/license rows commit together. A duplicate returns409 without partial rows.
- `POST /v1/auth/login` `{email,password}` returns ASP.NET Identity's `{tokenType,accessToken,expiresIn,refreshToken}`. These are framework bearer tokens, not JWTs. Unknown/wrong-password/unconfirmed/locked login returns401. Five failed attempts lock an account for15 minutes.
- `POST /v1/auth/refresh` `{refreshToken}` checks expiry, security stamp, confirmation policy and lockout. Access lasts10 minutes, refresh seven days.
- `POST /v1/auth/confirm-email` `{playerId,code}` consumes the Identity-generated code delivered by email. `POST /v1/auth/resend-confirmation` `{email}` returns202 without revealing whether an account exists. Delivery must be configured; no confirmation codes are returned by registration or printed to logs.
- `POST /v1/auth/revoke-sessions` requires account authentication and invalidates refresh tokens via the security stamp. Already-issued access tokens expire normally; ticket issuance additionally checks the current stamp. This is not instant revocation of every profile/read access token.
- `GET /v1/me` requires account authentication and returns `{playerId,emailConfirmed,emailEligibleForOfficialPlay}`. Email eligibility alone is not server/match/rating authorization.
- `PATCH /v1/me/profile` `{displayName?,favoriteHunter?}` updates only the authenticated owner. Favorite Hunter is numeric0–6. Unknown properties, including supplied ownership/RP fields, are rejected.
- `GET /v1/players/{id}/license` returns only `{playerId,displayName,favoriteHunter,joinedAt}`. No account email/password metadata is exposed.

With confirmation required but SMTP absent, registration returns503 before writing anything. SMTP failure after commit leaves a real unconfirmed account; use resend after delivery recovers rather than creating another identity. Email delivery is synchronous after the database transaction, not a durable email outbox. Password reset/change and2FA management UI/endpoints are not included yet; do not offer UI for absent routes.

## Game ticket integration

`PUT /v1/server/session` uses `X-Server-Id: <D-format UUID>` and `Authorization: Bearer <server secret>`, with `{serverIncarnation}`. A valid configured server registers its current startup UUID and receives `{serverId,serverIncarnation}`. This memory registry is empty after Backend restart; servers must re-register. A process must never reuse its previous startup incarnation. Only one active incarnation per configured ServerId is supported.

`POST /v1/game-tickets` uses account bearer authentication and `{serverId,nonce}` where nonce is a canonical nonzero unsigned64 decimal **string**. It returns `{ticket,expiresAt,serverId,serverIncarnation}`. The account must have confirmed email, a current security stamp and no lockout even when private-test login is enabled. Unknown/unregistered server returns404; absent signing configuration returns503.

Tickets are compact ES256 JWTs with `kid`, `iss`, `aud`=server UUID, `sub`=PlayerId UUID, `sid`=server startup UUID, `jti`=unique ticket UUID, `nonce`=decimal string, `name`=authoritative display name, and numeric `iat`/`nbf`/`exp`. Lifetime is exactly120 seconds. Issuance enforces the current Game `JoinPacket.MaxTicketBytes` join-carrier cap. An optional signed boolean `observerTrusted:true` is emitted only for PlayerIds in the operator-configured `Tickets__TrustedObservers__0` (and subsequent indexed entries) allowlist. It is never accepted from a player request. Tickets do not contain account tokens, email, RP or trust-class claims. Server validation must enforce signature/algorithm/issuer/audience/incarnation/lifetime/name/nonce and owner-thread single-use admission; Backend issuance alone does not establish replay-safe UDP authentication.

`GET /v1/game-ticket-keys` returns standard public `{keys:[{kty,crv,x,y,kid,use,alg}]}` including configured previous keys. Retain previous keys for at least the ticket lifetime plus verifier clock skew after the last ticket signed with that key, and coordinate server refresh (currently planned60s TTL/unknown-kid refresh). No automatic remote key URL is taken from a JWT header. Disabling a server/replacing its session does not revoke already-issued tickets at another running instance; the server must enforce its actual incarnation and shutdown/credential policy.

## Explicit migrations and validation

The checked-in `Data/Migrations/InitialAccounts` creates Identity plus profile/license tables. Startup never calls `Migrate` or `EnsureCreated`. Migration tooling uses `BackendDesignTimeFactory` and the same environment connection-string key. Model construction and script generation do not connect to PostgreSQL.

With a compatible10.x `dotnet-ef` tool, generate/review an idempotent SQL script or migration bundle, then apply it separately with a migration role. Do not apply a migration against a shared database as part of a normal build/test. Example (writes a script only):

```sh
dotnet ef migrations script --idempotent --project src/Backend --output /tmp/prime-backend-migration.sql
```

`dotnet test tests/Backend.Tests/Backend.Tests.csproj` uses ephemeral SQLite and an in-process HTTP host, captures email only in test doubles, and validates PostgreSQL migration SQL/model offline. This is not PostgreSQL execution, deployed TLS, SMTP delivery, or native-client credential-storage proof. Before deployment, run migrations and account uniqueness/transaction/restart checks against disposable PostgreSQL, verify TLS/SMTP and durable protected keys, and complete server/client admission integration. No database or service is started by these tests.

Package/API basis: [ASP.NET Identity APIs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [Identity framework endpoint source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs), [Npgsql EF10](https://www.npgsql.org/efcore/release-notes/10.0.html), [EF migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying). Password/token cryptography stays in established framework components; JWT signing is Backend-owned and Game has no cryptographic/package dependency.


## Match ledger and career queries

`POST /v1/server/matches` authenticates the provisioned server credential (`Authorization: Bearer`, `X-Server-Id`). Configure `GameServers__Servers__0__TrustClass` explicitly; it defaults to Community. The Backend derives effective trust from that authenticated registration, independently of the raw report claim. It stores that effective trust with the accepted ledger row and uses the stored value during rebuild. Old startup incarnations remain accepted so a retained spool can drain after restart; active join-ticket session registration is independent.

The endpoint requires `Idempotency-Key` (match UUID), `X-Content-SHA256` (uppercase SHA-256 of the exact body), and the default System.Text.Json serialized `MatchReportV1` body, bounded to512KiB. It stores the original bytes. A first acceptance returns201, the same UUID/hash returns200, and a conflicting UUID/body returns409. Receipt fields are `matchId`, `payloadHash`, `processingOrder`, `ratingStatus:"policyPending"`, and `rating:null`. No RP calculation, initial rating, rating delta, or star rank is fabricated.

One PostgreSQL transaction takes a shared rebuild barrier, a UUID-derived report advisory lock, and all affected license rows in canonical PlayerId order. It then inserts the ledger row (allocating processing order after player locks), records participant history, and updates projections. Any failure rolls back all changes. A future approved rating policy belongs immediately after these row locks, reading current balances and persisting its input/output facts in this transaction; no speculative rating processor or reservation exists today. Gaps in the sequence after rollback are valid. Disjoint participants may commit independently; overlapping participants always replay in their mutation order.

`CareerRebuild.RebuildAsync` is an explicit operator service, with no public HTTP route or startup invocation. It takes the exclusive rebuild barrier, verifies raw hashes/identities, clears only derived projections, and replays accepted reports in processing order in one transaction. Never rebuild RP from these career projections; an approved rating ledger will need its own immutable policy inputs.

Public additive queries (existing license/identity response fields are unchanged):

- `GET /v1/players/{id}/career`: official totals combine VerifiedCasual and Ranked. Optional `trustClass` selects one separate bucket, including Community or Private. Tournament is explicit, not silently included. Forced/InvalidTeams and Practice reports remain in history but do not add career totals.
- `GET /v1/players/{id}/matches?limit=25&before=<processingOrder>`: descending acceptance order, up to100 entries, `nextCursor` is the last processing order when another page exists. This is recorded processing order rather than wall-clock arrival/finish order.
- `GET /v1/leaderboards/career?metric=kills&limit=25&cursor=<opaque>`: allowlisted metrics are kills, wins, kd, winPercentage, headshots, octolithScores, nodesCaptured, killsAsPrime and rp. The same optional trust filter applies. Optional `hunter` selects attributable per-hunter categories. RP returns an empty list with `ratingStatus:policyPending`. Ordering is descending score then ascending PlayerId; cursors bind the selected metric/scope/hunter. Ratio board scores round to six decimal places before both sorting and cursor comparison. K/D requires nonzero deaths and ten attributable games; winPercentage requires ten attributable outcomes.

Career includes wins/losses/ties, ratios, combat totals, play ticks, streaks, and derived favorites. K/D is null with zero deaths; win ratio is null without games. Form-kill counters stay null if any historical report lacks that telemetry. Selected favorite Hunter remains in the identity profile; most-played Hunter is derived from actual span play ticks. Most-played map/mode/hunter use play ticks, favorite weapon uses kills. Best map and best Hunter use win ratio with ten attributable matches, ties broken by sample count then ordinal key. A participant changing Hunter within a match contributes measured play time to each Hunter, but cumulative combat/outcome facts cannot be divided across those Hunters: best-Hunter/leaderboard combat samples include only single-Hunter matches and expose `attributedMatches`. Guests and bots never receive account aggregates; registered-player counters remain attributed only to the authenticated report's PlayerIds. These are server-authoritative reported facts, not client self-reported statistics.

## PostgreSQL integration tests

Set `PRIME_TEST_POSTGRES_FILE` to a private file containing the connection string for an explicitly disposable local PostgreSQL instance. Each PostgreSQL test creates a unique schema, applies actual migrations, and drops only that schema afterward. Without this variable those tests are explicitly skipped. Tests cover concurrent registration, overlapping-player reports, same-body idempotency, conflicts, raw-body preservation, rollback, rebuild equality, trust separation, malformed facts, and actual PostgreSQL leaderboard/keyset translation. SQLite covers focused HTTP/auth boundaries; it is not a production persistence substitute.


Ticket responses include `publicAddress` and `publicPort` from operator registration, never from UDP status or server session requests. Account clients must resolve the selected destination once, require an exact match with this authenticated HTTPS response, and send the ticket only to that pinned IPv4 literal and port. Otherwise an impostor could advertise another server UUID and steal a valid ticket for relay. Endpoint mismatch fails the join; it must not silently downgrade an account join to guest. This binding does not claim protection against an on-path UDP attacker. Private/LAN and loopback literals are permitted for explicitly configured private deployments; production uses the externally reachable NAT address/port. Omitting both endpoint fields preserves report-only registration but disables game-ticket issuance (404); partial or malformed endpoint configuration fails startup. Changing an endpoint requires operator configuration/restart. The response endpoint is HTTPS-authenticated routing metadata and does not enlarge the UDP JWT.


Report duration validation follows the simulation's signed tick half-range: at most `int.MaxValue` 60Hz ticks, with corresponding UTC duration and mode-time bounds. Unlimited matches longer than one day remain admissible. Per-slot and overall participation intervals must remain within that range even when uint ticks wrap; body and participant limits are unchanged. Known biped/alt totals and the sum of beam kill buckets cannot exceed total kills; unavailable nullable historical form metrics remain accepted.
