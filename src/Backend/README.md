# Backend account and ticket foundation

This net10.0 service references Game and the small shared Node contract assembly. It provides registered identity, profile/license fields, confirmation, bearer login/refresh, Node admissions, authenticated match submission, immutable career/rating projections, and read-only career leaderboards. Public license responses contain server-authoritative Ranking Points and tier data.

## Configuration

Use deployment environment variables or an operator-managed secret provider. No credentials or signing keys belong in appsettings, source control, command-line arguments, logs or replays.

| Environment variable | Meaning |
|---|---|
| `ConnectionStrings__Backend` | PostgreSQL connection string; required; supplied privately; runtime uses `prime_app` |
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
| `Backend__AllowLoopbackHttp` | Explicit Development-only loopback HTTP; remote plaintext HTTP is retired |

Use distinct per-Node secrets (at least32 characters of cryptographic entropy), and deliver the plaintext only to that Node. A length check is not an entropy guarantee. Ticket rotation publishes the current signing key plus at most seven previous public keys through the bounded HTTPS `/v1/node-admission-keys` response; keep the overlap deployed for the admission lifetime, clock skew, and reconnect drain before retiring an old key. Changing server configuration still requires restarting this initial service. Node credentials authenticate registration, heartbeat and report ingestion; they do not grant account access, ticket signing or official rating authority.

Persist and protect the Data Protection directory and private PEM with owner-only access and deployment storage protection. Losing Identity keys invalidates existing tokens; losing the ticket private key requires deliberate key rotation. This service does not provision secrets or choose a machine-wide key location in production. Development uses framework defaults; tests explicitly use ephemeral keys and delete temporary signing files.

Production rejects HTTP. Terminate TLS at Kestrel or explicitly configure and audit trusted proxy forwarding before deployment; this slice does not trust arbitrary forwarded headers. Do not expose a Development/Testing environment publicly. Request bodies are limited to16KiB at Kestrel; known oversized bodies are also rejected before binding. Authentication endpoints allow20 requests/minute per method/route/account-or-IP partition; read/profile APIs allow120. A bounded concurrency limiter returns503 without queueing when the configured request permits are exhausted. Reverse-proxy address/rate-limit tuning remains deployment work.

## Supabase PostgreSQL boundary

Supabase is PostgreSQL infrastructure only. Project Prime does not use Supabase
Auth, client SDKs, Realtime, the Data API, or direct database access from the
Client, Server.Node, or Server.Worker. Server.Worker produces an authoritative
result, Server.Node durably spools and sends it over HTTPS, and only the Backend
uses EF Core/Npgsql to reach PostgreSQL.

All application and ASP.NET Identity tables use the private `prime` schema.
EF history is `prime.__EFMigrationsHistory`. In the Supabase dashboard, disable
the Data API and enable incoming PostgreSQL SSL enforcement. Production Backend
configuration also fails closed unless the connection uses port5432 and
`SSL Mode=VerifyFull`. Install the project CA certificate in the Backend host's
trusted certificate store or set Npgsql's `Root Certificate` to the protected
CA file; do not use `Trust Server Certificate=true`.

Use one of these dashboard-provided connection modes:

1. Direct connection on port5432 when the Backend host has IPv6. The runtime
   username is `prime_app`.
2. Shared session pooler on port5432 when the Backend host needs IPv4. The
   runtime username is `prime_app.<project-ref>` and the pooler hostname must be
   copied from the dashboard.

Port6543 transaction mode is not a supported Project Prime deployment. The
Backend owns one long-lived `NpgsqlDataSource` with minimum pool size0, maximum
pool size10, connection timeout10 seconds, command timeout15 seconds, and
application/data-source name `ProjectPrime.Backend`. Automatic EF retry
strategies remain disabled because account, match-ingestion, and projection
flows own explicit transactions; a transient persistence failure returns 5xx
and the Node outbox retries later.

