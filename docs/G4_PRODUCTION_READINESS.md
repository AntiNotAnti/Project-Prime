# G4 production readiness

This is the S6 production/security disposition for the current Backend and
authoritative-server source. “Ready” below describes the implemented source
path and its operator gates; it is not evidence that a production deployment
has already been performed.

| Surface | Status | Boundary |
| --- | --- | --- |
| Account system | **Ready** | Registration, confirmation, bearer login/refresh, profile ownership, and session revocation are implemented. Production requires the startup and external-service gates below. |
| Verified Casual | **Ready** | A confirmed account can receive a server-bound ticket, a registered `VerifiedCasual` server can admit it, and authenticated immutable reports can feed career projections. `RatingStatus` remains `policyPending`; this status does not claim Ranking Points. |
| Ranked | **Intentionally disabled under Path B** | Public Ranked remains unavailable until authenticated transport proof-of-possession is implemented. It must not be advertised or registered. |

## Production startup gate

Run the Backend only in a non-Development, non-Testing environment with a
separately managed PostgreSQL database and secret store. `Program` and
`BackendSecurity` fail startup or reject requests when the required production
configuration is absent. The deployment record must contain, at minimum:

- `ConnectionStrings__Backend` pointing at the production PostgreSQL database
  with a least-privileged application role. Schema migration is a separate
  operator step; application startup does not call `Migrate` or `EnsureCreated`.
- `Backend__PublicUrl` as the public HTTPS origin, without credentials, query,
  or fragment. Production middleware rejects HTTP requests. If TLS terminates
  at a reverse proxy, the proxy must forward the external scheme and client
  address through the allowlist described below.
- `Backend__AllowLoopbackHttp=true` is the only HTTP exception and applies only
  in Development when both ends of the connection are loopback. Leave it false
  in every shared or public environment.
- `Accounts__RequireConfirmedEmail=true` and
  `Accounts__DataProtectionKeyPath` as an absolute, durable, operator-owned
  directory. Protect and back up this directory; losing it invalidates
  protected Identity and confirmation material.
- A configured SMTP provider: `Email__Host`, `Email__Port`, and
  `Email__Sender`, with `Email__Username` and `Email__Password` supplied only
  through secret storage when authentication is required. `ConfirmationEmail`
  requires TLS and bounds the provider timeout. Configuration validation is not
  a delivery proof, so a real registration, confirmation, resend, and provider
  failure check belongs in the deployment runbook.
- Ticket signing settings: `Tickets__Issuer`, `Tickets__KeyId`, and
  `Tickets__SigningKeyPemPath` for an operator-owned P-256 private PEM. The
  issuer must use the Backend public HTTPS origin. Keep any
  `Tickets__PreviousKeys__*` entries as public verification keys during a
  rotation; never distribute the private key to a game server or client.
- At least one explicitly enabled `GameServers__Servers__*` registration for
  the intended server, with a unique UUID, canonical public IPv4 address and
  UDP port, an explicit trust class (`VerifiedCasual` for this release), and a
  SHA-256 digest of a separately generated high-entropy server secret.

The production validator rejects known development credential hashes and
duplicate enabled server credential hashes. `GameServerRegistry` additionally
requires a server secret of 32–512 characters and compares it in constant
time. Generate a different secret for every server; do not reuse a development,
test, player, SMTP, reporter, or ticket-signing credential. Keep the plaintext
only in the service environment/secret provider. A report-only server may use
`PRIME_REPORT_CREDENTIAL`; it must still be a separately managed operator
secret and must never be a player bearer token or signing key.

## Forwarded headers, rate limits, and concurrency

`BackendSecurity.ConfigureForwarding` trusts only the literal IP addresses in
`Backend__TrustedProxies`. Entries must be canonical IP literals; networks and
arbitrary forwarded addresses are not accepted. Forwarding is limited to one
hop, requires header symmetry, and clears the framework's default known
networks. Put the directly connected TLS reverse proxy in this allowlist and
leave the list empty when Kestrel is the public TLS endpoint. Do not expose a
Development or Testing environment through a public proxy.

`Program` uses two bounded fixed-window endpoint policies after routing:

- `auth`: 20 requests per minute per HTTP method/route and authenticated
  account, or per client IP before authentication.
