# A3 timed grant reconciliation — work in progress

**Status: NOT ACCEPTED — reconciliation acceptance is in progress.** Main `16b1d89b` and the reconciled A1/A2 prerequisites are integrated. The immutable candidate is source `edb97ce1ce94bf74fa3a9f47a95b5f0825b2a08d`, client SHA256 `6999d6f91a9d45026a576f414da4132601ad37e0fb6e51ca5569e7fb058856c2`, MVID `1a6ca1a3-f5d0-4891-8db9-26bc7583e3c7`. Local, hosted CI and native replay gates must finish before merge. Historical development results below are not acceptance of this candidate.

Reconciliation preserves main's NativeTouch input and retired hunter compatibility. It also fixes a discovery room fence, transactional consumption capacity, first-pickup HUD flash, and a healthy grant stream incorrectly retaining its delivery deadline after a complete drain. The A2 terminal retry reservation and corrected stress fixture are inherited. Each repair retains its negative control.

The source58 prototype passed real UDP bootstrap/recovery, native three-effect projection, and genuine Double Damage/Cloak recording and clip checks. **Death Alt native seek color/HUD/depth parity FAILS at its first positive frame**; raw captures are preserved under `work/net21-reconcile/a3-native-authored-DeathAlt-prototype`. This remains a merge blocker. Its gameplay hash matches; that does not override the rendering failure. Current enabled-workload scheduling drops also require a matched main comparison. Neither gate is waived by the passing packet/claim audits.

## Scope and trust boundary

A1/A2 authority grant ownership remains the source of durable pickup/grant outcomes. A3 adds bounded client presentation, retained grant transitions and replay projection for Double Damage, Cloak and Death Alt. It does not add another combat payment or pickup authority.

`TimedGrantPresentation` explicitly separates `Remaining` from `ConfirmedRemaining`. Pending owner contact can produce immediate native visual timers and Death Alt form prediction. `PlayerEntity.DoubleDamage` and Death Alt collision authorization now consult `ModConfirmedTimedEffect`, rather than treating the provisional visual timer as permission for normal native client damage. Confirmation requires matching authority life/scope and source contact preceding the observed authority clock. Visual prediction is not authority evidence.

**A4 remains open:** this separation does not independently authorize a malicious owner's reported damage or Double Damage flag at shot source time. Source-specific damage caps and authority grant-at-fire history must still fence delayed claims. Existing exact ShotKey/component payment, CombatAck and accepted-shot rules must remain intact. Do not describe A3 as complete damage anti-cheat.

## Live facts, budgets and bootstrap

Current `NetProtocol.cs` declares protocol **56**, `TimedGrant=77`, `PickupPrediction=78`, and `TimedGrantBootstrap=79`. IDs 78/79 are local replay presentation facts, not live client authority messages. Protocol56 combines main's NativeTouch contract, A2 pickup claims and A3 grant state. The earlier staged protocol54 used a different intent layout and is retained only as a historical reader capability. Current PPRC12 carries explicit intent/pickup/grant features; PPWC10 carries explicit touch/pickup features. Valid legacy PPRC10/11 and PPWC9 layouts remain supported, with ambiguity or contradiction rejected before publication. Deploy matched client/server protocol56; the source constants do not establish acceptance.

`NetTimedGrantFact.cs` encodes a fixed **53-byte** grant fact: complete authority life identity, room, revision, effect type, grant sequence, spawn identity, authority contact/end frames, end reason and authority observation clock. Life identity uses ShotKey with ShotId zero. Explicit absence is a complete authority-issued retired fact, never an inference from a missing packet. Resume occupies bit 7 of the reason byte.

`NetGrantNetwork` retains transitions separately from the fast player lane. `NetFactBatch.MaximumPayload` is `1200 - NetHeader.Size - 4`; **22 facts** fit, producing **1194 bytes** including the 24-byte header and four-byte reliable envelope. An eight-slot/three-type **24-state** bootstrap therefore needs **two packets**, not a widened snapshot or MTU. Join republishes existing facts with a fresh revision/authority clock and Resume marker without rewriting contact/end lifetime. A new peer receives its bounded latest-state baseline and retained cursor.

Retention uses a 2048-entry delivery store. Pump refuses stale-store peers or delivery stalled beyond the shared **15-second** deadline; it does not silently discard an outstanding terminal obligation. These are implementation bounds awaiting loss, congestion, reconnect and recipient tests.

## Prediction, clocks and projection

