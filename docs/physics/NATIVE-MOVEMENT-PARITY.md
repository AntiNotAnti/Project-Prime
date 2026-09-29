# Native movement parity — kernel experiment

The offline `-fpsnativekernel` experiment now matches the original ROM's position, velocity, standing/grounded/collision state, ground timer, observed movement stages, and ordered contact results across all 17 fixtures. The normal 60 Hz gameplay path is unchanged. This is **not** acceptance of a live 60 Hz conversion.

The strict complete comparison passes 15 fixtures. `biped-idle` and `knockback` fail heading only, beginning at boundary 68: the ROM enters idle sway while Prime retains its deliberately delayed sway policy. Their movement states and measured stages/contacts still agree. No tolerance was widened: position/velocity/stage tolerance remains 0.0001, less than one native fixed-point unit (1/4096), and heading tolerance remains 0.001 degrees.

## Reconstructed operation

The experiment runs one complete native movement operation per sample. It invokes the scene once per operation; unrelated scene timers retain their 60 Hz definitions. Manifests distinguish `movementOperationHz: 30` from `sceneClockHz: 60`. This is an isolated movement experiment, not a complete 30 Hz world simulator. It uses the existing movement/collision control flow with diagnostic arithmetic selected only by the offline scenario runner:

1. Apply timed acceleration, decrement its remaining duration in the existing 60 Hz counter units.
2. Select the horizontal cap and movement basis; exclude jump-pad velocity before damping.
3. Apply horizontal damping with signed fixed-point multiplication truncated toward zero; restore jump-pad velocity.
4. Select gravity from the previous standing state, then integrate the entire native velocity.
5. Resolve map contacts, velocity rejection, slope slowdown, standing/grounded transitions, and the ground timer.
6. Apply input traction after collision and limit horizontal speed using native rounded products, square root, and division.

The native arithmetic is not one universal rounding operation. ARM9 damping at `0202132C` truncates signed products. Cap calculations at `02026008` round squared components individually before square root, round the quotient, and round the scaled velocity. Contact pushout and velocity rejection truncate products; slope slowdown rounds its horizontal products and final scaling. Biped edge contacts use rounded fixed-point projection, distance, and normalization. These distinctions account for the previously observed one-unit errors and cap oscillation.

Native static-plane distance is projected through the stored normal, including fixed-point loss when the normal is not exactly unit length. For example, the ceiling's file distance -3.601806640625 becomes -3.599853515625 in the observed contact. Preserving the file distance directly produced a different collision even with the correct integration cadence.

The experiment removes the half-step ceiling/slope response multipliers and displacement clamp. Native pushout uses the computed plane-normal component times penetration. This was verified on the ceiling, both slopes, angled wall, and edge-contact fixtures. Those changes remain **diagnostic only**; removing them from the live half-step pipeline without fixing the coupled update would not establish parity.

## Native operation inside the 60 Hz scene

`-fpsnativecadence60` now invokes the scene twice per native operation. Movement, collision, the ground/acceleration timers and post-collision traction run together on even scene boundaries. Other scene processing continues on every 60 Hz frame. This closes the earlier experiment's gap between the declared scene clock and the number of actual scene updates.

A first-half jump edge is buffered per player and consumed exactly once on the second half. An edge arriving on the second half is consumed immediately at that boundary. Held directional input is sampled at the operation boundary. `-fpsinputphase 1` moves the same scenario commands to the second half; the default phase supplies them on the first. All 17 fixtures produce identical boundary traces for both phases, including jumps and direction reversals. All 17 match native movement/stages/contacts; 15 pass the complete comparison and the same two idle-sway heading differences remain.

This candidate deliberately holds authoritative position between native operations. The runner writes a `.cadence.jsonl` companion containing every actual scene frame, requested jump edge, position before/after and velocity. It rejects unexpected position changes on held frames. The separate draw pose now presents the midpoint on a native boundary and the completed endpoint on the following held frame. This adds one 60 Hz frame (about 16.7 ms) of translation delay. Models and first-person camera/viewmodel use the existing render histories; simulation positions, collision volumes and input ownership remain authoritative. Large discontinuities snap; native-mode teleports clear the pending edge and rebase the previous position/draw history. This is a tested presentation candidate, **not live network or visual-playtest acceptance**. The input latch now belongs to each player and is saved in a versioned world-checkpoint appendix. Live network correction is still unverified. The mode cannot be selected through gameplay settings.

Final regression evidence is in `artifacts/fps-audit/followup/cadence60-verified/`: both input phases, all eight coverage checks for each phase, and complete trace equality against the prior default-60 and native-30 captures for all 17 fixtures. No native references were regenerated or adjusted for this test; it uses the previously verified original-ROM captures.

### Restore and replication constraints

