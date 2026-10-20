# CW-08 — accepted lethal head collapse

The accepted damage path remains the only cause of death. This slice reads its
immutable lethal fact and the already-dead actor to present a short articulated
helmet/torso/knee collapse. It never writes HP, velocity, collision, inventory,
score, respawn timers, native shot identity, audio or replay checkpoints.

## Cause and lifetime

Native/custom/local causes retain distinct tagged keys and the full victim
scene, actor reference, slot, generation, life, match, authority epoch and
presentation timeline. A valid ordinary body death consumes the same one-cause
life latch, preventing later retransmissions from changing its cause. The native
recorded source key is retained even if the shooter has since respawned. Custom
direction joins only the exact accepted launch/action and original bootstrap;
missing/native immutable direction uses a neutral facing fallback.

Reliable facts can precede the health snapshot. One bounded pending cause per
victim keeps its original resolve clock and eligibility, waits at most18
presentation steps and then suppresses the cause for that life. An actually
dead actor is required before any corpse pose is exposed. Arrival age, stale
client snapshot, old life, actor replacement, teleport, frozen/morph state,
quality, reduced motion, spectator or same-life revival cancel the pose. Late
join does not create a new corpse without an observed alive actor. Recorded
native replay reconstructs the last144 recording frames under its own seek
identity; no new archive or checkpoint fields are written. Archives without an
accepted lethal head fact retain their existing death fallback. Custom replay
remains a CW-12 dependency.

Classic lasts42ticks and fades during its final12ticks. Helmet recoil leads a
12tick hip/knee collapse. Nonzero effects hold the settled pose while their
existing body remains visible, bounded to144ticks (current effects end by132). Direction chooses a stable
small lateral variant using fixed integer hashing, independent of gameplay RNG
and scene/seek identity. Draw only samples a bounded fractional pose.

## Skeleton borrowing and catalog policy

The scope prewarms both native LODs for each Hunter. It borrows every composed
node and regenerates the palette only during body submission, then restores all
nodes and the complete palette in finally. Trace/Noxus do not require a Pelvis
node: actual left/right hip joints define the pivot. Analytic two-bone legs
retain the already-composed ankle targets and foot orientation. Spire has an
unweighted native head; its weighted torso is the deliberate visible fallback.
No independently simulated bodies or gameplay collision objects are added.

Classic receives a short anchored body while its original native death particle
call and clock continue. The seven nonzero effects keep their selected root
pose, surface, particles, HideBodyAt, disappearance and duration. Settled knees
stay collapsed until that body disappears. Cryo gets zero added motion; Float and Kneel keep reduced
collapse. No pose extends a hidden/dissolved body. All visible nonzero biped
corpses now use one native/modern replacement selection route for their entire
lifetime, including ordinary body deaths, avoiding an HD-to-native swap when
the short head track ends. Alternate-form cosmetic bodies retain their original
native route. Actual HD/weighted art qualification is a separate release gate.

## Validation and rollback

See [evidence](evidence/CW-08.md) for measured checks and explicit platform gaps.
No protocol, native table, save or checkpoint contract changes are required.
Existing cosmetics/quality/reduced-motion gates suppress the additional pose;
rollback removes this cosmetic adapter and body scope without combat migration.
