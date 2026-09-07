# Tournament administration

The optional Server admin endpoint is authenticated and loopback-only. It queues commands for the simulation owner; HTTP workers never mutate peers, world state, readiness, or match rules. It does not pause an active simulation.

## Startup

Set `PRIME_SERVER_ADMIN_PORT` to an unused unprivileged port and `PRIME_SERVER_ADMIN_KEY_SHA256` to the SHA-256 hex digest of a separately held32–512-character admin credential. Both settings are required together. The endpoint binds only `http://127.0.0.1:<port>/`; remote operators use an authenticated tunnel. This is independent of player account credentials and server reporting credentials. No credential is written into commands, status, reports, or replays.

Set `PRIME_SERVER_REPLAY_DIRECTORY` to the operator-owned replay output directory before using mandatory recording. Runtime map choices are restricted to the startup rotation/current-map allowlist. Rules presets use the same `RulesetResolver` as initial launch and normal rotation. A preset cannot change the running session's player capacity; launch a two-player session for Duel.

## API

Every request requires `Authorization: Bearer <admin credential>`.

- `GET /v1/admin/status` returns server UTC time, tick, authoritative MatchId once Playing begins, phase, roster lock, between-round hold, start request, tournament/round IDs, current map/preset, connection IDs/readiness, and recording state.
- `POST /v1/admin/commands` accepts an object with `requestId` (new UUID), `kind` (named command), and `issuedAtUtc` (current UTC ISO8601 timestamp). Command-specific fields are listed below. Unknown fields, enum integers, oversized bodies, and unrelated parameters are rejected. The response is202 for queued work; poll its status rather than treating enqueue as completion.
- `GET /v1/admin/commands/{requestId}` returns queued/applied/scheduled/rejected/expired status and the owner tick. Map reload and recording startup are explicitly scheduled operations; use the main status to observe readiness/completion.

Commands expire30 seconds after issue time and may be at most two seconds ahead of server time. Exact retries retain their original result throughout that validity window; changing a body under the same UUID conflicts. The system admits at most32 queued commands, retains at most256 in-window command/results, and applies at most four commands per tick. HTTP body size is4KiB with a five-second request deadline. Queue saturation rejects explicitly.

| Command | Additional fields | Behavior |
|---|---|---|
| `LockRoster`, `UnlockRoster` | none | Gate new competitive identities; existing/grace reconnects and true observers remain eligible. |
| `AssignTeam` | `connectionId`, `teamIndex`0 or1 | WaitingForPlayers team modes only. Updates peer/player team and disables automatic prestart reassignment. Invalidates ready confirmations. |
| `ForceSpectator` | `connectionId` | Real observer transfer on the authenticated connection, freeing its competitive slot. Fails before detaching if observer capacity/history is unavailable. |
| `SelectPreset` | `preset`, optional `roomKey` | WaitingForPlayers only; validate the allowlist/rules/capacity, then reload through normal match transition. Further commands wait for the new round. |
| `ReadyCheck` | none | Capture the current competitive connection roster and clear confirmations. |
| `ConfirmReady` | `connectionId` | Operator-confirmed readiness for a member of that captured roster. This is not an invented player-side ready UI. |
| `SetRoundIdentity` | `tournamentId`, `roundId` | WaitingForPlayers only; each1–64 ASCII alphanumeric/underscore/hyphen/dot. Both freeze into the report when play begins. |
| `StartCountdown` | none | Requires all checked participants confirmed and loaded, an unchanged roster, required teams/count, round identity, and available mandatory storage. Uses the existing countdown/reset owner. |
| `CancelBeforeStart` | none | Waiting/Countdown only; returns to waiting through lifecycle eligibility, clears confirmations, and holds the round. Cannot cancel a Playing/terminal result. |
| `PauseBetweenRounds`, `Resume` | none | Hold/release the next start/rotation. An active Playing simulation continues normally. |
| `Kick` | `connectionId` | Remove that connection without reserving reconnect; preserve reported participant departure facts. |
| `Mute`, `Unmute` | `connectionId` | Gate chat on that connection; mute state is removed with the connection. |
| `ForceReplay` | none | Before countdown, request server-owned mandatory recording. Countdown remains blocked until storage opens; failures are explicit. Subsequent rounds also require recording. |

