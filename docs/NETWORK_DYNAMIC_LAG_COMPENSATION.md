# Network Dynamic Lag Compensation

## Authority and query timeline

The server computes the compensated hit once. A shot's server-owned action tick
selects the historical player and dynamic-world snapshots; a client cannot
select an unrestricted rewind tick.

For projectile catch-up, a projectile that collided at `T` is advanced through
`T+1` to `N`. Each catch-up step reads the matching historical dynamic state.
The completed current tick `N` remains live. After catch-up drains, the worker
records player and dynamic history for the same authoritative boundary.

The live `Scene` is never rewound. Each `MatchInstance` owns its own fixed
registry and 32-tick ring, so two matches cannot observe or mutate one
another's collision history.

## Historical geometry

Queries combine immutable static room geometry with the historical registered
dynamic sources. Door and force-field tests use the exact state fields consumed
by the current beam path. Transformable Object/Platform collision uses the
registry-owned immutable collision shape with the recorded transform and
inverse; current broadphase membership and live transforms are not consulted.
Static candidates and duplicate tracking use a pre-sized workspace owned by the
match's historical query engine. After room setup, warmed queries allocate no
managed heap memory, and a fail-fast lease prevents recursive reuse.

If a historical state is missing, the server uses the exact current registered
collider state and increments the missing diagnostic. A removed/replaced
registration has no safe current fallback and is treated as an unknown blocker;
an active collider with an unknown shape follows the same fail-closed rule.

## Metrics

The worker exposes match-owned counters for dynamic records, queries, and
missing states, plus door, force-field, object, and platform query counts. A
side-effect-free current-versus-historical nearest comparison increments
`HistoricalGeometryChangedOutcome` when the selected collision source changes.
It does not execute damage, score, callbacks, or combat resolution twice.

## Developer diagnostics

The authenticated worker host can request `netdebug lagcomp-history` or
`netdebug lagcomp-dynamic` through the lane-affine
`WorkerRuntime.NetDebugAsync` surface. For an in-game view, configuring
`Node:HostAdmin:TokenFile` maps an HTTPS-only bearer-authenticated
`POST /v1/host/matches/{matchId}/lagcomp-debug` endpoint. Its bounded JSON
accepts enable/refresh for `history` or `dynamic`, or clear, plus one frozen
human seat. It is capped at 512 request bytes and 30 requests/minute per source
address; without a configured token the endpoint does not exist.

The Node constructs and routes `AdminAction.LagCompHistory`,
`AdminAction.LagCompDynamic`, or `AdminAction.LagCompClear` through the
authenticated Node-to-Worker pipe. This is deliberately not a public
player/control-socket command. The worker then sends a single bounded
`NetMessageType.Debug` datagram containing only server-selected facts. A client
never sends a debug request and cannot select a rewind tick, path, collider, or
recipient.

`lagcomp-history` renders up to eight historical player volumes;
`lagcomp-dynamic` renders up to eight historical door planes, force-field
rectangles, and transformable bounds. Both render the server-selected
projectile segment when one exists, and the HUD reports the current (`N`),
query (`Q`), and rewind (`R`) ticks plus bounded record/query/missing/door/
force-field/platform/changed-outcome counters. The payload is capped at 996
bytes (1,020 bytes with the network header), and truncation is explicit. Clear
uses one canonical empty packet. The existing `WorkerRuntime.NetDebugAsync`
text result remains available for host logs.

An end-to-end test covers Node HTTPS authentication, Node-to-Worker routing,
the real Worker datagram, client parsing, mode refresh, and clear. This remains
a bounded local diagnostic presentation only; it does not alter gameplay or
enable dynamic collision. Transport behavior and rendered usefulness under
delay, jitter, loss, and reconnect remain part of the separate QZ1-E WAN
validation.

The bounded diagnostic sampler is kind-diverse: it selects one active door,
force field, object, and platform before filling the remaining eight-entry
budget in registry order. This keeps a large first kind from hiding every
later kind; truncation remains explicit.

## QZ1-E deterministic comparison check

The bounded `nettest` check is available as:

```bash
dotnet run --project tools/nettest/nettest.csproj -- \
  --dynamic-lagcomp-comparison /tmp/qz1e-dynamic-lagcomp.json
```

This check uses deterministic authoritative facts built through the public
`HistoricalCollisionState` door/force-field factories and the
`HistoricalCollisionQuery`/`HistoricalCollisionResult` contracts. It compares
the current and shot-tick facts for three deliberately contradictory
transitions:

* `FalseWallBlock`: the current door is closed, while the shot-tick door was
  open.
* `ShotThroughClosedDoor`: the current door is open, while the shot-tick door
  was closed.
* `ForceFieldContradiction`: the current field is inactive, while the shot-tick
  field was active.

