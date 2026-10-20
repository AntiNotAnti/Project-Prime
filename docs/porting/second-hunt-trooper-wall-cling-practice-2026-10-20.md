# Explicit offline Trooper wall-cling practice

The existing local wall-cling mechanics were unreachable: no launch owner
called their one-shot rule binder. Offline setup now offers an explicit
MECHANICS choice, TROOPER WALL-CLING. Selecting it captures Weavel, Battle and
zero bots in the existing immutable offline draft/LaunchPlan. It defaults to
Native on each new draft and never derives gameplay from a skin, imported
file, character catalog or source-preview state. Native Hunter values stay
unchanged. The presentation is explicitly native Weavel model/weapons, with
no drone or blaster claim.

The shared OfflineLaunch preview validates before preference writes. Existing
offline preflight also refuses unknown variants, other launch kinds, online or
replay owners, non-Weavel/Battle, bots, Balanced/Enhanced/LowTier/M7 and non-Native
movement. The native page projects current-rule refusal reasons before its
map job; launch revalidates again. After actual local-player creation and before
room initialization, MatchStart binds that exact current player once. A failed
bind disposes the candidate Scene and reports the refusal. Ordinary plans
retain Native behavior. The configured local Android launcher admits the same
explicit control through its existing local launch service.

The binder preserves foreground Scene/registry/slot/lifetime/thread and
preinitialization guards, rejects conflicting rules/recorders and never binds
network, replicas, sideScenes or replay labs. Retirement cannot revive an old
capability after facade restoration. A one-way Scene latch excludes recording
and killcams even after the ability retires, because replay setup/state schemas
do not yet carry this mechanics identity. Practice is a playtest, excluding the
ordinary automatic offline rematch. It cannot enter the server career path.

Existing native movement and collision hooks own the mechanics: jump or at
least2 units of fall, inward speed>.02, actual near-vertical face contact and
three height probes;150-frame hold, at most3 clings between standing contacts,
20-frame release wait, .3 outward wall jump and14-frame into-wall steering
suppression. Native jump pads, locks, freeze, forms, death, bots, spectators,
inactive slots and retired owners release/reset the hold. No second movement
controller or external collision representation is added.

## Qualification

Exploratory first-core tests pass276 source-free actual owner/collision checks,
92 offline plan/draft/intent/replay checks and1,403 actual initialized AMHE1
Proving Ground simulation checks. The main608-tick keyboard schedule produces
two clings, one wall kick, one exact150-step timeout and zero hold drift; Native
Weavel replays the same schedule with exact trajectory parity before the first
cling transition and never clings. The separate no-jump fall arm uses an
explicitly synthetic native Spawn placement at an observed real wall+2.5Y;
both rules fall2.0856323 units in22 native simulation steps, and only Trooper
clings. This is CPU simulation acceptance, without drawing or imported-model
playability. These exploratory results precede final UI/latch changes and must
not be represented as final frozen-source qualification.

Final frozen source, platform/UI results and retained failure evidence will be
recorded here. Drone hover/burst/laser, Pirate Blaster/affinity, source Trooper
actor/gun materials and animations, online authority, replay setup/state and
physical Android/touch/gameplay qualification remain engineering/acceptance
work. This feature is labelled wall-cling practice, not a completed Trooper.
