# N0 delivery repair and follow-up validation — 2026-10-10

N0 remains blocked on combat/presentation gaps. This follow-up repairs the
reproduced reliable-delivery failure and investigates shot gaps without changing
combat authority or converting missing shots into accepted results. The
[initial validation](netcode2-n0-validation.md) and its raw attempts are retained.

## Repairs

The diagnostic-only build `04c0a651` reproduced all eight reliable failures on
the Mac in a 25-second Shock Coil campaign. Each failed admission was a critical
`SessionState` packet: only 1–13 events remained pending, but the oldest event
held the **256-event deduplication span** open. The ordinary pending queue was
not full. The client admitted only 100 background packets/second, while eight
continuous weapons can generate 480 shots/second and both semantic/replay facts.
Retries were throttled, then a critical state update failed span admission.

Build `4e38efe2d299e74b337a4f2a45cabfefe87203ff` gives an admitted server-to-client
connection a separate, bounded authority-fact budget: 1,920 packets/second,
128 burst. This matches the existing 32-background-packets/frame drain at 60 Hz.
It applies only to semantic events, awards and replay shot facts. Client-to-server
background admission remains 100/second, burst 32. Pending queues, dedup history,
inboxes, critical reserves, protocol 41 and wire/replay layouts are unchanged.
Both direct Welcome and validated queue promotion establish the directional flag.

The same build fixes graceful close: a retired connection with no outbound
reliable work must retain an outstanding ACK for the received `Bye` until the
socket worker sends it. Previously the connection could be removed before its
ACK was sent, causing the closing peer to report a timeout. Existing retirement
expiry remains bounded. Diagnostics retain the first admission/expiry failure
reason and report its reliable-channel counters.

Two regression checks exercise sustained eight-shooter fact admission with
bounded bursts and unchanged client limits, and real UDP retirement before ACK
maintenance. Existing loss/jitter/reorder, saturation, dedup and queue checks
remain in place. The intentional critical-saturation negative test still logs a
reliable failure; that suite log is distinct from campaign evidence.

## Test driver and evidence

Capture/bot driver build: `fbaa14c499f6836ea54a7e9c1d5b1dae46209424`.
Final headless driver/shot context build: `32f2b65c5967490c2740e40b3f8aec9ec92b9dce`.
Only the user's macOS arm64 and Windows x64 machines were used; CI was skipped.
The user clarified that Windows cannot support eight local clients. Its completed
short eight-client overload attempt is retained, but excluded from performance
comparisons. The Mac runs eight clients; Windows runs two for endurance.

The original soak attempted lobby-owner bot commands on a continuous server,
which has no lobby owner. The opt-in authority driver now adds a bot after seven
peers finish loading, retains it for ten seconds, removes it through the existing
bot lifecycle, and publishes the roster. Both evidence and soak flags are required.
The runner waits for removal before its late join (with a bounded fallback), then
starts the reconnect 210 seconds after that join. No ordinary server gains this
behavior. The soak capture bound is 1,048,576 rows per stream/dedup set to retain
20–30 minutes of repeated shot evidence; ordinary diagnostic runs retain the
262,144 bound. Overflow still fails adjudication; transport/gameplay bounds do
not change.

Capture now records `phase-skipped` when the existing continuous-shot path
consumes a retained tick whose phase differs from the current carrier. Offline
context joins complete shot and actor identities, reports whether missing view
commits had arrival evidence, counts recorded phase skips, and relates authority
commit frames to sampled authority death frames. These are explanatory fields;
all missing-shot failures remain failures. Three new adjudicator regressions
prevent phase skips, death proximity or a different life from waiving a gap.

## Short controls after the delivery fix

Each campaign is 60 seconds, lossless loopback UDP, separate processes and
isolated user data. Counts are offered / authority accepted / authority committed.
“Missing view commits” aggregates per-view gaps, not unique shots or a claim
about on-screen projectile visibility.

