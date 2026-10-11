# Netcode 2.0 — N7 self and splash damage

N7 builds on merged N6/main `a6883e50`. Branch:
`codex/net2-07-self-and-splash`. Live protocol is **47**; peers must update
together. The implementation passes the focused and short Mac UDP gates described
below. PR review and merge remain pending.

## Behavior

Self claims name the full authority stream, shooter generation/life, emitted
shot and component, with the same exact victim identity. An unpaid self splash
rescue requires independently verified emitted fire and a real authority
explosion witness. The witness captures the authority owner body, explosion
centre, radius, damage curve, source modifiers and eligibility at impact.
A direct collision excluding the owner from native splash cannot become an
unpaid splash rescue. Capacity exhaustion and missing or expired evidence fail
closed; explosion witnesses are bounded to 512 entries per owner and 512 frames.

The client hit point is only a bounded plausibility witness. Paid falloff and
impulse use the captured authority body and explosion centre. Rescue derives
damage, handicap reduction, Enhanced Samus jump scaling and afflictions from
authority evidence rather than accepting a client damage multiplier or impulse.
The claim's affliction flags must match the witness. The shared component payment
reservation prevents native and rescued damage from paying the same component
again. Immunity, native self-damage rules and existing arbitration grace remain
part of settlement.

Supported self claim shapes include Missile, Magmaul, Judicator and Omega Cannon.
A matching native payment can return its exact historical outcome, including
for a direct self collision. Unpaid direct self rescue is explicitly refused.
Omega's source admission remains unavailable, so unpaid Omega rescue fails
closed; accepting the shape does not manufacture emission proof or promise
Omega rescue coverage.

Bombs remain on their native placement and damage paths. Their captured owner
stream/generation prevents stale bombs from damaging a replacement occupant's
match or receiving replacement credit. Clients cannot predict an old-life bomb
as their new life's placement; the authority can retain a same-occupant old-life
bomb within the same stream. N8 still owns admitted bomb placement identities,
placement replay, and placement/component reconciliation. N7 does not claim
those mechanisms are implemented.

Burn stores original shot/component, owner generation/life, weapon, launch
frame, duration, tick cadence and launch double-damage state as values. Projectile
pool reuse, source respawn and powerup changes cannot rewrite that source.
Replacement occupants cannot inherit burn credit. Body and turret burn ticks
resolve only on the authority in live network play; per-frame source/victim
identity prevents duplicate ticks. Burn damage stamps death arbitration with
the current authority tick while preserving original source identity for replay
and attribution, without becoming a reusable projectile payment. Spawn/death
clear body burn ownership; turret destruction clears turret burn. Zero-shot
legacy/offline sources remain refreshable rather than being deduplicated as one
network shot.

Local self damage and jump impulse are predicted immediately. A claimed lethal
self hit holds health at one until authority resolution. Self health holds its
presentation floor without subtracting a paid snapshot's damage twice, and
remaining exact self claims retain their floor as individual ACKs settle.
N6 healing credit still follows its existing exact paid-tick receipts.

An ACK retires only its named claim and emitted shot. An authority self event
that arrives before its local projectile collision is retained as already
applied. An ambiguous later collision still authors its full exact claim but
defers another local health debit and jump impulse. Its receipt joins the event
by exact damage sequence rather than FIFO order; it cannot replay the authority
impulse again. Deferred claims preserve the floor of other pending self hits
when their ACK arrives, because they did not spend that health locally.

Snapshot-first nonlethal
self feedback waits for an exact receipt rather than consuming another pending
self hit. Legacy self confirmation is blocked while an unresolved exact claim
for that weapon could own the event. When the matching event is available, the
owner applies actual impulse minus its already-predicted impulse once. Deferred
feedback is lifecycle-fenced and bounded by a 240-frame deadline; it is not an
unbounded promise to recover every presentation event. Environment damage and
burn are authority-resolved rather than predicted as projectile self damage.

The new self claim/rescue path is default-on. Setting `PRIME_SELF_SPLASH=0`
disables its claimability and authority explosion witness/rescue checks; it is
a guarded compatibility fallback, not a replacement for payment evidence.

## Validation

