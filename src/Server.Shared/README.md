# Server topology contracts (A1)

`Server.Shared` depends on `Game` for its existing immutable `MatchRules`, Hunter,
PlayerId and trust classification. It contains no simulation, scheduler or process
manager. `MatchSpec.Validate()` is the admission boundary; the IPC codec invokes it
on both encode and decode. An immutable array freezes the roster, and every nested
seat/content/rules value is immutable. Directly constructed DTOs must be validated
before use outside the codec.

V1 framing is a four-byte little-endian unsigned body length, followed by one byte
of message type and a UTF-8 JSON envelope containing `version: 1` and `payload`.
The length excludes the four-byte prefix and includes the type byte. The hard cap
is 65,536 body bytes. The type IDs are the ordered table in `WorkerIpcCodec` (1–18);
never reorder or reuse them without a protocol version change. Unknown versions,
types, properties, duplicate JSON keys, missing required parameters, invalid values,
truncation and trailing bytes fail closed. Invalid frames require connection close.
EOF before any prefix byte is a normal close; partial EOF throws.

Roster limits are an initial contract choice: at most 32 seats total, player/bot
seat IDs 0–7, all seat IDs 0–31, and no more active seats than Game rules allow.
Hunters must already be resolved (Random is rejected). Worker placement carries
both persistent MatchId and per-incarnation WireMatchId. Worker lifecycle events
carry WorkerIncarnation; global MatchId identifies match lifecycle events within
the authenticated connection. Capacity and text limits are explicit in validators.

A1 does not authenticate a connection. The later Node/Worker transport must create
an authenticated local pipe, verify and consume the one-time WorkerHello token,
check Node/Worker incarnation, enforce message direction and lifecycle ordering,
serialize writes, impose deadlines and close on protocol failure. No runtime service
should treat successful JSON decoding as authorization. Startup tokens must never
be logged; UpdateNodeSigningKey carries only public verification material.

CreateMatch duplicate/conflict detection, placement allocation, reports, heartbeats,
worker startup/disconnect handling and real named-pipe tests belong to later stages.
The report-ready event carries a bounded report ID rather than a report/path; its
retrieval/acknowledgement protocol remains to be designed with the report outbox.

Run `dotnet test tests/Server.Shared.Tests/Server.Shared.Tests.csproj` from the repo
root for serialization, malformed payload, bounds, stream and immutability checks.

`MatchCompleted` carries a validated immutable completion summary: MatchId, LobbyId,
Game end reason, up to 32 participant outcomes, optional opaque replay/telemetry
UUIDs and a report UUID. Outcomes reuse Game participant kinds/outcomes and preserve
signed scores. This is a bounded Node presentation payload; the full authoritative
report and actual artifact retrieval remain separate. Runtime population and delivery
belong to the later Worker/Node integration stages.