Cadence policy now belongs to each scene, and the pending jump belongs to each player. There are no process-wide cadence switches or weak-table input latches. Respawn, teleport, entering the alt-form path and returning to a non-cadence policy clear pending edges.

World capsules use a required version-4 appendix for native modes. It stores an explicit appendix version, mode, player count and canonical pending-edge bytes; the existing scene graph stores frame parity, previous position and model/camera render histories. Unknown modes/versions, incorrect counts, noncanonical booleans, truncated/trailing data, and edges attached to a non-cadence policy are rejected. This is the **world capsule** version, separate from the replay file format. Ordinary worlds still write version 3 with the existing graph contract and type IDs. Version 1–3 restoration explicitly selects the legacy policy and clears pending edges, including authentic v0.1.34 capsules.

`--fps-cadence-replay` verifies full world serialization/restoration from both frame halves, a pending first-half edge consumed after resuming, 20 continuation frames against the uninterrupted world, bound-versus-reflection capture equality, independent scene policies, malformed-policy rejection, current-v3 restore, historical-policy reset, and teleport invalidation. Continuation checks the existing gameplay and presentation hashes **and** the explicit cadence state; the legacy gameplay hash alone does not cover this new state. This is detached replay continuation evidence, not local-owner network rollback certification.

Latest evidence: `artifacts/fps-audit/followup/cadence-presentation-restore-final.log` and `cadence-presentation-corpus/`. All 17 fixtures preserve complete movement traces in all four modes. Both input phases validate every body/camera endpoint plus first-person pose preparation at five render fractions per scene frame: 17,800 draw samples total, with position, velocity, facing and buffered input checked for draw-induced mutation. Frame-timing regressions pass. The fixture now explicitly ends its otherwise-retained intro camera before capture; movement traces remain exactly equal to the previous captures.

Existing remote movement is replaced after local movement in `NetHooks.AfterRemoteMovement`. `PlayerReplicationBridge.Move` also resets `PrevPosition`, translates collision attachments, refreshes the node and rebuilds the collision volume. A future intermediate-position cache must participate in that correction; restoring only `Position` would leave its anchor stale. Current owner-authored movement and passing transport tests do not establish rollback/resimulation parity for a new cadence policy. The diagnostic runner refuses active network sessions.

## Matching initial conditions

The native save originally retained 55 idle ticks before teleporting to the scenario start. Prime's Spawn resets its input timer. The recorder now resets native `CPlayer+408` at setup, confirmed by the native increment/reset instructions at `02011090–020110A4`. It also clears initial Grounded/GroundedPrevious bits to match the runner's spawned state, which matters for zero-settle falls. These are explicit initial-condition writes; no timer or physics state is overwritten during the recorded run.

The old references in `paired-reproduced` included pre-existing idle sway in the movement basis. They remain historical evidence but are superseded by captures using matched setup. The original ROM still performs every movement update. Its remaining idle-sway behavior is retained and reported as a heading mismatch.

The knockback fixture prescribes initial velocity plus six native ticks of acceleration after settling. It verifies the response and timer, including termination of acceleration; it does not certify any weapon's damage/impulse generation. Jump-pad activation uses the real Combat Hall trigger and original launch impulse. The capture ends after 40 native ticks, before unrelated bot damage observed in a longer trial.

## Stage comparison and evidence

Prime diagnostic stages explicitly declare `sampleHz: 30`. The comparator can compare them with original-ROM `nativeStages` at 30 Hz, including contact order, plane, penetration and pushout. Ordinary Prime stages declare 60 Hz and remain UNAVAILABLE for direct native-stage comparison; one half-step is not a whole native operation.

Reproducible corpus commands are in [the scenario README](../../tools/fps-scenarios/README.md). Initial full comparisons are in ignored `artifacts/fps-audit/followup/paired-kernel8/`; the repeatable corpus runner writes fresh provenance, capture logs, coverage checks, and comparisons per fixture. The ordinary v3 world layout, protocol 33, and default 60 Hz simulation/network cadence are unchanged; native modes use the explicit v4 cadence appendix.

## Remaining integration

Architecture B in the original plan permits a native-cadence authoritative kernel with deterministic 60 Hz intermediate states. The experiments establish the static biped operation and its boundary schedule inside the 60 Hz scene; checkpoint restoration now preserves that schedule and input state. The draw-only intermediate presentation candidate is implemented and numerically checked; live network correction and visual playtesting remain unverified. Publishing it still requires acceptance of the added translation delay, live input/correction ownership, moving-platform/player interaction timing, dynamic teleport/impulse coverage and prediction restoration. Derived intermediate poses must not accidentally feed back into the authoritative native state.

After a live implementation passes the paired corpus, rerun real-client transport profiles and replay restoration, then platform runtime determinism. Combat, entities/projectiles, AI, camera policy and the remaining audit cohorts follow. Alt forms remain last.
