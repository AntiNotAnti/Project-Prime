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

Frozen combined source `cf54892cbea3568041f23e9b058d68ea8c57e8e9`
includes main through `7513ece03` (scoreboard/lobby return and Aim Lab routing).
Mac SDK10.0.401 desktop Release passes with one inherited CS0649 warning and
zero errors. The shipping desktop DLL SHA256 is
`799f8dd3c2a747bd7f45ca7163db22faf8aba50966f2f6d1823a32e31e552bf0`;
nettest is `782cf5f8c653ff94ca2ae68256dba9dbed2e78637f43743a3f26550855c7253c`.
Both binaries and all6,332 tracked inputs remain unchanged throughout the
following runs. Logs, commands, inventories and exact numeric results remain
local under `artifacts/sh-integration/scout-drone-cf54892cb/`.

| Check | Result and scope |
| --- | --- |
| Isolated dedicated Release |54.34s, one inherited CS0649 warning/zero errors; retained DLL SHA256 `33630a8ac7f6aff42b011569dbaf9cb257c972f2258c1203c59266748c6d94f7` |
| Source-free wall owner and offline plan |276 and166 assertions; explicit capture, refusal, lifetime/registry/rule fences and native defaults |
| Owned initialized Scene |4,936 assertions; real native keyboard/mouse input, split79=>40+39, body damage12=>6+6, merge67, room-floor hover cap1.5998536, exact32/150/24/120 timeline,38 bolts/94 nominal damage, release/current-aim/merge/freeze/death/retirement and native Weavel controls |
| Wall-cling initialized Scene regression |1,403 assertions,608 simulation steps, two clings, kick, timeout, zero held drift and native fall recovery |
| Owned actual OpenGL |773 assertions,632 simulation steps,17 full Scene pictures and14 HUD-only diagnostics; native fallback meshes/trails, actual reticle pixel witness, independent HUD states, extra draws cannot advance simulation; all40 pool members and380 Scene textures retired |
| Actual native menu OpenGL |265 assertions and27 translated intents at each960x600 and640x480; explicit drone/wall/native choices, stale preflight refusals and merged Aim Lab/Battle route boundaries; synthetic services, no match/media |
| Source-linked runtime/capabilities |Default checks and732 capability assertions; finite native action boundary, no new ABI or inferred rule |
| Core-bound Android handoff/live menu |66 handoff assertions and full live-menu defaults; four tool-only builds/runs each, zero warnings/errors; preserved exact core copies |

The owned tests use existing local AMHE1 data read-only. All3,970 files and
845,958,857 bytes remain unchanged. The GPU run is Apple M4 Pro, OpenGL2.1 Metal91.7;
only hashes/counts/numeric receipts are retained, with no screenshots, media or
saves. Fresh temporary contexts are deleted. Windows SSH recovery reached the
host but authentication was refused before any command ran, so this increment
uses the user's authorized Mac fallback and makes no Windows drone claim.

The capacity test explicitly invokes the production volley hook without ticking:
it proves real native40-flight allocation, whole-pair reservation at0/1/2 free,
survivor and ordinary-pool snapshot preservation and exact Scene removal. It is
not cadence evidence. The real-input timeline independently proves cadence.
A normal control projectile expires on room collision before the first drone
volley, so natural overlap of live ordinary and drone flights remains unqualified.
Target placement/remaining-health cap and injected native freeze are labelled
synthetic setup. The second inert target is added after zero-bot admission;
it does not establish a product route to native bot combat.

Earlier exploratory failures are retained in `scout-drone-94981ac8c/`: the stale
selection-capture expectation, old PR901 body recipe/component/menu labels,
own-body support mistaken for room-floor support, an unsupported local keyboard
aim producer, and ID-map membership mistaken for anonymous Scene membership
were fixture defects, corrected without weakening the relevant oracle. Actual
production defects were also exposed: native HUD initialization disabled the
reticle, and anonymous pool entities survived map-guarded retirement. Commit
`14c334cfb` fixes both and passes the strict actual GL/Scene tests; the combined
freeze repeats them. The earlier14c Android run was deliberately superseded
without an APK when overlapping main UI changes arrived, and is not a pass or
a compile/gameplay failure. Later unrelated main corpse-profile work is outside
this frozen whole-binary qualification.

Android packaging qualification is still running; this paragraph is replaced
with its exact results before this increment is merged.

These checks qualify native-fallback private mechanics and scoped rendering.
Pad landings, ledge/ceiling acceptance, balance, imported model/animation fidelity,
physical Android controls/GPU, multiplayer and replay remain unqualified.

Rollback: leave MECHANICS at Native (the default), or revert this increment.
Remaining work includes laser, blaster/affinity, standing-body cylinder/headshots,
source Trooper/drone/cannon materials and animation, authenticated multiplayer
identity/authority and deterministic replay setup/state.
