# Immutable reports and durable outbox (G4.6–G4.7)

Reporting is an explicit opt-in Node/Worker facility. A Worker-owned
`MatchInstance` captures measured facts and writes an immutable report artifact;
the persistent Server Node validates the frozen placement, owns the durable
outbox and submits the report to Backend. It does not implement Ranking Points,
classify a factual departure as a policy forfeit, or make a Node's trust claim
authoritative. Backend owns credential/trust validation and idempotent
persistence. The client reaches matches through public Node lobbies; private or
unlisted local hosting and a Worker `--standalone` mode are retired.

## Participant and envelope ownership

`Game/Identity/MatchReport.cs` contains immutable, database-free records. The persistent MatchId is a fresh UUID at first Playing, distinct from the local uint wire match epoch. Metadata freezes the Node UUID/startup incarnation, actual Worker assembly informational build version, protocol, map and complete immutable MatchRules, UTC start/end and measured played ticks. Ruleset is currently Custom; variant and content hash remain unavailable/null rather than fabricated. Community is the only emitted trust class in this slice; the contract also distinguishes Practice, Private, VerifiedCasual, Ranked and Tournament for later registry-approved policy.

`MatchParticipantLedger` belongs to one simulation. It observes only participating peers, never waiting spectators. It captures stats at `ParticipantLeaving` and before `ForgetSlot`, so a later slot occupant cannot replace the old person's name, identity or damage. Each logical participant has a per-match UUID, optional authenticated PlayerId, explicit guest/registered-human/bot classification, original display name, started-match flag, factual participation spans, actual played ticks, outcome and complete measured metrics. Legacy Stars are not RP. Biped/alt-form kills use the new authoritative counters; unmeasured shots/hits remain null.

Registered returns reuse the logical PlayerId record; guest returns require the authority's retained connection chain. A return with retained game counters does not double-add old totals. A same-account new activation after a counter reset adds the old measured counters to the new epoch. Prior spans, including ExplicitLeave, are never rewritten. A disconnected participant records Departed; connected at completion records Finished. This does not erase an earlier explicit leave or authorize a forfeit waiver: the future approved policy must inspect the retained facts. Raw departure-time standing is not a finalized placement. Terminal connected metrics come from the already immutable MatchResult, and reporting cannot mutate that result.

Spans use absolute wrapping uint server ticks for JoinedTick/LeftTick and explicit PlayedTicks for actual simulated participation; no playtime is inferred from disconnected wall-clock gaps. Every span closes with an exit reason. Participant PlayedTicks equals the sum of its spans. No credentials, endpoints, IPs, session secrets or client account tokens enter the report.

The ledger is bounded at256 historical participants and256 spans per participant. Admission closes before exhausting remaining participant/interval capacity. A capacity/invariant failure remains visible and prevents reporting completion; it is not silently truncated. This preserves more than the eight simultaneous slots without unbounded match history.

## Capacity and tick-thread boundary

One `MatchReportOutbox` lasts for the entire Server Node lifetime, across Worker matches and Worker replacement. Before a configured match can start, the Node reserves one report slot and twice the maximum payload size (covering the encoded envelope). The Worker writes the terminal immutable report artifact after simulation; `NodeReportIngestor` validates its Node/Worker placement binding and transfers that reservation into the Node outbox. Snapshotting immutable facts is on the Worker simulation owner; JSON serialization, hashing, file writes, flushes, recovery and HTTP are background work.

Default limits are128 reports,64MiB reserved/spooled bytes,512KiB body and16 queued handoffs. Quarantined files still count against limits. A backend outage permits the current match and later matches while reserved capacity remains. Storage faults, invalid reports, quarantines and authentication refusal close new starts/admission. The running match continues. Rotation waits for local DurablyStored or BackendAccepted, never merely Queued. Failed transfer retains the report in the current simulation and retries; no later scene owns that retry.

## Durable boundary and recovery

