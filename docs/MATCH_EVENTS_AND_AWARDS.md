# Match events and awards

This is the canonical entry point for Project Prime's QZ3 semantic-event,
award, announcer, replay-marker, and telemetry contract. The detailed ownership
rules, award definitions, transport behavior, presentation boundary, and
deterministic test evidence are maintained in
[`QZ3_MATCH_SEMANTIC_EVENTS.md`](QZ3_MATCH_SEMANTIC_EVENTS.md).

The central invariant is that authoritative simulation publishes one normalized
`MatchEvent` fact. Awards consume those facts, and reliable presentation,
replay, telemetry, HUD, and announcer consumers receive the normalized fact or
the resulting authoritative `MatchAward`. They do not reinterpret snapshots or
kill timing to invent semantic awards.
