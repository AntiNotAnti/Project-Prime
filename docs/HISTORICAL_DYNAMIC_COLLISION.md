# Historical Dynamic Collision

Project Prime's server lag compensation keeps the live `Scene` at the current
authoritative tick. It never rewinds entity transforms or shares a mutable
world-wide rewind between matches.

## Scope

The worker registers a fixed set of beam-relevant dynamic collision sources
after a room is loaded:

| Source | Snapshot facts | Historical query |
| --- | --- | --- |
| Door | open/blocking state, facing vector, lock position, radius squared | closed-door plane and radius test |
| Force field | active state, plane, position, up/right vectors, width, height | active rectangle plane test |
| Object / Platform `EntityCollision` | active state, immutable shape reference, transform, inverse, bounds | shape query using the recorded transform |

Immutable room collision remains shared by all ticks. Object and platform
queries traverse the registry's shape references directly; they do not use the
current entity broadphase and do not write to a live `EntityCollision`.

Each historical query engine owns one pre-sized static-collision workspace.
Room setup computes a hard upper bound from the immutable collision entries and
preallocates both candidate storage and duplicate tracking. Warmed historical
queries therefore allocate no managed heap memory. The workspace is leased for
one synchronous query at a time and rejects re-entry; this preserves the
single-writer `MatchInstance` ownership model instead of introducing shared
scratch state or locks.

Transformable sources are rejected with a deterministic segment-versus-recorded
bounds test before their immutable faces are traversed. Focused coverage walks a
64-source registry, proves that 63 outside shapes perform no face query while
the one inside shape still wins, and preserves the warmed zero-allocation
contract. This is an operation-count regression rather than a timing benchmark:
representative-content CPU profiling remains an unmeasured performance gate,
because a wall-clock threshold in the unit suite would be environment-sensitive.

The registry uses entity id plus a registry generation. A removed/recreated
entity cannot inherit a prior historical callback. Registration is fixed after
scene load and bounded by a hard cap (256 by default). Overflow is explicit in
`RegistrationRejected`; an overflowed match keeps the current live beam path
until it can be configured with a supported registry. Unsupported or missing
historical geometry fails closed using the current registered state and
increments the missing metric.

## Ordering and side effects

Nearest collision ordering is deterministic. Static room geometry is evaluated
first, then Object/Platform shapes, doors, and force fields. Equal distances
retain the first result, matching the existing strict-less-than beam ordering.
Historical collision callbacks are side-effect guarded by the same id and
generation. Door unlock/shot-open and force-field lock mutation are not applied
from an old snapshot. A matching Object/Platform may receive the existing
reflection/message callback; a removed or replaced entity receives no callback,
while its historical blocker still wins the query.

Force-field locks are not historical blockers. They are separate target/hurt
entities and remain current-tick gameplay targets. This pass also does not add
historical line-of-sight rules to homing: the existing homing target policy is
preserved because it does not perform a new beam LOS mechanic.

## Recording boundary

`DynamicCollisionHistory` stores 32 completed ticks per registered collider.
The worker records dynamic state once, after `Scene.StepHeadlessFrame()` and
after projectile catch-up drains, at the same boundary used for player history.
Catch-up queries use `T+1..N`; current tick `N` remains live. Recording is a
fixed-array value write and introduces no per-tick heap allocation.

## Diagnostics

The worker's authenticated host-side command surface accepts:

```text
netdebug lagcomp-history
netdebug lagcomp-dynamic
```

`WorkerRuntime.NetDebugAsync` executes either command on the match's simulation
lane and returns bounded text (16 KiB maximum). For in-game presentation, set
`Node:HostAdmin:TokenFile` to a file containing a 32..256 character printable
ASCII token. The Node then exposes this HTTPS-only, bearer-authenticated route:

```text
POST /v1/host/matches/{matchId}/lagcomp-debug
{"action":"enable","mode":"history","seat":0}
{"action":"refresh","mode":"dynamic","seat":0}
{"action":"clear","seat":0}
```

Requests are capped at 512 bytes and 30 requests/minute per source address. If
no token file is configured, the endpoint is not mapped. The coordinator also
rejects stale matches and seats outside the frozen human roster. The route uses
`AdminAction.LagCompHistory`, `AdminAction.LagCompDynamic`, or
`AdminAction.LagCompClear` over the authenticated Node-to-Worker pipe; it is not
registered on the public player control socket.

The worker selects the latest server-observed path (or an explicit empty-path
marker), current/query/rewind ticks, and sends one fixed-shape
`NetMessageType.Debug` datagram to that seat. The payload is at most 996 bytes
(1,020 bytes with the network header): history carries up to eight player
volumes, while dynamic carries up to eight door/force-field/object/platform
facts. It has no client request form and no rewind or collider-selection fields.

The client consumes the packet as presentation state only. The existing world
volume submission path renders player bounds, door planes, force-field
rectangles, transformable bounds, and the projectile path; the existing HUD
text path shows `N<current> Q<query> R<rewind>` plus bounded
record/query/missing/door/force-field/platform/changed-outcome counters. A
canonical clear packet removes the client presentation. This is deliberately a
host-authorized diagnostic surface, not a gameplay-authoritative client path.
`ServerCombat.CopyHistoricalDebugSnapshot` remains the side-effect-free span
API for structured server consumers.

An end-to-end test exercises Node HTTPS authentication, Node-to-Worker routing,
the real Worker datagram, client parsing, mode refresh, and clear. This is local
vertical evidence only: transport behavior and rendered usefulness under delay,
jitter, loss, and reconnect remain part of the separate QZ1-E WAN validation.
It does not justify enabling dynamic collision by default.