| Host / workload | Shot counts | Missing view commits | Matched health / mismatches | Result |
| --- | --- | --- | --- | --- |
| Mac 2, missiles | 75 / 75 / 75 | 0 | 2,384 / 0 | PASS |
| Windows 2, missiles | 75 / 75 / 75 | 1 | 2,072 / 0 | FAIL: view gap |
| Mac 4, tour | 460 / 460 / 459 | 8 | 9,490 / 0 | FAIL: shot gaps |
| Windows 4, tour | 682 / 682 / 620 | 109 | 7,968 / 0 | FAIL: shot gaps |
| Mac 8, Shock Coil | 23,435 / 23,435 / 23,032 | 2,304 | 37,702 / 0 | FAIL: shot gaps |
| Windows 8, overloaded, excluded | 27,732 / 27,334 / 34 | no valid interval | 144 / 0 | FAIL: authority advanced only 215 frames; four shutdown timeouts |

The Mac eight-client control had zero queue drops and all 182,322 expected
per-view semantic facts matched within the declared comparison intervals. No
reliable-control failure or shutdown timeout appeared in the Mac 2/4/8 or
Windows 2/4 campaigns. All their expected semantic facts matched. Windows's
8-client case has no meaningful comparable semantic interval and proves no
sustained-load or performance gate.

## Shot investigation

All uncommitted offered shots in the Mac 4/8 and Windows 4 short controls were
**received Shock Coil continuous ticks** (1, 403 and 62 respectively). In the
original Windows four-client attempt, all 92 uncommitted shots had the same
classification. `NetFireEvents.Prepare` explicitly consumes older continuous
phases without selecting them; intent coalescing or slow simulation can therefore
produce accepted-but-uncommitted ticks. The new phase-skip records distinguish
this path from other non-commit causes in the long run. In the interrupted
rotation attempt, 437 of 454 received-but-uncommitted authority shots had a
matching phase-skip record. The crashed spectator view had 807 phase skips among
814 received-but-uncommitted shots; the late joiner had 748 among 763. Those
counts concern received shots, not the complete owner-offered parity denominator.

Every missing view commit in the Mac 4/8 and Windows 2/4 controls had arrived in
that view (or was locally offered). The Windows two-player missile gap was within
12 authority frames of the shooter's first sampled dead state. The original
rendered Windows shot `(639272560644630077, 2, 0, 1, 1, 14)` also reached both
victim and spectator: authority commit 2090, first sampled death 2094. Shot 13
committed in every view; shots in the subsequent life also committed.

This supports a separate death/intent presentation-ordering investigation.
Snapshots apply authoritative death to puppets while fire reconstruction relies
on their current simulation state; arrival alone does not ensure a live firing
step. Sampled death proximity does **not** establish exact client ordering or
prove a visually absent projectile. Cooldown/ammo/weapon transitions and other
unrecorded rejection paths remain possible for gaps without direct phase proof.
No historical damage was replayed and no death or shot-parity check was waived.
The remaining ticks need explicit firing/expiry/rejection semantics; blindly
replaying an old continuous phase against a current target would change combat.
Death-boundary presentation also needs an event-ordering repair that does not
revive a dead player or reapply damage.

## Long runs and remaining gates

The first follow-up rotation soak exposed two additional blockers. At spectator
rejoin (150 seconds), the headless client consumed a presentation camera request,
entered `CameraMode.Player`, and tried to reset a reticle that `-nographics`
never initialized. The final test driver consumes those camera requests before
simulation, keeping the headless Roam state; gameplay spectator/rejoin requests
still run normally. Evidence campaigns also suppress external career-ticket
acquisition so isolated load tests do not generate identity-service requests.

At the first rotation, the remaining clients waited at the new match's loading
barrier. `Scene.RunSimulationFrame` returns through `PumpLoading` while frozen,
before reaching `NetRoomChange.Sync` in `NetHooks.AfterInput`. This attempt was
stopped and retained as failed; it was not treated as twenty minutes of gameplay.
The lighter Windows rotation attempt was also stopped before replacing it.
A subsequent Windows build ran out of disk space copying dependencies. Its
failed build log is retained. Verified duplicate dependency directories from
completed campaigns were reclaimed after retaining the exact engine DLLs and
hashes; raw evidence and parent builds remain. The retry build passed.
The final long runs deliberately retain one map for 30 minutes: they exercise
sustained delivery, bot/late-join/reconnect and spectator lifecycle, but do not
satisfy the map-rotation gate. A repair to general loading/rotation is separate
work and is not hidden by bypassing the barrier or increasing its timeout.