- `api`: 120 requests per minute using the same partition shape.

The old shared global requests-per-minute bucket is not the primary control.
The service instead has a `ConcurrencyLimiter` with
`Backend__MaxConcurrentRequests` (default 128, bounded to 1–10,000), no queue,
and a `503 Service Unavailable` response when all permits are occupied. Request
bodies are bounded to 16 KiB except for `/v1/server/matches`, which is bounded
to `ReportValidation.MaximumBytes` (512 KiB). Monitor 429/503 rates and adjust
the endpoint limits or concurrency only with an observed workload and an
explicit capacity decision.

## Confirmation lifecycle and SMTP outage behavior

`AccountConfirmationTokens` wraps the Identity confirmation token in the
Backend Data Protection provider with an issued timestamp. The configured
`Accounts__ConfirmationTokenLifetimeMinutes` defaults to 60 minutes and is
bounded to 5 minutes through 24 hours. Expired, future-dated, malformed, or
otherwise unprotectable codes are rejected.

`POST /v1/auth/resend-confirmation` always returns `202 Accepted` after a valid
request whether the email exists, is already confirmed, or has been throttled.
The limiter uses a keyed digest of the normalized account address and the
client address, so raw email addresses are not retained by the limiter. The
defaults are three resends per account and ten per IP in a 15-minute window,
with bounded tracking capacity. The endpoint also remains behind the `auth`
rate policy. These limits are privacy behavior as well as abuse protection; do
not add an existence-revealing response.

With confirmation required and SMTP not configured, registration returns 503
before creating an account. If a configured provider times out or returns an
SMTP failure after the account transaction commits, registration leaves the
account unconfirmed and reports `ConfirmationDeliveryPending=true`. The
failure log records only a failure type. Resend is the recovery path after the
provider is restored. Confirmation mail contains the player identifier and the
required verification code only; it must not contain passwords, bearer tokens,
server credentials, or signing material.

## Ticket keys, session revocation, and server revocation

`GameTicketIssuer` signs short-lived ES256/P-256 tickets. The current private
key is configured by `Tickets__SigningKeyPemPath`; `/v1/game-ticket-keys`
publishes the current public key and configured previous public keys. Tickets
expire after 120 seconds and are bound to the account, server UUID, server
incarnation, display name, and join nonce.

Use this rotation sequence:

1. Generate a new P-256 private key and key ID outside the repository.
2. Configure it as the current `Tickets__KeyId` and
   `Tickets__SigningKeyPemPath`, while publishing the former public key under
   `Tickets__PreviousKeys__*`.
3. Restart the Backend, then verify `/v1/game-ticket-keys` and a real ticket
   issue/admission flow.
4. Keep the previous public key until every ticket it could have signed has
   expired, plus verifier clock skew and the current server key-cache window.
   The conservative operational window for the current verifier is at least
   three minutes after the last old-key issuance. Remove the old public key in
   a later restart and record the retirement time.

Account `/v1/auth/revoke-sessions` updates the Identity security stamp. Refresh
tokens then fail validation; existing access tokens expire normally, and game
ticket issuance checks the current stamp and lockout state. This is not an
instant revocation of an already issued UDP ticket.

`GameServerRegistry` stores enabled server identities and credential hashes in
memory at Backend startup. To revoke a server, disable its registration or
replace its credential hash with a newly generated secret, restart the Backend,
and stop or isolate the old server process. A fresh `/v1/server/session`
registration is required for the new server incarnation. Issued tickets remain
bounded by their 120-second lifetime; a transient Backend outage may allow the
server's cached public keys to validate already issued, unexpired tickets, so
cached-key behavior is never an operator revocation guarantee. A failed server
registration closes new ticket admission for that process.

## Migrations, backup, and rollback

Keep these operations separate from application startup and from a normal
release health check:

1. Review the exact EF migration set (`InitialAccounts`, `MatchLedger`, and
   `CareerStatistics` in the current tree), generate an idempotent SQL script
   or migration bundle, and apply it with a migration-only role during a
   maintenance window.
2. Take and verify a restorable PostgreSQL backup before applying schema
   changes. Include the durable Data Protection directory, ticket key material
   and public-key history, and any pending server report spool in the recovery
   plan. Keep secrets in the secret manager rather than copying them into the
   repository or backup logs.
