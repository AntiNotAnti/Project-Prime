# G4 implementation design

**Historical proposal only — no persistence implementation or deployment
authorization.** This document translates G4 of
`PROJECT_PRIME_G1_G5_IMPLEMENTATION_PLAN.md` using the pre-A26 source shape.
The `src/Server` names, direct game-ticket route, direct server admission and
guest-LAN/private-hosting language below are retained as historical design and
behavioral evidence; they are not current implementation or deployment
instructions. Exact retail RP arithmetic and the eight-player extension still
require their separate evidence/specification gate.

## Current A26 boundary

The supported implementation is a persistent `src/Server.Node/` plus a bundled,
Node-owned `src/Server.Worker/`. Backend owns account, Node directory/admission
and report persistence. The Node owns sessions, public lobbies, frozen
`MatchSpec` placement, Worker lifecycle and report ingestion/outbox. Each Worker
owns its `MatchInstance`, fixed 60 Hz simulation, direct gameplay UDP, replay,
telemetry and immutable report artifact. The client selects a Node, creates or
joins a public lobby over WSS, then receives the signed Worker handoff. Private
or unlisted local hosting is retired; no Worker `--standalone` mode was added.

The native `osx-arm64` extracted-bundle package smoke passed the WSS, public
lobby, Worker placement, routed-UDP admission, match-end, artifact/replay and
drain/no-orphan checks. This is local package/process evidence only; it does
not establish Windows, Android, deployed, WAN, rendered-client or broader live
client proof.

## Historical proposed shape (pre-A26)

Add one `src/Backend/Backend.csproj` (`Microsoft.NET.Sdk.Web`, `net10.0`) with ASP.NET Core Identity, EF Core, and PostgreSQL. Backend references Game for small immutable identifiers/report contracts; Server and Client communicate with Backend through HTTPS/JSON, without project references to Backend. Game retains BCL plus OpenTK.Mathematics only. No broker, cache service, repository-framework layer, or extra contract project initially.