The writer serializes the report once with default System.Text.Json options (PascalCase properties, numeric enums, PlayerId's canonical UUID converter), hashes those exact UTF-8 bytes with SHA-256, and stores original bytes plus uppercase hash/schema/sequence in a spool envelope. Temporary files are created in the same directory, written with write-through, flushed, then renamed to a sequence-and-MatchId filename. Unix also fsyncs the parent directory; Windows uses MoveFileEx write-through rename. Lost Windows deletion may cause an idempotent retry, not another rating transaction.

An exclusive `.owner` file prevents competing Node report senders from using the same spool. Startup validates bounded files, schema, report identity, exact body hash and duplicate/conflicting identity; incomplete or invalid files are quarantined visibly. Recovery retains original serialized bytes. It does not upgrade an old body to current rules or silently discard poison records. Report directories are dedicated to the owning Node; unknown files are treated as invalid owned spool contents.

Receipt states distinguish Queued, DurablyStored, BackendAccepted, Quarantined and Failed. A duplicate pending ID with identical bytes shares the original receipt; conflicting bytes fail without replacing the original file. Backend idempotency must remain authoritative after locally acknowledged files are deleted.

A crash before the local durable acknowledgment can lose the RAM handoff. A flushed temporary file before rename is not an acknowledged complete record; recovery quarantines it for inspection. This implementation does not claim zero-loss completion across power failure. Stronger guarantees require an explicit durable completion fence before the game advertises official completion. Fault/restart tests model these boundaries; they are not physical storage power-loss certification.

## HTTP and operator workflow

Nothing is created or submitted when `Reporting` is null and PRIME_REPORT_DIRECTORY is absent. Enable through the Node's explicit `ServerReportingOptions` injection or these process environment values:

- PRIME_REPORT_DIRECTORY: dedicated private writable spool directory.
- PRIME_SERVER_ID: provisioned nonempty persistent Node UUID.
- PRIME_REPORT_URL: HTTPS endpoint, normally `/v1/server/matches`.
- PRIME_REPORT_CREDENTIAL: provisioned reporter secret; never a player token or ticket-signing key.

The reporter secret may be the same registered Backend Node secret, but is supplied through this separate variable so report-only operation does not accidentally enable incomplete Node admission configuration. Do not place secrets in shell arguments, logs or spool files. Backend authenticates the stable Node reporting identity and accepts historical startup incarnations so restart recovery works. With Node admissions configured, the report and admission use the same startup incarnation; mismatched Node IDs fail configuration.

Requests include Authorization Bearer, X-Server-Id, Idempotency-Key UUID and X-Content-SHA256. Redirects are disabled. HTTPS response headers and bounded receipt-body reads share a15-second deadline. A successful response must explicitly confirm lower-camel `matchId` and `payloadHash` for the exact body; an arbitrary2xx is not acceptance.

The Node sender is FIFO by durable sequence, preserving per-Node order for the proposed current-balance transaction model. Network/408/429/5xx failures use exponential backoff with jitter, bounded at60seconds plus an honored Retry-After capped at300seconds. Authentication refusal pauses sending until operator credential repair/restart. Validation/hash conflicts quarantine the report; later reports may proceed, but the permanent fault blocks new matches until an operator resolves the spool. No RP is calculated by this worker.

Every30seconds the Node prints ready/reserved count/bytes, quarantine count, durable count, undurable reservations/queued work, oldest durable age and last error. Permanent faults are separate from transient disk failures and cannot be cleared by a later successful write. Operators should stop the Node report worker before inspecting or moving quarantine files and preserve original bodies/hashes when resolving them.

Orderly Node shutdown attempts terminal ownership transfer, closes the input channel and drains local writes for a bounded deadline before canceling HTTP. If an uncooperative report worker remains after the shutdown deadline, exclusive spool ownership and cancellation resources remain alive until it actually exits. A second owner cannot race still-running disk work. A deadline failure reports that pending work was not durable; incomplete live matches are not fabricated as completed results.

## Verification

Twelve focused report/ledger tests passed: durable writes independent of blocked HTTP, count/byte bounds, exact-body restart recovery, accepted cleanup, duplicate/conflict identity, malformed/incomplete quarantine, shutdown ownership retention, JSON validation, departing slot reuse, registered logical-participant return and immutable terminal facts. The run also covers transient/permanent faults, in-flight capacity reservations and FIFO retries. Terminal completion closes participation at the first excluded tick (T+1 after stepping T), matching half-open Backend interval validation. These are focused source/test results; they do not independently prove a packaged, deployed, WAN or live-client Node/Worker run.

Artifacts: `/tmp/codex-re-prime-g4-report-build.log` and `/tmp/codex-re-prime-g4-report-tests.log`. Tests use injected transport/storage and local temporary spools; no deployment, real Backend HTTP acceptance, power-loss durability or approved RP transaction is claimed by this slice.