A normal event sequence is: select map/preset; assign teams; lock roster; set tournament/round identity; enable replay; perform ready check; confirm each participant; start countdown. After a completed round, the admin holds the next round and clears its round ID/readiness. Resume releases an intermission hold; a new round still needs its own identity/ready check/start.

## Authoritative replay and result export

The server records Shared.Replay format3 files named `<ReplayId>.fpdemo`, using immutable observer facts. A bounded background writer owns file IO; tick code only enqueues frames. Recordings contain complete opening/indexed keyframes every300 ticks, match/rules, roster, snapshots, world batches, semantic event markers, authoritative clock/RNG, explicit observer perspective255, and shared observer feedback checkpoints preserving historical names, feeds, and duplicate suppression. Private participant recaps remain absent for observer perspective. Keyframes supplement ordinary records so sequential playback retains the opening baseline and checkpoint-frame facts. They are not optional client recordings. Missing-frame/overflow/storage failure exposes an incomplete-prefix failure rather than silently dropping records. Shutdown gives recording writers five seconds to drain, logs any pending failure, and continues durable result shutdown. Rotation closes the current recording; the next mandatory recording waits for that writer to finish before opening another, bounding live recording workers. A replay reference identifies a server artifact; the Backend does not pretend it hosts or independently verified that file.

The immutable report appends optional `tournamentId`, `roundId`, and `replayId`; no credentials or local paths are included. Backend `GET /v1/matches/{matchId}/export` returns those identifiers, the final participant scoreboard, effective authenticated trust, processing order, exact original report as Base64, its SHA-256, and an ES256 signed receipt. Verify the original bytes against the receipt's hash; readable outer fields are derived views of those bytes.

Result signatures use type `ph-match-result+jwt`, audience `urn:prime-hunters:match-result`, and purpose `match-result-v1`. They are durable assertions without ticket expiry, not admission credentials. A verifier must require that exact type/audience/purpose, issuer, permitted ES256 key, and payload hash. They cannot validate as server-bound game tickets. Retain verification public keys for all published historical exports when rotating signing keys; private old keys are unnecessary. Tournament reports remain rating-ineligible under the implemented `PairwiseNormalizedV1` policy; public Ranked remains disabled under Path B until authenticated UDP session proof-of-possession is implemented.

## Evidence boundaries

Focused checks cover actual loopback authentication and command queue bounds/expiry/idempotency, real AMHE1 simulation ready/start/cancel/playing-pause behavior, indexed observer replay contents/storage failure, and cryptographic result-purpose separation. The PostgreSQL pipeline test sends a real server outbox through the actual report transport and Backend HTTP pipeline, commits the report, verifies its receipt, removes the accepted spool file, and verifies the exported signature against returned public keys. The in-process HTTP pipeline is not deployed TLS proof; replay file/decoder checks are not rendered playback evidence.


## Automatic Duel recording

Setting `PRIME_SERVER_REPLAY_DIRECTORY` (or the embedded `ReplayDirectory` option) automatically records every round, without an admin API or `ForceReplay` command. A separate `ReplayMayStart` gate holds/cancels countdown until the recording writer has opened. Reporting, admin readiness, and voting gates remain independent. On rotation, only one closing writer and one active recording are permitted; the next round waits for the previous writer to finish. Reports carry the generated `ReplayId` even without tournament metadata or an admin controller.

Duel with either ticket authentication or report submission requires a replay directory at startup and for every allowed rules selection. The server cannot infer verified Backend trust locally, so this conservative policy covers authenticated/reported Duel regardless of its eventual trust bucket. Guest-only, unreported Duel can remain unrecorded. Explicitly configured recording failures (open, queue, capture gap, or close) terminate the server through normal bounded replay/report cleanup; they never silently release the start gate. An interrupted match has no fabricated completed result. A `ReplayId` identifies an artifact, not a guarantee that storage completed: a previously accepted terminal result may retain the identifier of a recording whose later close failed. The failure is logged and restart requires operator intervention. No rating policy is introduced.
