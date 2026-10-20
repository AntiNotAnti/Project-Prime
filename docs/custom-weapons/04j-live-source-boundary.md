# CW-04j live capability-to-source bridge

The separate internal `DeveloperCustomWeaponCombat` server flag requires the
existing readiness flag. Both are configured before Run; neither has a launcher,
lobby, CLI or packet activation. The real dedicated scene constructs the engine
adapter with a server-owned source-readiness bridge. Unsupported modes retain the
adapter's existing fail-closed eligibility rules. Protocol61 and all wire payloads
remain unchanged. No custom action handler or result transport exists yet.

A successful native `AcceptSlotIntent` return can mean an old-life recovered shot,
a duplicate or a reordered carrier. Its additive internal `newCurrentCarrier`
result is true only at the actual current movement write; public/native return
behavior remains unchanged. Only that result reaches the custom input publisher,
after exact endpoint/ConnectionId, native admission, bootstrap, stream, generation,
current life and active simulation-scene checks. The bridge repeats these checks
when the engine opens or reads readiness. It does not consume a diagnostic
capability snapshot as combat authority.

Each peer owns one bounded source-boundary tracker alongside its readiness owner.
It retains the latest exact accepted raw source input and a checked ulong serial.
The first accepted capability acknowledgement pins the current ingress serial.
Duplicate acknowledgement cannot move that fence. Reconsuming a held input after
readiness, a duplicate native carrier, an old life, malformed input or a foreign
connection cannot establish the boundary. Serial exhaustion closes the tracker.

At the completed post-control/pre-projectile phase, actual accepted placement,
controls and finite biped facts must match that exact latest post-fence input.
Only then can its client source frame become the immutable FirstSourceFrame.
Authority NetFrame and custom action timestamps never become source birth.
Existing engine admission pins bootstrap/manifest/connection/open-world/first-source
context and reads the retained pose, permissions, canonical grants and geometry.
Readiness retirement or an observed replacement publication refuses further use;
restoring an old publication cannot reopen it.

The adapter automatically binds the real current foreground life at this first
qualified phase. It grants the initial unequipped4/16 inventory once. Subsequent
source observations retain existing negative-permission/reload-cancellation
semantics; a rejected pose does not create the first boundary. Source histories
continue to retain only actual engine-consumed inputs, without inventing missing
frames or replaying movement.

Pool/flight retirement now preserves an independent eight-slot occupant source
clock and life grant fence. Spectating, actor retirement or transport/readiness
changes cannot refill the same life. A real newer life can receive its one initial
loadout while retaining the original clock anchor. A different occupant generation
gets a new clock. A read-only life-bind preflight rejects consumed lives and invalid
clock positions before allocating a new pool, payment store or journal; repeated
same-life rejoin attempts therefore allocate nothing after warmup.

Validation includes actual UDP ready/input handlers connected to a running native
dedicated scene. Manual clients acknowledge native loading; the server constructs
and steps the real engine. An existing snapshot-sink wrapper reads mutable engine
state only on its owner thread and publishes immutable observations to the test
coordinator. Other tests exercise real automatic life binding, native respawn,
spectator restoration and canonical M7 damage through the existing journal seam.
The scene fixture's accepted-input publication is explicitly trusted and is not a
UDP claim. No rendered client, custom action/outcome packets, local/bot parity,
source recovery, latency fairness or custom snapshots are delivered by this slice.
CW-04 remains incomplete until those acceptance gates are met.