Supabase references: [PostgreSQL connection modes](https://supabase.com/docs/guides/database/connecting-to-postgres),
[SSL enforcement and CA verification](https://supabase.com/docs/guides/platform/ssl-enforcement),
[disabling the Data API](https://supabase.com/docs/guides/api/securing-your-api), and
[database backups](https://supabase.com/docs/guides/platform/backups).

## API

- `POST /v1/auth/register` `{email,password,displayName}` returns201 `{playerId,confirmationRequired,confirmationDeliveryPending}`. Email is unique, at most254 characters; password is12–256 characters with Identity's default complexity; display name is1–16 trimmed printable ASCII characters. Account/profile/license rows commit together. A duplicate returns409 without partial rows.
- `POST /v1/auth/login` `{email,password}` returns ASP.NET Identity's `{tokenType,accessToken,expiresIn,refreshToken}`. These are framework bearer tokens, not JWTs. Unknown/wrong-password/unconfirmed/locked login returns401. Five failed attempts lock an account for15 minutes.
- `POST /v1/auth/refresh` `{refreshToken}` checks expiry, security stamp, confirmation policy and lockout. Access lasts10 minutes, refresh seven days.
- `POST /v1/auth/confirm-email` `{playerId,code}` consumes the protected Identity-generated code delivered by email. `POST /v1/auth/resend-confirmation` `{email}` returns202 without revealing whether an account exists, including when the per-account or per-IP resend limit is reached. Delivery must be configured; no confirmation codes are returned by registration or printed to logs.
- `POST /v1/auth/revoke-sessions` requires account authentication and invalidates refresh tokens via the security stamp. Already-issued access tokens expire normally; ticket issuance additionally checks the current stamp. This is not instant revocation of every profile/read access token.
- `GET /health/live` proves only that the Backend process can answer; it never queries PostgreSQL and bypasses request-concurrency saturation.
- `GET /health/ready` performs a two-second bounded PostgreSQL check and returns200 only when `SELECT 1`, the exact checked-in migration sequence, every expected `prime` table, and initialized career projections are present. It returns a detail-free503 otherwise.
- `GET /v1/me` requires account authentication and returns `{playerId,emailConfirmed,emailEligibleForOfficialPlay}`. Email eligibility alone is not server/match/rating authorization.
- `PATCH /v1/me/profile` `{displayName?,favoriteHunter?}` updates only the authenticated owner. Favorite Hunter is numeric0–6. Unknown properties, including supplied ownership/RP fields, are rejected.
- `GET /v1/players/{id}/license` returns `{playerId,displayName,favoriteHunter,joinedAt,points,tier,title,nextThreshold,lastOfficialDelta,policy}`. No account email/password metadata is exposed.
- `POST /v1/guest-node-admissions` is anonymous and accepts `{nodeId,displayName}`. The route is mapped in Development, Testing, and production; guest access is not environment-gated. The Node must still be online and ticket signing must be configured (`503` when the issuer is unavailable, `404` when the requested Node is not online). `displayName` is trimmed and must contain 1–16 printable ASCII characters.
- A successful guest admission returns the same short-lived Node admission envelope as an account admission. Its ES256 ticket has `typ=pp-node-admission+jwt`, `kind=guest`, a fresh ephemeral UUID in `sub`, the supplied name in `name`, and a unique replay ID in `jti`; it expires after 120 seconds. The request creates no account, profile, license, or `PlayerId`. The name is a display label only and is not an identity, authorization, or uniqueness claim.

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
It returns a short-lived ES256 `pp-node-admission+jwt` containing the player
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

## Explicit migrations, roles, and validation

`Data/Migrations/PrimeInitialPostgres` is the clean first-deployment baseline.
It creates the `prime` schema and the complete Identity, account, match, career,
and rating model. It deliberately replaces the earlier development-only chain.
Normal startup never calls `Migrate` or `EnsureCreated`. Migration tooling uses
`BackendDesignTimeFactory` and `ConnectionStrings__Backend`; model construction
and script generation do not connect to PostgreSQL.

If any real database already contains the retired development migrations or
Project Prime tables in `public`, do not apply this baseline. `--migrate`
detects those legacy objects and fails without moving or dropping them; create
and review a dedicated `MoveToPrimeSchema` migration instead.

Use an administrator/migration credential only for schema work. After migration,
run `Data/Scripts/provision-prime-app.sql` with the password supplied through the
required psql variable. The `prime_app` role receives CONNECT, schema USAGE,
application-table SELECT/INSERT/UPDATE/DELETE, sequence access, and SELECT-only
EF history. It owns no objects and has no DDL or role-management capability.
Re-run the grants script after a migration adds objects. Never use the Supabase
`postgres` administrator as the runtime identity.

With a compatible10.x `dotnet-ef` tool, generate/review an idempotent SQL script
or migration bundle. The supported deployment order is:

1. Take an encrypted off-site dump and verify a restore into a disposable database.
2. Supply the migration credential and run the Backend once with `--migrate`.
3. Apply the `prime_app` grant script as the migration administrator.
4. Supply the runtime credential and run `--rebuild-career`.
5. Run `--check-database`; record the safe JSON version/user/schema/migration/table result.
6. Start the Backend normally as `prime_app`, then gate activation on `/health/ready`.

`--rebuild-career` now refuses pending, missing, or unknown migrations and never
performs DDL. `--migrate`, `--rebuild-career`, and `--check-database` are mutually
exclusive one-shot operations and do not open an HTTP listener or require SMTP,
ticket, or Node registration settings.

Example offline SQL generation (writes a script only):

```sh
dotnet ef migrations script --idempotent --project src/Backend --output /tmp/prime-backend-migration.sql
```

`dotnet test tests/Backend.Tests/Backend.Tests.csproj` uses ephemeral SQLite and
an in-process HTTP host, captures email only in test doubles, and validates the
PostgreSQL migration/model offline. This is not deployed TLS, SMTP delivery, or
native-client credential-storage proof. No database or service is started by
the default test run.

Package/API basis: [ASP.NET Identity APIs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [Identity framework endpoint source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs), [Npgsql EF10](https://www.npgsql.org/efcore/release-notes/10.0.html), [EF migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying). Password/token cryptography stays in established framework components; JWT signing is Backend-owned and Game has no cryptographic/package dependency.


## Match ledger and career queries

`POST /v1/server/matches` authenticates the provisioned Node credential (`Authorization: Bearer`, `X-Server-Id`). Configure `GameServers__Servers__0__TrustClass` explicitly; it defaults to Community. The Backend derives effective trust from that authenticated registration, independently of the raw report claim. It stores that effective trust with the accepted ledger row and uses the stored value during rebuild. Old Node startup incarnations remain accepted so a retained spool can drain after restart; active Node admission is independent.

The endpoint requires `Idempotency-Key` (match UUID), `X-Content-SHA256` (uppercase SHA-256 of the exact body), and the default System.Text.Json serialized `MatchReportV1` body, bounded to512KiB. It stores the original bytes. A first acceptance returns201, the same UUID/hash returns200 with the exact persisted rating receipt, and a conflicting UUID/body returns409. Rating status is `applied` with immutable player transactions and pair evidence, or `ineligible` with an explicit reason. Schema1 remains accepted for career history and is always rating-ineligible as `LegacyReport`.

One PostgreSQL transaction takes a shared rebuild barrier, a UUID-derived report advisory lock, and all affected license rows in canonical PlayerId order. It freezes every current balance, calculates all PairwiseNormalizedV1 results, inserts the match and immutable rating ledger, applies every balance, records participant history, and updates projections before commit. Any failure rolls back all changes. Gaps in the sequence after rollback are valid. Disjoint participants may commit independently; overlapping participants always calculate from committed balances in their mutation order.

`CareerRebuild.RebuildAsync` is an explicit operator service with no public HTTP route. Run the one-shot operator command after applying the current migration and runtime grants, before starting the public Backend:

```sh
dotnet run --project src/Backend/Backend.csproj -- --rebuild-career
```

The command first proves the exact checked-in migration sequence and projection-state row are present. It never applies migrations. It then takes the exclusive rebuild barrier, verifies raw report hashes and identities plus every persisted rating before/after chain and pair contribution, restores RP balances from the immutable rating ledger, rebuilds career projections in processing order, and exits without opening an HTTP listener.

Public queries:

- `GET /v1/players/{id}/career`: official totals combine VerifiedCasual and Ranked. Optional `trustClass` selects one separate bucket, including Community or Private. Tournament is explicit, not silently included. Forced/InvalidTeams and Practice reports remain in history but do not add career totals.
- `GET /v1/players/{id}/matches?limit=25&before=<processingOrder>`: descending acceptance order, up to100 entries, `nextCursor` is the last processing order when another page exists. This is recorded processing order rather than wall-clock arrival/finish order.
- `GET /v1/leaderboards/career?metric=kills&limit=25&cursor=<opaque>`: allowlisted metrics are kills, wins, kd, winPercentage, headshots, octolithScores, nodesCaptured, killsAsPrime and rp. Optional `hunter` and `trustClass` select attributable career categories; RP is the single official balance and rejects those filters. Ordering is descending score then ascending PlayerId; cursors bind the selected metric/scope/hunter. Ratio board scores round to six decimal places before both sorting and cursor comparison. K/D requires nonzero deaths and ten attributable games; winPercentage requires ten attributable outcomes.

Career includes explicit finished-win, finished-loss, tie, forfeit, grace-expired departure, and no-contest outcomes plus ratios, combat totals, play ticks, streaks, and derived favorites. K/D is null with zero deaths; win ratio is null without games. Form-kill counters stay null if any historical report lacks that telemetry. Selected favorite Hunter remains in the identity profile; most-played Hunter is derived from actual span play ticks. Most-played map/mode/hunter use play ticks. Favorite weapon uses kills and exposes `matchesUsed`, which increments only when authoritative `BeamKills` is positive. Best map and best Hunter use win ratio with ten attributable matches, ties broken by sample count then ordinal key. A participant changing Hunter within a match contributes measured play time to each Hunter, but cumulative combat/outcome facts cannot be divided across those Hunters: best-Hunter/leaderboard combat samples include only single-Hunter matches and expose `attributedMatches`. Guests and bots never receive account aggregates; registered-player counters remain attributed only to the authenticated report's PlayerIds. These are server-authoritative reported facts, not client self-reported statistics.

## PostgreSQL integration tests

Set `PRIME_TEST_POSTGRES_FILE` to a private file containing an administrator
connection for an explicitly disposable PostgreSQL instance on
`localhost`, `127.0.0.1`, or `::1`. The role must be allowed to create and drop
databases. Each PostgreSQL test creates an exact random
`project_prime_test_<uuid>` database, applies the real migration into its literal
`prime` schema, and drops only that database afterward. Remote and Supabase hosts
are rejected before destructive setup. Without the variable, PostgreSQL cases
are explicitly skipped.

Coverage includes schema/history placement, runtime role DML versus DDL/history
denial, raw `FOR UPDATE`, advisory locks, concurrent registration, overlapping
matches, idempotency/conflicts, rollback, rebuild equality, and PostgreSQL query
translation. SQLite remains only for focused HTTP/auth boundaries.

## Deployment acceptance, observability, and recovery

The non-destructive Supabase smoke is `--check-database`: connect, `SELECT 1`,
report PostgreSQL version/current user, verify `prime`, verify the exact migration,
verify expected tables, and disconnect. Never point the destructive integration
suite at Supabase.

After that smoke, record live evidence for both complete paths: register,
confirm, log in/refresh, update/read profile and Hunter License; then sign in,
join a Node/lobby/Worker match, finish it, drain the durable report through
`/v1/server/matches`, and observe history/career/RP after Backend restart. Repeat
the same report for 201 then exact-idempotent200, and submit the same MatchId with
a changed body for409 without projection changes.

For failure acceptance, block Backend/database access while a match runs. The
match must finish, the Node spool must retain the report, retry after recovery,
and remove it only after the exact receipt. Repeat across Backend restart and
Node restart. Verify statistics change once and concurrent overlapping matches
remain serialized. These are live gates, not conclusions from unit tests.

Collect the native `Npgsql` meter for connection acquisition/command duration,
failures, and pool used/idle/max; its data-source label is the sanitized stable
name `ProjectPrime.Backend`. The `ProjectPrime.Backend` meter adds match ingestion
duration/failures, accepted count, and accepted-report byte histograms. Node
already logs `DurablePending`, `QueuedPending`, and `OldestAgeSeconds` every30
seconds. An external database collector should measure `accepted_matches` count,
`avg`, p95, and total `octet_length("OriginalReport")`, plus
`pg_database_size(current_database())`. Never label/log IDs, report bodies,
connection strings, passwords, tokens, authorization headers, or server keys.

Keep `AcceptedMatch.OriginalReport`: it is the immutable rebuild authority.
Measure storage before considering compressed object storage. On the Free plan,
schedule encrypted off-site `pg_dump`/Supabase CLI dumps and test restoring them;
upgrade before public account/rating value depends on Free-plan availability.
After the Backend has a stable outbound IP, restrict PostgreSQL ingress to that
host and an explicit administrator/VPN address.


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
  ES256, have type `pp-node-admission+jwt`, audience
  `urn:project-prime:node:<NodeId>`, and expire after 120 seconds. The Node
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
