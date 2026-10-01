# Queue-only lobby admission

Protocol 35 adds generated packets 57–64. Protocol-34 replay state layouts remain
supported and unchanged. IDs 55–56 are reserved for the coordinated semantic lane.
Run `dotnet run --project tools/nettest -c Release -- --waitlist` for the production
codec and real UDP dedicated-server matrix; no copyrighted content is required.

The server defaults to a 64-entry FIFO (hard maximum 256), 15-second offers and
10-second disconnect grace. Startup options are `-nowaitlist`,
`-waitlistcapacity`, `-waitlistoffer` and `-waitlistresume`. Discovery advertises
capability and current count as an optional extension; historical status replies
remain readable. Metrics contain aggregate counts and durations, not player names.

QueueHello receives a reliable QueueWelcome carrying the existing random transport
connection ID. Only a subsequent enveloped QueueJoin proves receipt and enters
FIFO. Pending peers, bootstrap rate, transport connections and pending lifetime
are bounded. Queue peers have an explicit direction-specific packet allowlist:
no gameplay state, health, score, intent or fabricated player Welcome reaches
ordinary server dispatch. Endpoint and connection identity fence every action;
queue IDs, offer IDs, names and client nonces alone cannot authorize acceptance.

Reservations account for humans and bots. Normal joins and bot additions cannot
consume reserved capacity. Accept refreshes authoritative occupancy, match and
epoch, then invokes ordinary Hello admission for the reserved slot. Existing
identity, team, JIP and readiness validation remains in that path. Normal Welcome
promotes the existing transport; `LobbyQueueClient` transfers the same socket and
queued bootstrap to `NetSession`, avoiding a second connection or race to the seat.
Offers are explicit accept/decline; server time controls expiry. Disabled JIP keeps
entries queued until the next lobby. Shutdown terminates queue clients.

The production test covers strict generated golden/truncated/malformed bytes,
full-server FIFO, theft by direct join or another queued peer, forged promotion,
decline and expiry, bot occupancy, stale offers, JIP transitions, disconnect grace,
shutdown and the actual same-transport NetLaunch/NetSession identity handoff.
Standalone policy tests additionally cover simultaneous reservations.

## Remaining completion gates

Launcher presentation is a follow-up on this admission foundation. Reconnection
currently retains only the same established endpoint/connection incarnation;
reopening a socket or changing address needs an explicit authenticated rebind
contract. There is no separate server spectator capacity: current SpectatorMode
uses an ordinary player slot, so full-server Spectate + Queue is not advertised.
Physical Android, WAN and gameplay-with-content runs have not been performed.