Validation ran on this Mac arm64 using .NET SDK 10.0.401 / runtime 10.0.12.
The tested gameplay source is `50bb438e382f12120e70f13ce9ce3208e29a3552`
(attempt 13). Final source `719a3c5674e9c7e477aaf0b6d3355d25389163d1`
(attempt 14) changes only the opt-in wall test driver's aim. Both Release builds
have zero errors and 43 warnings. No Windows-specific change needed a Windows
run. No Linux/Android, GitHub Actions, GPU validation or long soak was run.

The focused suites pass 55 self prediction assertions, 22 full damage-Replay
ordering assertions, 353 asset-backed self/splash assertions, 50 combat
reconciliation assertions, 94 combat arbitration asset assertions, 44 continuous
healing assertions and 337 snapshot assertions (1,172-byte maximum fast packet).
Shot proof passes 324 assertions plus 43 input-proof asset assertions; Protocol 19
assets pass 60. Architecture passes, the health/shot suite passes 3,338,739
assertions across 7,776 profiles, and Python evidence checks pass 165 tests.

The asset fixtures cover supported weapons and charge/affinity variants, native
and rescued payment ordering, independent chained shots, team modes, immunity at
impact, moving bodies, wall self-kills, forged geometry/source/component claims,
and actual bomb damage/jump lifecycle fences. Burn checks preserve the original
source through projectile removal, owner respawn and disconnect/replacement,
including lethal attribution and current-time death arbitration. Full Replay
checks cover authority-event-before-prediction, snapshot-before-ACK and
ACK-before-snapshot. Separate pending-claim tests cover accepted/rejected ACKs
without charging an already-applied hit again.

Both short real-UDP controls passed the unchanged campaign adjudicator and the
exact N7 payment/receipt/impulse audit:

| Scenario | Fault profile | Active time | Self payments / damage | Self deaths | Exact ACKs / impulse checks |
| --- | --- | --- | --- | --- | --- |
| Moving rocket jumps, attempt 13 | LAN | 25 s | 20 / 396 | 4 | 16 / 16 |
| Wall self-kills and respawns, attempt 14 | Moderate | 25 s | 20 / 396 | 4 | 20 / 16 |

Each uses two players, real Missile controls and world collisions, native deaths
and respawns, and an 18-second maximum drain budget. The test driver equips and
replenishes the weapon; it does not inject damage, health, death or player
position. Moderate uses 100 ms RTT, 20 ms jitter and 1% loss. The wall driver aims
at the wall's base so elevated respawns exercise the native body blast radius.

The moving run exercised authority-first feedback; the impaired wall run
exercised predicted-first feedback. Every accepted receipt joins its exact
server payment, and every recorded impulse correction matches the authoritative
impulse minus effects already applied. Both runs have zero duplicate payments,
refused-but-paid contradictions, pending self claims, delivery gaps, sampled
health/score mismatches or drain failures. One additional wall prediction had
no matching server payment and failed the constrained rescue policy; its refusal
retired cleanly. This is an authority correction, not evidence that every local
collision must be accepted. Each run paid self damage across four owner lives.

## Evidence and limits

The tracked ledger is
[`net2-n7-20261011.json`](../tools/nettest/baselines/net2-n7-20261011.json).
It records exact commands, source commits, binary hashes, passing checks and
failed development attempts. Ignored raw evidence remains under
`artifacts/net2-n7`; final manifests and audits are hashed in the ledger.

Attempt 09 never aligned the test gun and produced no combat. Its baseline-only
semantic frontier also exposed a diagnostic drain false-negative; the workload
was independently incomplete, and no drain gate was relaxed. Attempt 10 passed
the general UDP checks but failed N7-specific coverage: the moving run exposed
the authority-first prediction race, and the wall run did not damage the second
life. Attempt 13 fixed the gameplay race and passed moving combat; attempt 14
corrected only the wall driver's aim and completed wall coverage. All earlier
results remain recorded as failures or incomplete evidence, not acceptance.

These short controls establish the exercised authoritative transactions and
ordering cases. They do not measure every intermediate rendered health-bar
frame, global client/server movement equality, GPU presentation, every weapon
under live UDP, long sessions, or performance improvement. Zero extra debit and
impulse for accepted self hits is checked by deterministic ordering tests and
exact UDP receipts; arbitrary mispredictions can still receive honest authority
corrections. Unpaid Omega rescue and N8 bomb placement/component admission remain
fail-closed or deferred as described above. N4 admission remains in shadow mode.