The tuple in the table is `false-wall / through-closed-door /
force-field-contradiction`; `changed` is the historical geometry outcome count.
The first two modes use current dynamic geometry, while the third selects the
historical dynamic state. All rows passed the command's deterministic
assertions.

| Echo delay | Query rewind | Lag compensation off | Historical players only | Historical players + dynamic geometry |
| ---: | ---: | :---: | :---: | :---: |
| 0 ms | 0 ticks | 0 / 0 / 0; changed=0 | 0 / 0 / 0; changed=0 | 0 / 0 / 0; changed=0 |
| 50 ms | 3 ticks | 1 / 1 / 1; changed=0 | 1 / 1 / 1; changed=0 | 0 / 0 / 0; changed=3 |
| 100 ms | 6 ticks | 1 / 1 / 1; changed=0 | 1 / 1 / 1; changed=0 | 0 / 0 / 0; changed=3 |
| 150 ms | 9 ticks | 1 / 1 / 1; changed=0 | 1 / 1 / 1; changed=0 | 0 / 0 / 0; changed=3 |
| 200 ms | 12 ticks | 1 / 1 / 1; changed=0 | 1 / 1 / 1; changed=0 | 0 / 0 / 0; changed=3 |

This is a contract-level simulation, not a loopback transport test. The
worker query engine is match/`Scene`-owned and intentionally remains separate
from this data-free nettest fixture; the fixture therefore proves the bounded
comparison and metric classification, not the live worker's collision result.
The report is explicitly marked `RenderedWanProof=false`, uses no packet
impairment, and does not enable QZ1 by default.

STOP: do not enable or ship dynamic-geometry adoption based on this table.
Static or deterministic contract-level results are not rendered WAN proof.

## Rendered loopback fixture matrix

`--rendered-wan-validation` also supports the explicit compiled developer
fixture `--fixture unit1-rm1-dynamic`. The ID selects one fingerprint-bound
AMHE1 descriptor; it cannot name paths or JSON. It is absent from the normal
room and Worker content catalogs and is accepted only by an isolated loopback
Worker with one lane, one match, two seats, and the exact production Node
handoff shape. A second match identity is rejected.

The fixture source contains 114 entity records. Its private scene-load path
admits exactly 46 records: two active player spawns, 10 doors, 11 force fields,
three platforms, and 20 objects. The other 68 single-player-only records are
intentionally omitted. The historical registry then contains 10 doors, 11
force fields, nine object collision slots, and three platforms. This is a
developer validation scenario, not an advertised production multiplayer room;
its mutable state is intentionally not emitted by production world
replication. The renderer consumes the authenticated server diagnostic facts.

On 2026-09-09, the `off|players|dynamic` by `0|50|100|150|200 ms` matrix under
`output/qz-wan-validation-20260909/fixture-matrix-v2` passed all 15 cells through
the production Node handoff, an external Worker, a real SDL/Metal client, and a
same-session reconnect. It produced 10,800 acknowledged frames and 180 saved
nonblack final-frame PNGs with no capture failures. All 15 reconnects retained
the Node session, match, and seat while rotating the connection identity.

The dynamic-mode cells recorded 3,795 historical dynamic queries with zero
missing samples: 1,150 door, 1,265 force-field, and 345 platform queries. The
fixture was therefore loaded, recorded, queried, transported, and presented.
It naturally produced zero `HistoricalGeometryChangedOutcome` events, because
the omitted trigger/path entities do not deterministically drive the admitted
mutable entities through a contradictory state while the scripted projectile
crosses them. The separate deterministic matrix directly proves the intended
50-200 ms false-wall/door/field outcome reduction from three contradictions to
zero; the rendered matrix proves the real integration path, not that those
contradictions occurred in the rendered room.

Every rendered report keeps `renderedWanProof=false`, `qz1Accepted=false`, and
`requiresHumanVisualReview=true`. Process-local NetLag is controlled local
impairment, not an internet-path measurement. Historical dynamic collision
therefore remains opt-in; the default `players` Worker mode keeps dynamic
geometry compensation disabled.

The split operator/client/merge harness is now available for the remaining
two-physical-endpoint validation gate. The ordinary `MP1 SANCTORUS` same-host
smoke in `output/qz-wan-validation-20260909/split-loopback-smoke-8` passed its
production Node/external-Worker/render/reconnect path and strict offline merge,
but is labeled `same-host-split-smoke-non-wan`. No genuine WAN-path evidence or
human visual acceptance was produced; `renderedWanProof`, QZ1 acceptance, and
QZ5 acceptance remain false.

## Scope boundaries

This pass does not change weapon policy, add client prediction, or add a
gameplay-authoritative visualization packet. The diagnostic snapshot API and
debug datagram are bounded server facts only. Force-field locks remain current
target/hurt entities, and no new historical homing LOS rule is introduced.
Genuine WAN validation, full human visual review, and default enablement remain
later validation gates.
