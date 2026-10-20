# CW-04i endpoint-owned custom readiness

Protocol 61 reserves packet 83 CustomWeaponManifest, 84 CustomWeaponReady and 85
CustomWeaponReadyConfirmed. The developer capability is false by default on server
and client. It has no launcher/lobby/CLI activation and no action packet, combat
runtime activation or resource grant. Ordinary matches construct no custom readiness
owner or manifest cache. The native payload widths remain those of protocol 60;
the live datagram version/Hello requirement intentionally becomes 61.

The first live handshake supports only the reviewed built-in M7. Each side constructs
its immutable definition and exact manifest/ready bytes once per recipient bootstrap.
Complete cached byte comparison verifies the definition row, hash, schema, behavior,
capability and recipient. It neither fetches packages nor interprets untrusted
mechanics. Unknown/extra/modified packages refuse readiness. The general bounded
manifest codecs/assembler remain available for later package work; this path does
not repeatedly allocate their page objects on duplicate packets.

Server publication requires an endpoint-admitted, nonspectating peer with completed
native scene/bootstrap admission, exact current start and occupant generation.
Readiness binds the current transport ConnectionId in addition to endpoint, slot and
exact bootstrap. A copied packet from a foreign endpoint or superseded connection
cannot acknowledge another slot. The first accepted acknowledgement publishes an
immutable capability fact; duplicates preserve its opening world. Ordinary life
changes leave this occupant capability alone and do not grant inventory. Departure,
spectating, bootstrap/connection replacement and shutdown retire the capability.

Client messages pass the existing authenticated-host filter, exact confirmed native
bootstrap/slot/generation, current transport incarnation and explicit developer
opt-in. Pages before native confirmation may be ignored and recovered by retries.
The authority's exact ready echo confirms capability only after the full local
manifest matched. A readable contradictory current-bootstrap payload invalidates
its owner permanently until a new bootstrap; old/foreign bootstraps are ignored.
No arbitrary page can refresh an existing successful capability.

All three messages use the bounded reliable channel's ordinary quota and background
inbox lane. Send backpressure returns without consuming reserved critical capacity.
Application retries run at most every 0.25 wall seconds and pending readiness expires
after 30 seconds inclusive. Duplicate messages do not reset the deadline. Confirmed
capability persists until lifecycle retirement. Ingress additionally permits at most
16 ready offers per peer per wall second before header work; wrong connections do not
charge another peer's budget. Cached duplicate/header comparisons allocate nothing.
Client deadlines use the same sampled wall timestamp for construction and processing,
so a newly created cache cannot reject its first event as backdated.

The immutable server query is a diagnostic capability snapshot. It deliberately does
not implement H's ICustomSourceReadiness: no fresh post-open source-frame boundary
has been proven yet. No action, shot, health, ammo or presentation follows from these
messages. Production engine admission and combat transport remain separate CW-04
work. The control-plane UDP fixture explicitly supplies an empty synthetic native
baseline and acknowledges scene application without constructing a remote scene;
its opening world uses that baseline when there is no engine clock.

Protocol60 discovery/status and archived replay bodies remain readable. Explicit
protocol 61 checkpoint/metadata support keeps newly recorded native replay readable
without changing checkpoint versions, feature flags or field widths. The live Hello
still refuses protocol 60 with its existing mismatch reason. Old packet IDs and the
frozen registry baseline remain unchanged. This slice does not add custom replay
mechanics or claim a custom match is replayable.
