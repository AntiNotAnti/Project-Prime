# Netcode 2.0 — N8 canonical bombs

N8 builds on merged N7/main `3613b3d6`. Branch:
`codex/net2-08-bomb-state`. Live protocol is **48**; compatible peers must
update together. Review and merge remain pending.

## Behavior

Bomb placement and detonation requests use a dedicated reliable lane. Every
request and bomb carries the match, authority epoch, owner slot, occupant
generation, owner life and bomb sequence. The existing 1,175-byte player intent
does not grow. Remote movement/button replication cannot create damaging bombs.

The authority waits for the request's movement/form state, then checks the
native form, ability, inventory, cooldown, bomb cap, placement distance and line
of sight. The native placement path consumes inventory and establishes links.
A Lockjaw detonation requires all three registered bombs to belong to the
current owner life; validation completes before changing any countdown.
Only one unresolved owner request is allowed, and stale-life requests receive
an explicit refusal when replaced by a new-life request.

The authority retains 2,048 ordered placement, refusal and terminal facts per
stream. Three bounded state pages recover up to 24 live bombs, including their
positions, orientations, velocities, countdowns and exact Lockjaw endpoints.
Each page is at most 696 bytes. Late admission includes a frozen bomb baseline
and its event frontier before the client enters the match. A history overrun
fails visibly instead of silently losing a terminal event.

Clients materialize canonical ghosts from these facts and snapshots. They do
not independently move, touch-detonate or damage other players with those
ghosts. Reliable birth/terminal ordering, snapshot timestamps and terminal
tombstones prevent duplicates and resurrection. Owner bomb-jump presentation
is tied to the accepted terminal and current owner life. Samus and Kanden bombs
can retain their original ownership after the same occupant respawns; retired
Lockjaw links cannot use the replacement life's native link array.

Canonical Samus and Lockjaw body effects exist before the first bootstrap draw.
Their initial particle emission, and that of canonical explosions, remains
pending until an effect pass actually runs. This handles late bootstrap and
multiple simulation steps before a render without dropping zero-age emitters.
A draw with no simulation step initializes only pending canonical births;
it does not advance existing or ordinary native effects. Replay checkpoints retain the
pending visual birth, canonical manager frontier, buffered events, live snapshot
assembly and terminal tombstones. Older replay protocols retain native bombs.

## Validation and evidence

Validation uses this Mac arm64 and extracted local game assets. No Linux,
GitHub Actions or long soak is used. No platform-specific implementation change
requires a Windows run.

The short campaigns use native morph/attack/movement controls and real UDP.
The fault profile is 400 ms RTT, 80 ms jitter, 5% loss, 3% reorder and 1%
duplication, with two participants and a spectator joining after four or six
seconds, as recorded per run.
Samus and Kanden use ordinary spawn positions. The explicit Lockjaw triangle
fixture starts participants on collision-verified clear floor: the authority
sets initial positions once, and each participant adopts its own initial
snapshot pose. Subsequent movement and all placements use normal controls;
no damage, bomb countdowns or admission outcomes are injected. This is an
isolated geometry fixture, not a claim of navigation coverage. Exact request/event
accounting,
positive authority damage, client health/score parity, late-spectator recovery
and a frozen, empty drain are separate checks. Missing damage or an undrained
suffix remains incomplete evidence.

The focused suite includes 245 Python audit/runner checks, 49 codec assertions,
47 asset-backed bomb assertions (including native damaging three-bomb snare), 64 effect birth/draw/checkpoint
assertions, 2,896 replay format checks, replay controls and existing network
architecture/bootstrap/prediction checks. The full source and binary identities
for each run are recorded in the ledger; later test-rig changes do not replace
earlier tested production code.

Rendered checks distinguish actual RGB pixel changes from entity existence or
draw submissions. The isolated canonical fixture checks five bomb bodies and
three Lockjaw edges in clear view and behind a depth-tested room wall. Live
network observations, where available, are identified separately by full bomb
key. Wire payload comparison remains exact. For live captured orientations,
the audit reconstructs the engine's float32 cross/normalize transform and
allows one ULP of arithmetic rounding around that derived orientation; all
other state fields, keys and source frames remain exact.

Native bombs can be created and detonate within one tick. If both accepted
facts arrive before any body draw, or an accepted snapshot already retires the
bomb before that draw, the diagnostic records the exact lifecycle and requires
positive pixels from the matching canonical explosion instead. A retirement
snapshot must cover the terminal frame/frontier and omit that key. This is
reported separately from persistent body pixels. It cannot replace a failed
or incomplete body attempt, a missing expected ghost, or any prior live-body
draw opportunity. The effect registration is diagnostic only and preserves
native effect ownership, expiry and physics.

The final Kanden run uses same-draw explosion witnesses for two peer/key pairs.
The separately validated retirement-snapshot classifications have unit coverage;
they were not needed by that passing campaign. Earlier incomplete runs retain
their original verdicts and are not retroactively counted as passes.

These diagnostics do not measure performance or establish universal
visibility before a delayed network event arrives.

Acceptance evidence includes:

| Check | Result |
| --- | --- |
| Samus rendered UDP, 20 seconds | PASS: 18 placements/terminals, 96 actual HP damage, all required body keys visible on all peers |
| Lockjaw rendered UDP, 24 seconds | PASS: three admitted bombs/terminals, 99 actual HP damage, all three bodies and links visible on all peers |
| Kanden rendered UDP, 20 seconds | PASS: 14 placements/terminals, 99 actual HP damage; five damaging keys covered on every peer (bodies or exact coalesced explosion pixels) |
| Isolated native GPU body/link fixture | PASS: 16 positive/wall-negative cases, 48 PNGs |

Final commands, source/binary hashes and acceptance results are recorded in
[the N8 evidence ledger](../tools/nettest/baselines/net2-n8-20261011.json).
Development failures remain retained: an initial body test
caught missing first-step initialization; the realtime GPU control then caught
zero-age effects lost during simulation catch-up. Early Sanctum campaigns did
not produce sufficient contact damage, and an 18-second Lockjaw drain was too
short for a remaining native bomb. The initial triangle movement test used a
blocked spawn location and produced no bomb workload. Those runs are retained
as failed or incomplete evidence, never counted as acceptance.
