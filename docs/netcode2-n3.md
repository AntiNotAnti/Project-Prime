# Netcode 2.0 — N3 retained shot lifecycle

N3 starts at merged N2/main `23a33475bed7549dad1d3b668031000581de8aa2`.
Branch: `codex/net2-03-shot-lifecycle`. Production candidate:
`109463bba640eeb00ba4846a2f2e34ea58f7c073`.

Live protocol changes **42 → 43**. Clients and server must update together.
The accepted discrete-shot path is enabled by default; `PRIME_ACCEPTED_SHOTS=0`
is an explicit comparison switch for the former input-driven playback path.
Campaign reports record the enabled state. The campaign runner clears inherited
experimental environment variables and therefore exercises the normal default.

## Behavior and ownership

A bounded `AcceptedShotLedger` owns both the pending queue and terminal duplicate
history. There is no second queue claiming the same discrete event. Each of eight
slots has 256 cells keyed by authority epoch, match, slot, occupant generation,
source life and shot ID. Pending events drain in source-frame/sequence order;
late arrivals remain eligible without rolling back shots already emitted.
Retention is 64 source frames relative to the newest observed carrier. Conflicts,
capacity refusals, expiry and retirement have explicit outcomes. A pending entry
is never overwritten to make room. Counters satisfy
`Accepted = Fired + Rejected + Expired + Pending`.

Discrete remote shots no longer enter `TryFireWeapon` through a reconstructed
button edge. They use their captured weapon, charge, scope, affinity/Prime and
double-damage state, muzzle/ray, ACK/subframe and fenced homing target. Fresh
per-flight equipment prevents playback from spending present ammo, changing the
selected weapon/zoom, releasing charge, or consuming a new-life enhanced Samus
lock. Samus children use the captured pip count and original Missile equipment.
Whole bursts require free projectile slots before emission; refused spawns become
explicit terminal rejections rather than silently consumed events.

Death does not cancel an accepted shot. Respawn preserves discrete pending shots,
author redundancy and the sequence counter, but clears continuous/turret cadence
and current controls. Old-life carriers can deliver events without changing
movement, inventory, readiness or respawn input. An occupant-generation or stream
change retires previous ownership and resets its observation clock. Projectile
and ricochet identity remains the original full shot key.

Existing projectile creation, collision, lag compensation and damage resolution
remain the authority. Accepted playback supplies source weapon policy and timing
without restoring the old player body. Exact authority-resolved old-life hits
can answer matching duplicate claims after respawn. Unmatched old-life claims
receive `WrongLife`; this does not enable historical damage rescue or headshot
correction. N4/N5 still own broader claim proof and arbitration.

Continuous ticks, turret fire and authoritative bot firing keep their separate
policies. Discrete events do not enter their pending history. Cosmetic shot cues
are restricted to a compatible living body; no old-life input is synthesized.

## Encoding and recordings

Each event is now 74 bytes, including its original life and complete target
slot/generation/life. Noncontinuous phase bits also retain shot-time modifiers.
The live codec requires nonzero source life, finite valid pose and well-formed
target identity. Carriers contain at most 14 events: **1,139 bytes** of intent or
**1,164 bytes** including the relay slot and 24-byte transport header, below the
1,200-byte realtime limit. Idle carriers remain 103/128 bytes.

Canonical records use 16 event cells (1,287 bytes). Readers retain historical
1,175-byte posed and 423-byte legacy event records, with the original strides.
Detached checkpoints advance to schema 7 and read their advertised historical
intent width. Protocol 43 recordings drain all same-frame discrete events and
retain original life identity. Older recordings retain their former firing path
because they lack the new captured context. Seek/reapply guards prevent duplicate
modern playback.

## Validation

Validation results and exact campaign identities are recorded in
`tools/nettest/baselines/net2-n3-20261010.json`. Raw attempts are retained under
`artifacts/net2-n3`. The verified archive is
`artifacts/net2-n3-evidence.tar.gz` (SHA-256
`835a4980693ab83171ccd86cf54f45d26b9e0884b9cf4c7d3a525aa60065bdcc`).
Its index covers 163 files / 79,240,207 bytes; duplicated runtime dependency
directories are omitted and exact tested engine/test binaries are retained.

| Final Mac control | Duration | Server accepted / fired | Result |
| --- | ---: | ---: | --- |
| Eight clients, LAN | 25 s | 114 / 114 | PASS |
| Two clients, 400 ± 80 ms RTT, 5% loss, 3% reorder, 1% duplicate | 25 s | 34 / 34 | PASS |

Both existing campaign adjudications pass, including health and semantic parity.
Every delivered discrete event in every process has one explicit fired outcome;
none is unaccounted, rejected, expired, duplicated, or committed with a different
weapon/charge/kind. The impaired observer received 33 of the server's 34 events;
the remaining shutdown-tail event was not delivered to that observer. Existing
view comparison excludes 30 terminal frames, and semantic comparison excludes
60; these results do not claim delivery after process shutdown. No verdict,
reliable-delivery or socket failure was reported.

The final engine SHA-256 is
`6ad689d22f74547b3a92503e200b57daa44151f1a9645fad88762c179c7875d7`.
Combat checks pass 204 assertions, continuous scene checks 63, protocol19 scene
checks 60, turret event checks 13, and replay format checks 2,888. Twenty virtual RTT/loss cases each
recover 607/607 events. Pure ledger, lifecycle, health/shot, protocol and allocation
checks also pass; their earlier build attribution remains explicit in the ledger.

The eight-client run has no simulation exceptions. Seven dropped steps and its
one stall occur at startup; the retained steady window (frames 301–1500) has no
step over 16.67 ms. Compared with the retained N2 single run, steady mean is
0.365 vs 0.314 ms; coarse p99 moves from [0.75,1.0) to [1.0,1.25) ms, while full-run
p99 is 1.99 vs 2.02 ms. These independent runs and coarse buckets do **not** prove
an exact matched p99 overhead bound or a causal performance improvement.
`step-comparison.json` retains the source aggregates and this limitation.

Focused Mac checks cover real weapon-switch bursts, charged missiles, scoped
Imperialist, dead/respawned shooters, old-life duplicate claims, enhanced Missile
children, unchanged present state, continuous target behavior, turret damage,
actual turret emission and duplicate suppression, bounded ledger outcomes,
reordered/duplicate carriers and historical recordings.
Windows was not required for these platform-independent network changes. No
Linux/Android tests, CI execution or long soak were requested or run.

Draft failures are retained: a malformed target fixture, historical checkpoint
fixtures with mislabeled widths, a test-only Hunter namespace compile error, and
an enhanced-child assertion made after catch-up could retire projectiles. The
last fixture now examines emission at ACK-now; separate recovered-shot checks
exercise historical timing. The original failed log did not establish which
catch-up collision retired a child. A final turret fixture initially used carrier
frame zero and expired its own authored event. Setting the frame to the current
network frame fixed the fixture. That first turret log was overwritten; the
ledger preserves this limitation and the agent-reported cause, not a fabricated
raw log. The final turret test commit `c311e17b` changes only tests; the engine
SHA-256 is identical to the campaign build.

This slice does not claim N4 anti-exploit admission, N5 verdict reliability,
N6 continuous-target redesign, rendered visual parity or a full netcode sign-off.