`TimedGrantPresentation.cs` holds at most **16 pending predictions** and a consumed-spawn history bounded by the existing **512-entry** grant capacity. Predictions expire after **900 local simulation frames**, or when their life is no longer alive. Normal grant duration remains 1800 simulation frames. The network terminal deadline and local presentation deadline are separate clocks even when both correspond to 15 seconds at 60 Hz.

Authority remaining time uses an authority/local anchor and modular signed frame differences. Observing an older authority clock cannot move the projection backward. Live projection also observes the latest snapshot plus snapshot age. Revision/life checks reject stale facts; conflicting same revisions and backward grant sequences fail explicitly. Matching grant deltas can retire presentation prediction before a verdict arrives, while transport retains its independent verdict delivery obligation.

`PlayerEntityTimedGrants.cs` projects native timers, cloak alpha, sounds and effects. Expiry/refusal removes the matching provisional overlay without subtracting another confirmed grant. Resume/quiet projection avoids fresh pickup effects and forced form changes. Native timer countdown is suppressed where this projection owns time. Historical replicas retain their native timers until explicit new grant coverage exists; current network clients suppress unknown native timed effects.

`TimedPickupProjection.cs` projects authored spawners and dynamic drops. Dynamic identity is the full PowerUpSpawnKey (epoch/match/room/spawn/generation); dynamic drops use SpawnId -1 and their distinct generation. Unidentified/duplicate local dynamic timed drops are retired. Available authority drops are materialized once, unavailable facts retire them, and pending/consumed identity hides the appropriate pickup. This is presentation of authority facts, not another grant function.

## Replay and privacy

`ReplayGrantFacts.cs` records authority/received grant facts and local owner prediction facts. Each private replay owns its `TimedGrantPresentation`; it does not borrow the live static presentation. Checkpoint serialization includes the bounded value state, clocks, predictions and consumption history. Clip rebasing shifts local anchors/prediction arrival frames rather than treating original producer frames as the new playback clock.

The local `TimedGrantBootstrap` fact seeds presentation state at recording/clip boundaries. Its state payload is bounded to **32768 bytes** and validated for exact consumption and matching stream. It is not a live UDP bootstrap packet. Old replay capabilities must not fabricate contact, grants or hidden pickups from absent fields.


## Pending verification and acceptance

Development iteration dev6 reports a build with **34 warnings and zero errors**; a new fixture nullable warning is being fixed. This is development verification, not an immutable acceptance build or full CI result.

Observed dev6 logs under `artifacts/net21-a3/` report:

| Check | Result | Log |
| --- | --- | --- |
| Timed grant codec | PASS, 498 assertions | `dev6-timed-grant-codec.log` |
| Timed grant presentation | PASS, 75 assertions | `dev6-timed-grant-presentation.log` |
| Grant network | PASS, 229 assertions | `dev6-grant-network.log` |
| Grant replay | PASS, 33 assertions | `dev6-grant-replay.log` |
| Pickup protocol | PASS, 352 assertions | `dev6-pickup-protocol.log` |
| Pickup network | PASS, 6219 assertions | `dev6-pickup-network.log` |
| Pickup readiness | PASS, 19 checks | `dev6-pickup-readiness.log` |
| Pickup replay | PASS, 31 checks | `dev6-pickup-replay.log` |
| Native timed presentation | PASS, 47 assertions | `dev6-native-presentation.log` |
| Python suite | OK, 484 tests run, one skipped | `python-dev6.log` |

Required acceptance evidence still includes codec/direction/handshake fixtures; worst-case datagram bounds and 24-state bootstrap; 16-prediction/512-consumption overflow; authority clock/frame wrap; simultaneous prediction and independent grants; verdict-before-delta/delta-before-verdict; expiry, death, slot reuse, room changes and reconnect; dynamic-drop identity/availability; single native visual/audio transitions; provisional-versus-confirmed damage; real UDP loss/reorder and stalled delivery; private replay/checkpoint/seek/clip bootstrap and historical adapters. Player-facing HUD countdown and sound stages need integrated asset-backed checks.

Rollback must remove A3 consumers, replay facts and corresponding protocol deployment together. Preserve A1/A2 authority ledgers and their accepted outcomes; do not downgrade the wire silently or convert absent A3 coverage into a granted effect.

Source anchors: `NetGrantNetwork.cs`, `TimedGrantPresentation.cs`, `PlayerEntityTimedGrants.cs`, `TimedPickupProjection.cs`, `ReplayGrantFacts.cs`, `NetTimedGrantFact.cs`, `NetFactBatch.cs`, `NetPickupNetwork.cs`, `NetProtocol.cs`, and the timed pickup/collision branches in `PlayerProcess.cs`/`PlayerCollision.cs`. Paths are under `src/MphRead/Mods/Network/` unless stated otherwise.