Use matching supported 10.x releases of `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, EF Core design tooling, and `Npgsql.EntityFrameworkCore.PostgreSQL`; pin exact compatible patch versions when implementing. Npgsql's EF provider has a released 10.0 line. [Npgsql release notes](https://www.npgsql.org/efcore/release-notes/10.0.html)

Keep Backend folders small: `Identity`, `Data/Migrations`, `Tickets`, `Matches`, `Licenses`, and `Ratings`. Server owns `Persistence` (outbox/report worker) and admission integration. Client owns account UI, secure credential storage, profile reads, and ticket requests. Android reuses portable HTTP/contracts and supplies platform credential storage.

## Historical source seams and proposed changes

| Historical source/API | Confirmed behavior | Proposed integration |
|---|---|---|
| Game `MatchRuntime.CaptureResult` / `MatchFlow.Process` | Authority creates one immutable `MatchResult`; replica capture is separately guarded | Keep the gameplay result immutable and database-free; wrap it in an authority-owned report |
| Game `MatchResult.MatchId` | `uint` copied from the live match | Retain as wire/local epoch; add a separate persistent UUID, never reinterpret it as globally unique |
| Game `PlayerMatchResult` | Eight slot-indexed snapshots, includes assists/objective facts; no persistent identity, participation interval, or explicit bot/forfeit status | Add a match participant ledger and explicit immutable identity/outcome facts before reporting |
| Server `ServerSimulation.Step` | Disconnect/replacement invokes `NetScoreboard.ForgetSlot`; activation reuses the slot and updates nickname | Snapshot the departing participant before clearing, including the reconnect path; never recover identity from terminal slot/name alone |
| Server `ServerNetwork.Admit` / `ServerPeer` | Join supplies nickname, nonce, hunter, previous connection ID; no account authentication | Resolve verified admission identity before allocating a peer; retain guest admission as an explicit policy |
| Game `JoinPacket.TryRead` | Fixed 34-byte packet, exact-size validation | Design/version a bounded authenticated join extension; do not append credentials to protocol 8 invisibly |
| Historical Server `AuthoritativeServer.RunSimulation` | Single writer calls `network.Poll`, `simulation.Step`, then may rotate/dispose the scene | Observe newly captured result immediately after Step and before rotation; hand off immutable references only |
| Historical Server `AuthoritativeServer.Run` / `Program` | `Stop` ends loop; `finally` disposes simulation and master-list `Reporter` | Own report worker for the entire server lifetime, outside individual simulations; drain durable writes before orderly shutdown |

`MasterReporter` is a historical pre-A26 availability reporter; it is not a
match reporter or verified-server credential. Existing
`PlayerMatchResult.Stars` must not be assumed to be persistent license tier;
Backend owns RP/tier calculation. `Active` does not establish human status or
historical participation.

Introduce `PlayerId` as an immutable Guid wrapper under Game `Identity`, rejecting `Guid.Empty` for registered identities. Use a nullable PlayerId plus explicit `ParticipantKind` for guest/bot identities. A separate per-match `ParticipantId` distinguishes sessions without turning slots, IPs, display names, or connection IDs into account identity.

The Server participant ledger binds each activation to `(ParticipantId, PlayerId?, connection identity, slot generation)`, authoritative display-name snapshot, hunter/team intervals, joined/left ticks, and exit reason. Preserve cumulative stats before any `ForgetSlot`; reconnecting the same verified player resumes the logical participant while a different player in that slot cannot inherit it. Define one active connection per PlayerId per server; do not count waiting spectators as participants. Snapshot mutation stays on the simulation owner. A report may contain more historical participants than eight simultaneous slots, so admission/report limits must be explicit rather than silently truncating the ledger.

Allocate a persistent `MatchId` UUID once per match attempt and preserve it through retries. At the first transition to Playing, freeze server identity/incarnation, canonical map key/content hash, rules/build/protocol/schema versions and UTC start; use simulation ticks for played duration, UTC only for wall-clock history. At terminal capture, record end reason and UTC end. Countdown cancellation is not a completed official match. Track per-participant played ticks; the current `Time` field is mode-specific and includes Survival's `-1` sentinel, so it is not career play time.

## Historical identity and admission proposal

Use `IdentityUser<Guid>` with one immutable PlayerId/account and one Hunter License. Keep Identity's password hashing, lockout, confirmation/reset and security-stamp mechanisms. Require unique normalized email, bounded display names, request throttling and generic login/reset failures. Public official eligibility requires confirmed email; any private-test bypass must be explicit configuration and ineligible for official RP.

Map established Identity endpoints under `/v1/auth`; a small registration endpoint using `UserManager` may add display name and create profile/license rows atomically. Do not duplicate password validation/hashing. Native clients use Identity's short-lived access token and refresh flow, storing refresh credentials in the OS credential store. Browser clients, if added, use secure cookies and CSRF protection. Identity's built-in bearer tokens are proprietary, **not JWTs**, and must never be forwarded to a game server. Preserve its Data Protection keys securely across Backend restarts; signing out locally is not instant revocation of every issued bearer token, so enforce short access lifetime and eligibility/security-stamp checks at ticket issuance. [Microsoft Identity API documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)

Historical design note (retired client game-ticket path): the former `POST /v1/game-tickets` flow authorized an account and target server, then issued a short-lived signed credential containing schema version, issuer, PlayerId, authoritative display identity, server ID, server startup incarnation, issued/not-before/expiry times, unique ticket ID, and client join nonce. It is no longer a client contract. Current clients request a short-lived Node admission credential from `POST /v1/node-admissions` (or the explicit guest route) and the Node owns Worker placement and handoff. Backend holds signing private keys; Nodes receive public verification keys only. Account tokens, passwords and admission credentials never enter logs, rosters, replays, or match reports.

Proposed Server API: `IJoinAdmission.TryBegin(request)` queues bounded validation work; `TryTakeCompletion(out AdmissionDecision)` returns immutable results to `ServerNetwork.Poll`. Neither HTTP, key refresh, nor unbounded signature work runs on the tick owner. Only that owner allocates peers and atomically consumes a ticket ID after all admission checks pass. Retain consumed IDs until expiry; bind tickets to a fresh startup incarnation so process restart cannot replay them against an empty cache. An exact retry for the same ticket/nonce/endpoint returns the existing admission; reuse for another connection/endpoint is rejected. A reconnect obtains a fresh ticket and resumes by verified PlayerId, not public previous-connection ID alone. A full server refuses without minting another identity.

A signed bearer ticket over the current plain UDP handshake is not a complete anti-theft/session-integrity solution. Before public official ranking, the wire design must bind admission to proof of possession and authenticated subsequent session traffic using a reviewed transport/crypto construction; source-address checks and random connection IDs alone do not prove that. Keep this as an explicit security gate, not an implied guarantee of ticket signatures. Guest LAN/practice remains usable independently. Backend outages do not stop admitted matches; new registered admissions may use already-issued unexpired tickets and cached valid keys, but never bypass validation.

Verified game servers have individually provisioned, revocable credentials and a Backend-owned registry of allowed trust classes, builds/rulesets and content. Proposed v1 reporter credential is a high-entropy API secret sent only over HTTPS, stored hashed by Backend, compared with constant-time primitives, and rotated by credential ID. It is distinct from account authentication and cannot issue player tickets. Server-submitted trust claims are checked against this registry; unauthenticated, private, practice and community reports cannot mutate official aggregates/RP. Trust in an approved operator remains a trust assumption, not proof that a malicious server faithfully simulated a match.

## Historical immutable report and durable outbox proposal

Proposed small Game contracts: `MatchReportV1`, `MatchReportParticipant`, `MatchTrustClass`, `ParticipantExitReason`. They contain immutable values only; Server maps the participant ledger plus `MatchResult` into the envelope. No `DbContext`, entity references, live arrays, credentials, endpoints or IPs cross this boundary. Add only measured fields: current beam-kill counts do not establish shots, hits, damage-by-weapon or biped kills. Leave unavailable facts absent, rather than fabricating zero observations.

Proposed Server API: `IMatchReportOutbox.TryEnqueue(MatchReportV1)` is nonblocking; a worker durably spools and later submits it. Acknowledgement exposes separate `DurablyStored` and `BackendAccepted` states. Keep the captured immutable result reachable until durable acknowledgement; release the scene independently after successful queue ownership transfer. Retry ownership must survive rotation and never rely on observing a later scene's `Result`.

Use a bounded channel and one durable spool file per report: write to a uniquely named temporary file in the spool directory, flush file contents, atomically rename to `<MatchId>.json`, and ensure the platform's directory durability semantics are covered. Startup scans complete files; incomplete/checksum-invalid files are quarantined with visible diagnostics. Persist schema, payload hash and original exact body; retry identical bytes. Delete/archive only after Backend confirms that MatchId and hash were accepted. Never rewrite an old report to current rules or silently discard poison reports.

Reserve bounded report capacity before admitting an official match. Queue saturation retains the terminal report and prevents another official match from starting; the tick loop can continue serving intermission/practice according to explicit operator policy. Backend outage alone permits play while spool capacity remains. Disk-full/I/O failure must alarm and stop new official admissions, not drop completed reports or silently downgrade their trust. Bounded storage cannot promise unlimited offline play. A process crash before the first durable acknowledgement has a real loss window; do not claim lossless reporting from a RAM queue. A stricter zero-loss completion requirement would need a durable completion barrier/journal before declaring the official result committed.

The submission worker uses bounded exponential backoff with jitter and request deadlines. Retry network failures, 408/429 (honor Retry-After), and 5xx; quarantine validation/hash conflicts; stop authentication retries until operator credentials recover. Expose count/bytes/oldest age, last error, quarantine count and durable-pending count. Shutdown stops new official matches, captures any already-terminal result, drains local writes within a deadline, then stops HTTP work; incomplete matches are not fabricated as completed wins. Recovery tests must cover process death at each spool/HTTP acknowledgement boundary.

## Historical PostgreSQL ledger and transaction boundary proposal

Use one `IdentityDbContext` and one PostgreSQL database initially. Keep Identity's standard tables (map the user table to `players` if desired; do not create a duplicate account store), one-to-one `player_profiles` and `hunter_licenses`, plus `verified_servers` and credential/key metadata. Add `matches`, `match_players`, supported `match_player_weapons`, `rating_transactions`, and only the aggregate tables needed by shipped license/leaderboard endpoints (`player_stats`, `hunter_stats`, `map_stats`, `mode_stats`, `weapon_stats`). Keep practice/community dimensions physically or key-wise separate from official stats. No achievement/season tables until used.

`matches` has a UUID primary key, reporter server ID, report schema/policy versions, immutable payload/hash and authoritative metadata. `match_players` keys by match and participant identity, with nullable PlayerId and explicit kind/outcome. A PlayerId cannot appear as two competing participants in one accepted report. Foreign keys, bounds, nonnegative counters where applicable, finite durations and result/rules consistency are validated. Preserve explicit sentinels as nullable/domain values when normalizing JSON. Names are snapshots; account renaming never changes historical ownership.

`POST /v1/server/matches` authenticates the reporter, validates body size/schema, membership and trust policy, then executes **one transaction** for raw ledger, all eligible aggregates, and rating transactions. Insert the unique MatchId before applying effects; an existing identical server/hash returns the stored receipt, and a different body for that ID returns409. Never implement this as an unprotected “exists then update” sequence. Concurrent duplicate inserts rely on the database unique constraint and transaction outcome, so one delivery applies effects once.

Lock affected player/license rows in sorted PlayerId order before reading rating state, updating counters/streaks and writing immutable `(MatchId, PlayerId, ratingPolicyVersion)` transactions. Use a database-generated committed processing order as the v1 rating/streak order and store it; late reports must not silently reorder previously published RP. Preserve per-server FIFO submission, but cross-server chronological rating requires a separate policy if desired. Rebuild aggregates from ledger and the recorded processing order/policy; rating rebuild must include original pre-rating state and algorithm version. A raw match without these ordering inputs is not sufficient to recreate history-dependent RP.

Use one explicit EF transaction for this multi-step operation, with bounded retry of the whole transaction on PostgreSQL serialization/deadlock failures. A timeout after commit is resolved by identical resubmission, not compensation. PostgreSQL row locks serialize conflicting player updates; isolation and lock order are part of the implementation tests. [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [PostgreSQL concurrency](https://www.postgresql.org/docs/current/transaction-iso.html), [PostgreSQL row locks](https://www.postgresql.org/docs/current/explicit-locking.html)

Check EF migrations into Backend; review generated SQL and exercise upgrade on a realistic disposable database. Apply an explicit deployment migration bundle/script using a migration role; the running Backend role has no schema-change privilege and does not call `Database.Migrate` on every startup. Test backup/restore and rollback strategy before public use. [EF production migration guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)

## Historical API and policy gates

| Route | Authorization and result |
|---|---|
| `/v1/auth/*` | Identity registration/login/refresh/confirmation/recovery; bounded requests |
| `POST /v1/node-admissions` | Signed-in eligible player; Node-scoped short-lived admission credential |
| `PATCH /v1/me/profile` | Ownership from authenticated PlayerId; selected favorite differs from derived most-played |
| `GET /v1/players/{id}/license` | Public safe profile only; no email/account metadata; compact aggregate response |
| `GET /v1/players/{id}/matches` | Cursor `(processed order, MatchId)`; bounded page size |
| `GET /v1/leaderboards/{category}` | Allowlisted categories, eligibility/minimum games, stable score/PlayerId tie-break and cursor |
| `POST /v1/server/matches` | Verified reporter identity; durable idempotent acceptance receipt |

Return profile/license cards without fetching full match history. Represent undefined K/D or win ratio explicitly. Public read models distinguish selected favorite, most-played, and best-with-minimum-sample policy. Source current license RP and stars from Backend, never from a client claim.

Rating remains a pure Backend service over immutable inputs with a versioned table/policy. Do not ship the web table: exact-ROM research has already identified disagreement with a secondary historical source. Encode only independently verified gain/loss/indexing/draw/clamp behavior; approve the eight-player pairwise aggregation/normalization, ties, forfeits, teams, late joins and bot/guest exclusion separately. VerifiedCasual/Ranked eligibility comes from Backend policy; Tournament remains explicit. No MMR, payment system, achievement framework or full matchmaking is introduced here.

## Historical implementation order and acceptance

1. Freeze identity/participant/result contracts and reconnect/disconnect semantics; test slot reuse, terminal snapshots and locally repeated wire IDs.
2. Add Backend/Identity/PostgreSQL migrations and ownership-safe profile reads, using a real disposable PostgreSQL instance for integration tests.
3. Implement versioned ticket/session admission and verified-server provisioning; test expiry, replay, wrong server/incarnation, altered claims, revocation and reconnect ownership.
4. Implement durable outbox and atomic ledger/aggregate ingestion; test repeated/concurrent duplicates, conflicting body, rollback, crash recovery, disk-full/backpressure and Backend outages while ticks continue.
5. Implement the approved exact RP plus eight-player policy, then license/history/leaderboard UI. Exhaustively snapshot every rank pairing and test boundary rank-up/down, bots/private exclusion, deterministic ties, rename continuity and ledger rebuild.

Review dependencies to preserve headless Worker and Game purity. This document
is source/design evidence only: no package installation, schema migration,
service startup, authentication proof or production persistence test has been
performed for the historical G4 proposal. Use the current Node/Worker package
and public-lobby path for any A26 process validation.
