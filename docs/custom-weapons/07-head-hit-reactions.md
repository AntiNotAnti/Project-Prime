# CW-07 — accepted nonlethal head reactions

The surviving Hunter receives a short cosmetic head/upper-torso recoil after
accepted body damage classified as a headshot. Gameplay damage, animation
selection, hit volumes, actor motion, aim, gun/muzzle anchors, simulation RNG,
packet layouts and native audio remain owned by their existing systems.

## Fact sources and identity

Offline TakeDamage emits a scene-local event only after actual positive body HP
removal with positive HP remaining. Its unique event serial belongs to the
scene, exact victim reference and spawn ordinal; the victim needs no custom
inventory. The callback never performs damage itself.

Live native clients use the existing accepted ReplayShotFact batch after full
batch validation and exact current match/epoch checks. Custom clients consume
the existing canonical Contact outcome once, before the bounded presentation
ring overwrites it. Trade denial, protected zero damage, body hits, turret-only
hits and lethal contacts cannot start the nonlethal reaction. Accepted death
terminates the victim track even if the next HP snapshot is still pending.

Keys remain tagged: native full ShotKey plus DamageEventId, custom full payment
key plus original source bootstrap and DamageEventId, or a scene-local event.
The custom key never becomes a native ShotKey. Victim context includes scene,
actor reference, slot, occupant generation, life and presentation/seek generation.
An accepted flight from an older shooter life remains valid for the current
victim; the adapter never requires the shooter's current life.

The local direction is the accepted damage argument. Custom direction joins
only an exact full Action plus original-bootstrap launch in the recent bounded
ring. Missing/reordered launch evidence produces a neutral recoil. Native facts
do not carry a proven incoming vector, so they use the same neutral fallback;
current shooter position is never substituted for accepted direction.

## Time and cancellation

The track peaks at five fixed ticks and settles at eighteen, with a smooth
bounded curve and a maximum head angle of12 degrees. Spine and shoulder weights
are smaller. Newer fresh hits coalesce; duplicates and reordered older hits
cannot refresh an active reaction. Thirty-two retained identities per victim
are bounded and refuse new events if full. Sampling is read-only and finite.

Age uses the canonical resolve world, with an eighteen-tick admission bound.
A reliable fact arriving before its world snapshot enters a scene-owned queue
of at most64 normalized values for at most18 presentation ticks. No HP or world
clock is advanced to accommodate it. A live client also requires a snapshot
received within18 local network ticks, so a stalled authority clock cannot make
a delayed old fact appear fresh. The queued eligibility value can only be
suppressed by later cancellation. Teleport, form/frozen/spectator/death changes,
quality/reduced-motion suppression and full victim-context changes prevent an
old pending reaction from reappearing after recovery.

Replica scenes use their recorded clock and seek generation. At each completed
replica step, only the existing last18 recording frames of accepted native shot
facts are considered. This reconstructs a short overlay after a seek without
adding a checkpoint field or changing an archive fingerprint. Repeated pictures
cannot consume facts or advance the track. Missing historical facts use ordinary
native presentation. Custom recording remains blocked until CW-12.

## Scoped skeleton ownership

Per-model profiles use native Head_1, Spine_1/2 and shoulder names for all seven
Hunters and both LODs. Spire's native Head_1 has no palette weight; its bounded
Spine_2 response is an explicit visible fallback. Missing/unsupported mappings
keep the existing native reaction. Frozen/morph states suppress the overlay.

A weak model cache holds bounded prewarmed scratch. During body submission only,
the scope saves every already-composed node Animation matrix, applies additive
rotations, updates the palette and restores all nodes/palette in finally. Nested
use of the same shared model fails closed. The R_elbow shooting leaf and all
lower-body nodes remain exact, including while the scope is active. Restoration
finishes before custom rifle, muzzle, effects and another actor use the model.
Native, rigid and weighted4 paths read these same scoped node matrices; actual
HD/weighted4 art qualification is tracked separately from native-rig evidence.

## Release boundary

Reduced motion, hidden/off cosmetics, headless and thumbnail paths keep their
existing presentation. No new audio is emitted. Visual teardown clears tracks
and pending references; profiles retain no GL handles. Rollback removes the
fact adapters and body scope without any wire/save/replay migration. See the
CW-07 evidence receipt for actual checks and unqualified release matrix rows.