3. Deploy the application only after the migration and restore check pass.
   A rollback must use a tested backward-compatible application build or a
   verified database restore/corrective migration; do not improvise a destructive
   down-migration over accepted immutable reports. Career tables are projections
   that can be rebuilt by the explicit `CareerRebuild` service, while the raw
   `AcceptedMatch.OriginalReport` and its hash remain the durable authority.

Record migration version, backup identifier, restore test result, application
version, and rollback decision together. A green process start without this
record is not a migration or backup proof.

## Reporting and outbox operations

Durable report delivery is enabled by `PRIME_REPORT_DIRECTORY`,
`PRIME_REPORT_URL` (the Backend HTTPS `/v1/server/matches` endpoint), and
`PRIME_REPORT_CREDENTIAL` or the explicitly shared server secret. The directory
must be dedicated, writable by the server identity, protected from other
services, and included in the recovery plan. `MatchReportOutbox` uses an
exclusive `.owner` file, bounded defaults of 128 reports, 64 MiB, 512 KiB per
report, and 16 queued handoffs, and preserves FIFO delivery across rotation and
restart.

The server prints an `[match-outbox]` status every 30 seconds. Review all of
these fields in service monitoring:

`Ready`, `ReservedReports`, `ReservedBytes`, `Quarantined`, `DurablePending`,
`QueuedPending`, `OldestAgeSeconds`, and `LastError`.

Per-report state is one of `Queued`, `DurablyStored`, `BackendAccepted`,
`Quarantined`, or `Failed`. A report is accepted only after the Backend receipt
confirms the exact `matchId` and `payloadHash`; a generic 2xx is not acceptance.
The Backend endpoint authenticates the provisioned server, derives effective
trust from `GameServerRegistry`, validates the immutable body, preserves the
original bytes, and treats a repeated UUID/body as idempotent.

Transient transport, 408, 429, and 5xx failures retry with bounded backoff.
Authentication refusal pauses the sender until an operator repairs the
credential and restarts it. Invalid/rejected bodies, conflicting MatchIds,
corrupt recovery files, and permanent storage faults are visible and stop new
match admission as appropriate; they never silently turn into an accepted
result.

Quarantine inspection is an operator action. Stop the owning server/outbox
worker first, preserve the original `.quarantine` file and its surrounding log
context, compare the envelope hash and MatchId with Backend receipts, and
resolve the source/configuration defect before any controlled retry. Do not edit
the serialized body, delete a poison file to clear the counter, or run a second
worker against the same directory. The current source exposes status through
the periodic log and local spool files; it does not provide a remote quarantine
editing endpoint.

## Ranked Path B boundary

The S6.4 gate remains on Path B because the signed bearer ticket authenticates
the ticket but does not provide complete on-path theft resistance for the plain
UDP handshake. In a public deployment, `GameServerRegistry.RankedAvailability`
is false with the explicit proof-of-possession reason. The registry rejects
Ranked server session authentication and ticket destinations, and
`GET /v1/ranked-availability` exposes the unavailable state for a truthful UI.

Do not add a public `GameServers__Servers__*` registration with trust class
`Ranked`, advertise a Ranked browser entry, show an enabled Ranked button, or
silently route a Ranked action to Casual. If a client has a Ranked surface, it
must show an explicit unavailable state and remain unregistered until Path A is
implemented and the transport gate is re-evaluated. `VerifiedCasual` remains a
separate trust class and does not inherit Ranked availability.

## Evidence boundaries

The evidence for this disposition is the current source path, including
`BackendSecurity`, `Program`, the account confirmation classes,
`GameTicketIssuer`, `GameServerRegistry`, `MatchIngestion`, and
`MatchReportOutbox`, plus their focused test projects. This document does not
promote source compilation, focused tests, or local SQLite/in-process HTTP
checks into proof of a deployed PostgreSQL service, real TLS/proxy behavior,
SMTP delivery, secret restoration, server revocation, physical spool durability,
WAN admission, or live client interoperability. Those gates require a separate
deployment record with captured results.

The combined endurance run, physical Android checks, and high-refresh checks
were waived and remain evidence limitations; no pass is claimed for them here.
They do not change the Ranked Path B decision or authorize a production
advertisement.