The final Mac run completed in **1,206.96 seconds**, with 72,144 authority
frames and no simulation-step failures. All participants had zero transport
queue drops, connection-loss/semantic-overrun/evidence-overflow flags, and no
reliable failure or shutdown timeout in their completed logs. Bot occupancy,
spectator and sustained-20-minute coverage passed; map rotation remains NOT_RUN.
Every offered shot reached the authority: **156,346 / 156,346 / 153,002**
offered/accepted/committed. There were **751,312** identical-frame health
comparisons, **zero mismatches**, and **1,215,918** expected per-view semantic
facts with **zero missing or conflicting facts**.

The overall Mac verdict is still **FAIL**: 3,344 owner-offered shots lack an
authority commit and 19,231 expected commits are missing across views. Every one
has arrival evidence. Explicit phase-skip records explain 3,228 authority gaps
and 18,872 view gaps. Another diagnostic counts 512 view gaps within 12 authority
frames of sampled death; that count overlaps phase skips and is not an independent
partition or proof of exact client ordering.

The final Windows **two-client endurance** completed in **1,206.13 seconds**,
with 72,067 authority frames and zero step failures or transport queue drops.
All participants exited normally, with no reliable/semantic failure or shutdown
timeout. All **1,552 / 1,552 / 1,552** offered/accepted/committed shots matched
on the authority. The run matched **41,360** health samples with zero differences
and all **3,222** expected per-view semantic facts without gaps or conflicts.
Its overall verdict remains **FAIL** for **nine** missing victim-view missile
commits. Every one was received, and each authority commit was zero to three
frames before the shooter's first sampled dead state. This is consistent with
the earlier rendered observation; exact client event ordering remains unproven.
The eight-slot-specific `sustained20Minutes` flag is NOT_RUN for this two-player
case, despite the completed twenty-minute duration.

The Mac authority gaps comprise 3,283 Shock Coil ticks, 60 Imperialist shots and
one missile. Beyond the 3,228 recorded phase skips, 116 accepted shots still lack
an explicit non-commit reason (55 continuous ticks, 60 Imperialist shots, one
missile). The view gaps comprise 19,137 continuous ticks, 69 Imperialist shots
and 25 missiles. No exception was added to turn these into a passing campaign.

Final builds, six focused C# suites (`reliable`, `queue-budget`, `architecture`,
`transport-stress`, `health-shots`, `lifecycle`) and all **33** Python adjudicator
regressions pass on both hosts. The short controls use `4e38efe2`; the final long
runs use `32f2b65c`. The Mac final engine SHA-256 is
`d57d8b9b402c07b5cba2a28438bf9fe8f85f4cd9bfbf307419acd5b8a67acd0f`;
Windows is `bef122edea1f12cea76d51d2c31063fea4ec05af76140e6342feeff9f227044e`.
Windows source archive hashes match the local Git archives. Running assembly
hashes were unchanged at campaign completion.

N1 remains blocked by unresolved shot reconstruction/presentation, loading/map
rotation and the original baseline combat/continuous-scene assertions. Precise
Shock Coil drain/kill, Imperialist spawn traps and same-projectile visual
agreement remain NOT_RUN. These are lossless loopback tests; no new impaired
real-UDP campaign or representative control/candidate performance acceptance is
claimed. Source identities, denominators and raw hashes are in the follow-up
ledger; the original ledger remains unchanged.

## Preserved evidence

The follow-up [machine-readable ledger](../tools/nettest/baselines/net2-n0-reliability-20261010.json)
contains eleven attempts, per-view denominators, complete source identities and
raw-file hashes. The local bundle is
`artifacts/net2-n0-fixes-evidence.tar.gz` (290,793,536 bytes), SHA-256
`7732fc0150a15c63b54e024f40ba2dcba3a549bfd73337b8c6a2304da0136708`.
Its index covers 649 files / 3,191,124,788 raw bytes and has SHA-256
`82823abc8fe38a74b92e686092d16e9a45b503f12c66fafcc7330d05d88d64b4`.
The original N0 archive/ledger are unchanged. Source archives and cleanup records
retain the Windows build/evidence chain; all temporary test processes finished.
