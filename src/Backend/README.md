# Backend account and ticket foundation

This net10.0 service references Game only. It provides registered identity, profile/license fields, confirmation, bearer login/refresh, signed server-bound game tickets, authenticated match submission, immutable career/rating projections, and read-only career leaderboards. Public license responses contain server-authoritative Ranking Points and tier data.

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
| `GameServers__Servers__0__Id` | Provisioned Node UUID |
| `GameServers__Servers__0__Enabled` | Must explicitly be true |
| `GameServers__Servers__0__ApiKeySha256` | SHA256 hex digest of a separately generated high-entropy server credential |
| `GameServers__Servers__0__TrustClass` | Backend-assigned community/verified reporting class; Nodes cannot self-assert it |
| `Backend__AllowRemoteHttp` | Temporary Development-only plain HTTP for `51.161.113.128`; keep false elsewhere |

Use distinct per-Node secrets (at least32 characters of cryptographic entropy), and deliver the plaintext only to that Node. A length check is not an entropy guarantee. Changing server configuration requires restarting this initial service. Node credentials authenticate registration, heartbeat and report ingestion; they do not grant account access, ticket signing or official rating authority.

Persist and protect the Data Protection directory and private PEM with owner-only access and deployment storage protection. Losing Identity keys invalidates existing tokens; losing the ticket private key requires deliberate key rotation. This service does not provision secrets or choose a machine-wide key location in production. Development uses framework defaults; tests explicitly use ephemeral keys and delete temporary signing files.

Production rejects HTTP. Terminate TLS at Kestrel or explicitly configure and audit trusted proxy forwarding before deployment; this slice does not trust arbitrary forwarded headers. Do not expose a Development/Testing environment publicly. Request bodies are limited to16KiB at Kestrel; known oversized bodies are also rejected before binding. Authentication endpoints allow20 requests/minute per method/route/account-or-IP partition; read/profile APIs allow120. A bounded concurrency limiter returns503 without queueing when the configured request permits are exhausted. Reverse-proxy address/rate-limit tuning remains deployment work.

## API

- `POST /v1/auth/register` `{email,password,displayName}` returns201 `{playerId,confirmationRequired,confirmationDeliveryPending}`. Email is unique, at most254 characters; password is12–256 characters with Identity's default complexity; display name is1–16 trimmed printable ASCII characters. Account/profile/license rows commit together. A duplicate returns409 without partial rows.
- `POST /v1/auth/login` `{email,password}` returns ASP.NET Identity's `{tokenType,accessToken,expiresIn,refreshToken}`. These are framework bearer tokens, not JWTs. Unknown/wrong-password/unconfirmed/locked login returns401. Five failed attempts lock an account for15 minutes.
- `POST /v1/auth/refresh` `{refreshToken}` checks expiry, security stamp, confirmation policy and lockout. Access lasts10 minutes, refresh seven days.
- `POST /v1/auth/confirm-email` `{playerId,code}` consumes the protected Identity-generated code delivered by email. `POST /v1/auth/resend-confirmation` `{email}` returns202 without revealing whether an account exists, including when the per-account or per-IP resend limit is reached. Delivery must be configured; no confirmation codes are returned by registration or printed to logs.
- `POST /v1/auth/revoke-sessions` requires account authentication and invalidates refresh tokens via the security stamp. Already-issued access tokens expire normally; ticket issuance additionally checks the current stamp. This is not instant revocation of every profile/read access token.
- `GET /v1/me` requires account authentication and returns `{playerId,emailConfirmed,emailEligibleForOfficialPlay}`. Email eligibility alone is not server/match/rating authorization.
- `PATCH /v1/me/profile` `{displayName?,favoriteHunter?}` updates only the authenticated owner. Favorite Hunter is numeric0–6. Unknown properties, including supplied ownership/RP fields, are rejected.
- `GET /v1/players/{id}/license` returns `{playerId,displayName,favoriteHunter,joinedAt,points,tier,title,nextThreshold,lastOfficialDelta,policy}`. No account email/password metadata is exposed.
- `POST /v1/guest-node-admissions` is anonymous and accepts `{nodeId,displayName}`. The route is mapped in Development, Testing, and production; guest access is not environment-gated. The Node must still be online and ticket signing must be configured (`503` when the issuer is unavailable, `404` when the requested Node is not online). `displayName` is trimmed and must contain 1–16 printable ASCII characters.
- A successful guest admission returns the same short-lived Node admission envelope as an account admission. Its ES256 ticket has `typ=ph-node-admission+jwt`, `kind=guest`, a fresh ephemeral UUID in `sub`, the supplied name in `name`, and a unique replay ID in `jti`; it expires after 120 seconds. The request creates no account, profile, license, or `PlayerId`. The name is a display label only and is not an identity, authorization, or uniqueness claim.

