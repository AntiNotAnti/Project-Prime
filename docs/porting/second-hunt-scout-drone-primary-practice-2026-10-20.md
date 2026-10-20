# Offline Scout Drone primary practice

This increment extends the explicit offline mechanics choice from PR903 with a
separate Scout Drone practice variant. It uses the current native Weavel player,
form, half-health body, collision, input and projectile owners. Default Native
and the wall-cling-only rule retain their previous meanings. Imported appearance
files never select a gameplay rule. The native Hunter enum, protocol76, replay
schemas and native RmlUi ABI1 are unchanged.

The three-step MECHANICS choice captures Native, Trooper Wall-Cling, then Trooper
Scout Drone. Either private choice captures Weavel/Battle/zero bots. Both require
native movement without conflicting modifiers, refuse network/replay/replica
owners and live recording, bind the exact player before initialization and latch
the existing unsupported-replay boundary. Neither enters automatic offline
rematch or server career admission. Experimental Android local-launch witnesses
must carry the exact known choice; the default Android launcher remains Avalonia.

The drone variant includes wall-cling and replaces the mobile half's native
lunge with the primary burst. Morph still uses native split/merge health and
collision ownership. The planted native turret body is vulnerable through the
existing damage owner but does not auto-fire. It retains native turret geometry
and sphere collision; donor standing-biped presentation, cylinder and headshots
are not implemented here. Psycho Bit supplies the mobile native fallback model,
using the existing captured presentation pose. This is not an imported Trooper
actor or source-animation completion.

Hover uses the native alt sphere and downward room probe: hold Jump to rise,
release to sink, cap at the native pickup-height span, and fall back toward the
floor when a ledge removes it. The native pad launch/lock wins until descent;
its apex transition applies biped falling gravity and a one-time horizontal
speed adjustment. Native ceiling/world collision still owns resolution. Rise
velocity also bounds the completed half-tick displacement so deceleration
cannot carry the sphere above the measured floor cap.

Primary timing is32 subsequent unfold steps, up to150 fire steps,24 fold steps
and120 cooldown steps at the existing60Hz simulation cadence. Release cannot
abort unfolding but cannot begin firing after release. Nineteen twin volleys at
fire frames3,11,...147 produce38 bolts and94 nominal unmodified direct damage.
Each muzzle has its own offset and room clamp; native Power Beam effects and
stats are the explicit fallback. A private40-shot pool reserves both free slots
before emission; a full or one-free pool cannot evict a live shot or emit a
partial pair. Damage and powerup modifiers use native projectile spawning and
impact, without fake accepted-fire, custom-weapon or turret events.

Burst processing follows the current native aim and accepted form decisions.
A same-step merge cannot fire an additional scheduled volley. Merge and freeze
cancel new emission and retain already-fired native flights; active merge starts
the full120-step wait. Death/respawn, teleport and owner/Scene retirement retire
private flights through native Destroy plus exact Scene unregistration. Teleport
cancels the burst without clearing its cooldown. A retired private body cannot
silently become an autonomous native turret. The local killed-by banner can
identify pool-owned Sentry Drone bolts; native Power Beam counters/kill-feed
classification remain unchanged. HUD shows ready/unfolding/firing/folding/wait,
the existing aim reticle and the native Jump/Alt Attack controls. Laser and
Pirate Blaster are explicitly unavailable.

Adaptation reference: SecondHunt41452f2d053fcfa679e33c00a73d120a53f59e61,
PlayerScoutDrone.cs (MIT). No donor global host flags, bot automation, network
packets, replay state or new simulation controller are imported.

## Qualification

Acceptance is in progress. Proposed owned-AMHE1 fixtures exercise actual
initialized Scene/player/native input and literal timing/health/pool oracles.
Synthetic placement, direct-hook pool stress and freeze setup are separately
labelled. Source-free plan, Android handoff, live-menu and actual native GL menu
fixtures preserve wall-cling assertions while adding the separate drone choice.
No playable-character, online/replay, GPU/model or physical-device qualification
is claimed from a compile or from these proposed checks.

Rollback: leave MECHANICS at Native (the default), or revert this increment.
Remaining work includes laser, blaster/affinity, standing-body cylinder/headshots,
source Trooper/drone/cannon materials and animation, authenticated multiplayer
identity/authority and deterministic replay setup/state.