With confirmation required but SMTP absent, registration returns503 before writing anything. SMTP failure after commit leaves a real unconfirmed account; use resend after delivery recovers rather than creating another identity. Email delivery is synchronous after the database transaction, not a durable email outbox. Password reset/change and2FA management UI/endpoints are not included yet; do not offer UI for absent routes.

## Node admission integration

Node registration and discovery are the only server admission path. `PUT
/v1/node/registration` authenticates the persistent Node with its configured
`X-Server-Id` and API credential, records its startup incarnation and `wss://`
control URI, and publishes its exact protocol/build/content profile. A Node
must heartbeat with the same incarnation; a replacement incarnation cannot
refresh the old listing. Backend restart clears the in-memory directory, so
Nodes republish their registration and heartbeat.

`POST /v1/node-admissions` uses a confirmed account bearer token and `{nodeId}`.
It returns a short-lived ES256 `ph-node-admission+jwt` containing the player
identity and the exact `publicControlUri`. The Node validates and consumes the
admission before opening its WSS control connection. The Node then signs and
delivers the per-match UDP handoff for the Worker-owned `MatchInstance`.

Guest admission is a separate anonymous path: `POST
/v1/guest-node-admissions` accepts `{nodeId,displayName}` and issues a fresh
ephemeral guest UUID with `kind=guest`. The account path remains account-only;
an account admission failure never falls back to the guest endpoint, and a
guest ticket never becomes an account admission. Both paths still require the
same configured ticket signer and an online Node.

The Backend no longer exposes direct standalone-server session registration or
account game-ticket issuance. Its signing configuration is used for Node
admission and result-signature consumers; it does not create a direct
client-to-Worker or client-to-legacy-Server path.

The Node keeps guest identities separate from registered accounts. Any guest in
the frozen roster, including a guest observer, makes the `MatchSpec` trust class
`Practice`. The Node validates the Worker report-ready artifact locally and
discards it instead of handing it to the Backend outbox, so the match produces
no Backend account/career or ranked report. A guest-containing match therefore
cannot contribute account statistics or rating even when other roster members
are registered.

## Explicit migrations and validation

The checked-in `Data/Migrations/InitialAccounts` creates Identity plus profile/license tables. Startup never calls `Migrate` or `EnsureCreated`. Migration tooling uses `BackendDesignTimeFactory` and the same environment connection-string key. Model construction and script generation do not connect to PostgreSQL.

With a compatible10.x `dotnet-ef` tool, generate/review an idempotent SQL script or migration bundle, then apply it separately with a migration role. Do not apply a migration against a shared database as part of a normal build/test. Example (writes a script only):

```sh
dotnet ef migrations script --idempotent --project src/Backend --output /tmp/prime-backend-migration.sql
```

`dotnet test tests/Backend.Tests/Backend.Tests.csproj` uses ephemeral SQLite and an in-process HTTP host, captures email only in test doubles, and validates PostgreSQL migration SQL/model offline. This is not PostgreSQL execution, deployed TLS, SMTP delivery, or native-client credential-storage proof. Before deployment, run migrations and account uniqueness/transaction/restart checks against disposable PostgreSQL, verify TLS/SMTP and durable protected keys, and complete server/client admission integration. No database or service is started by these tests.

Package/API basis: [ASP.NET Identity APIs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [Identity framework endpoint source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs), [Npgsql EF10](https://www.npgsql.org/efcore/release-notes/10.0.html), [EF migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying). Password/token cryptography stays in established framework components; JWT signing is Backend-owned and Game has no cryptographic/package dependency.


## Match ledger and career queries

`POST /v1/server/matches` authenticates the provisioned Node credential (`Authorization: Bearer`, `X-Server-Id`). Configure `GameServers__Servers__0__TrustClass` explicitly; it defaults to Community. The Backend derives effective trust from that authenticated registration, independently of the raw report claim. It stores that effective trust with the accepted ledger row and uses the stored value during rebuild. Old Node startup incarnations remain accepted so a retained spool can drain after restart; active Node admission is independent.

The endpoint requires `Idempotency-Key` (match UUID), `X-Content-SHA256` (uppercase SHA-256 of the exact body), and the default System.Text.Json serialized `MatchReportV1` body, bounded to512KiB. It stores the original bytes. A first acceptance returns201, the same UUID/hash returns200 with the exact persisted rating receipt, and a conflicting UUID/body returns409. Rating status is `applied` with immutable player transactions and pair evidence, or `ineligible` with an explicit reason. Schema1 remains accepted for career history and is always rating-ineligible as `LegacyReport`.

One PostgreSQL transaction takes a shared rebuild barrier, a UUID-derived report advisory lock, and all affected license rows in canonical PlayerId order. It freezes every current balance, calculates all PairwiseNormalizedV1 results, inserts the match and immutable rating ledger, applies every balance, records participant history, and updates projections before commit. Any failure rolls back all changes. Gaps in the sequence after rollback are valid. Disjoint participants may commit independently; overlapping participants always calculate from committed balances in their mutation order.

`CareerRebuild.RebuildAsync` is an explicit operator service with no public HTTP route. Run the one-shot operator command after applying the RatingLedger migration and before starting the public Backend:

```sh
dotnet run --project src/Backend/Backend.csproj -- --rebuild-career
```

The command applies pending migrations, takes the exclusive rebuild barrier, verifies raw report hashes and identities plus every persisted rating before/after chain and pair contribution, restores RP balances from the immutable rating ledger, rebuilds career projections in processing order, and exits without opening an HTTP listener. The RatingLedger migration remaps legacy participation outcomes and removes legacy weapon rows whose old `Matches` value cannot truthfully represent `MatchesUsed`; this rebuild restores those weapon projections from authoritative `BeamKills` facts.

Public queries:

- `GET /v1/players/{id}/career`: official totals combine VerifiedCasual and Ranked. Optional `trustClass` selects one separate bucket, including Community or Private. Tournament is explicit, not silently included. Forced/InvalidTeams and Practice reports remain in history but do not add career totals.
- `GET /v1/players/{id}/matches?limit=25&before=<processingOrder>`: descending acceptance order, up to100 entries, `nextCursor` is the last processing order when another page exists. This is recorded processing order rather than wall-clock arrival/finish order.
- `GET /v1/leaderboards/career?metric=kills&limit=25&cursor=<opaque>`: allowlisted metrics are kills, wins, kd, winPercentage, headshots, octolithScores, nodesCaptured, killsAsPrime and rp. Optional `hunter` and `trustClass` select attributable career categories; RP is the single official balance and rejects those filters. Ordering is descending score then ascending PlayerId; cursors bind the selected metric/scope/hunter. Ratio board scores round to six decimal places before both sorting and cursor comparison. K/D requires nonzero deaths and ten attributable games; winPercentage requires ten attributable outcomes.

Career includes explicit finished-win, finished-loss, tie, forfeit, grace-expired departure, and no-contest outcomes plus ratios, combat totals, play ticks, streaks, and derived favorites. K/D is null with zero deaths; win ratio is null without games. Form-kill counters stay null if any historical report lacks that telemetry. Selected favorite Hunter remains in the identity profile; most-played Hunter is derived from actual span play ticks. Most-played map/mode/hunter use play ticks. Favorite weapon uses kills and exposes `matchesUsed`, which increments only when authoritative `BeamKills` is positive. Best map and best Hunter use win ratio with ten attributable matches, ties broken by sample count then ordinal key. A participant changing Hunter within a match contributes measured play time to each Hunter, but cumulative combat/outcome facts cannot be divided across those Hunters: best-Hunter/leaderboard combat samples include only single-Hunter matches and expose `attributedMatches`. Guests and bots never receive account aggregates; registered-player counters remain attributed only to the authenticated report's PlayerIds. These are server-authoritative reported facts, not client self-reported statistics.

## PostgreSQL integration tests

Set `PRIME_TEST_POSTGRES_FILE` to a private file containing the connection string for an explicitly disposable local PostgreSQL instance. Each PostgreSQL test creates a unique schema, applies actual migrations, and drops only that schema afterward. Without this variable those tests are explicitly skipped. Tests cover concurrent registration, overlapping-player reports, same-body idempotency, conflicts, raw-body preservation, rollback, rebuild equality, trust separation, malformed facts, and actual PostgreSQL leaderboard/keyset translation. SQLite covers focused HTTP/auth boundaries; it is not a production persistence substitute.


The `publicControlUri` in a Node listing and admission is HTTPS-authenticated
routing metadata. Clients must connect to that exact `wss://` URI and cannot
substitute a Worker or UDP endpoint. The Node validates the admission, freezes
the lobby match specification, and returns the signed Worker handoff consumed
by the client. Loopback and LAN endpoints may still be used by isolated source
or package tests, but they are not a supported client hosting path.


Report duration validation follows the simulation's signed tick half-range: at most `int.MaxValue` 60Hz ticks, with corresponding UTC duration and mode-time bounds. Unlimited matches longer than one day remain admissible. Per-slot and overall participation intervals must remain within that range even when uint ticks wrap; body and participant limits are unchanged. Known biped/alt totals and the sum of beam kill buckets cannot exceed total kills; unavailable nullable historical form metrics remain accepted.

### Node discovery and admission

The Backend directory publishes compatible persistent Server Nodes. A Node owns
the client control connection, sessions, public lobbies and Worker placement;
its managed Workers own gameplay and direct UDP admission. The legacy UDP
MasterServer and the direct standalone Server rollback/deploy path are retired.
The Backend does not launch or stop local matches. Configure each Node ID and
hashed API credential in the existing `GameServers` registry. The configured
trust class determines the browser's `community` or `verified` label; Nodes
cannot submit that label themselves.

- `PUT /v1/node/registration`: `X-Server-Id` is the Node UUID and `Authorization:
  Bearer ...` is its API credential. JSON fields: `incarnation` UUID, `name`,
  `region`, `publicControlUri` (`wss://`), `protocolVersion`, `buildVersion`,
  `contentHash` (64 hex characters), and `capacity`.
- `POST /v1/node/heartbeat`: same credentials, JSON `incarnation`, `onlineUsers`,
  `lobbyCount`, `activeMatches`. Heartbeat approximately every 30 seconds. Old
  incarnations cannot refresh a replacement Node registration.
- `GET /v1/nodes?protocol=1&build=...&content=...`: exact compatibility filtering;
  only entries with heartbeat age below 90 seconds are returned.
- `POST /v1/node-admissions`: confirmed account bearer token and JSON `nodeId`.
  Returns `ticket`, `expiresAt`, `nodeId`, and `publicControlUri`. Tickets are
  ES256, have type `ph-node-admission+jwt`, audience
  `urn:prime-hunters:node:<NodeId>`, and expire after 120 seconds. The Node
  loads the matching operator-provisioned public key set and validates/consumes
  each `jti` once; there is no match identity or incarnation claim.

Listings are in memory and republished after Backend restart. This implementation
is for a single Backend directory process; multiple Backend replicas need shared
expiry storage before deployment. TTL disappearance affects discovery and new
Backend admissions only, never existing Node sessions or matches.

The supported client cutover is public-lobby-only: the launcher lists compatible
Nodes, creates `LobbyVisibility.Public` lobbies, and joins the public lobby list
over the Node's WSS control connection before receiving a Worker UDP handoff.
Private or unlisted local hosting has been retired. No `Worker --standalone`
mode was added; Workers are launched and supervised by their owning Node.
