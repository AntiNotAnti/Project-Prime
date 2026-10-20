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

Frozen combined source `6d311aef552b22a37f6c6721812283cc9b993d6e`
includes main through `7762666cd` (scoreboard/lobby return, Aim Lab routing,
corpse-profile work and native-picker/test corrections).
Mac SDK10.0.401 desktop Release passes with one inherited CS0649 warning and
zero errors. The shipping desktop DLL SHA256 is
`428af3405eb8ef28bd21ab940ba8a2186ff08a6d9c896d7b18b4fc55d12a712b`;
nettest is `02f3e1fc0a9ff78492344a2a29f5f5ab3e9a71b852489e4bd11bfc5bf54d77b7`.
Both binaries and all6,337 tracked inputs remain unchanged throughout the
following runs. Logs, commands, inventories and exact numeric results remain
local under `artifacts/sh-integration/scout-drone-6d311aef5/`.

| Check | Result and scope |
| --- | --- |
| Isolated dedicated Release |Captured exit0; initial full compile one inherited CS0649 warning/zero errors, same-source incremental confirmation zero warnings/errors; retained DLL SHA256 `87cc04db766e048620edfa875097dd05b7fe58f6d049cb5ecfff041e0452f57d` |
| Source-free wall owner and offline plan |276 and166 assertions; explicit capture, refusal, lifetime/registry/rule fences and native defaults |
| Owned initialized Scene |4,958 assertions; real native keyboard/mouse input, split79=>40+39, body damage12=>6+6, merge67, room-floor hover cap1.5998536, exact32/150/24/120 timeline,38 bolts/94 nominal damage, release/current-aim/merge/freeze/death/retirement and native Weavel controls |
| Wall-cling initialized Scene regression |1,403 assertions,608 simulation steps, two clings, kick, timeout, zero held drift and native fall recovery |
| Owned actual OpenGL |773 assertions,632 simulation steps,17 full Scene pictures and14 HUD-only diagnostics; native fallback meshes/trails, actual reticle pixel witness, independent HUD states, extra draws cannot advance simulation; all40 pool members and380 Scene textures retired |
| Actual native menu OpenGL |265 assertions and27 translated intents at each960x600 and640x480; explicit drone/wall/native choices, stale preflight refusals and merged Aim Lab/Battle route boundaries; synthetic services, no match/media |
| Source-linked runtime/capabilities |Default checks and732 capability assertions; finite native action boundary, no new ABI or inferred rule |
| Core-bound Android handoff/live menu |66 handoff assertions and full live-menu defaults; four tool-only builds/runs each, zero warnings/errors; preserved exact core copies |

The owned tests use existing local AMHE1 data read-only. Each run preserves its before/after inventory of3,970 files and
845,958,857 bytes. Between the earliercf548 and final6d freezes, precisely three
816-byte generated `map.build.json` files changed (Prime Aim Lab, Test Arena
and Test Pads), with no added/removed files or size changes. The normalized
per-file comparison is retained; no cross-freeze byte-identity claim is made. The GPU run is Apple M4 Pro, OpenGL2.1 Metal91.7;
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
freeze repeats them. A later read-only audit exposed a third production
defect: the body could observe a lost rules owner inside its fire condition,
retire itself, then fall through into native turret emission. The new strict
fixture reproduces one ordinary shot after removal against thecf548 core.
Commit `6d311aef5` resolves the owner before the body lifetime guard; the same
body-first call now returns false with zero ordinary/private flights, both old
private bolts retired, cooldown600 unchanged and replay exclusion still latched.
Native turret positive controls and all prior drone assertions remain intact.
The explicit target/cooldown setup and direct body scheduling are synthetic,
not a separate simulation loop. Raw red and final green receipts are retained.

The earlier14c Android run was deliberately superseded
without an APK when overlapping main UI changes arrived, and is not a pass or
a compile/gameplay failure. The intermediatecf548 Android attempt was stopped by the768MiB disk reserve
during linking, before an APK; its source compile and raw wrapper/stop logs are
retained without a package pass. Final6d includes the later main changes above. Its first dedicated wrapper
omitted an import after starting the compiler; the compiler log reports success
but that wrapper did not capture its exit. The corrected same-source incremental
confirmation captures exit0; both logs and the wrapper error are retained.

The final6d parallel Android attempt also hit the disk reserve during AOT.
A same-cache retry signed an APK but the independent inventory found zero AOT
entries for both ABIs, so that APK was rejected and retained as failed evidence.
No optional profile was admitted from it. Subsequent clean-cache serial-AOT builds
pass both required Release profiles: default APK SHA256
`dd23c3995e59119b811857d518ed73fa1ab0afea385872522d34ed83ca876fcb`
(147,839,049 bytes), and Package+Experiment APK
`5c5c62b5b05294b8d71575302553d405277b95a29b2edc95e5b8e0fd35bc86e2`
(179,978,263 bytes). Both contain119 AOT libraries per ABI with correct ELF
machines and valid v2/v3 signatures using the local Android Debug certificate.
The default excludes the optional native bridge; the optional package matches
fingerprint `65eb5d70c119715a3c941aa68792512d53adf760bee7bc8980fe227fd8403ec9`.
The optional build completes in275.35 seconds with27 existing warnings and zero
errors. Retained Android core hashes are
`9e2f0716a05916546a7d97d4508c553f5a07e60a0dd5d226d0d5e41ce3b0d791`
(default) and `226f58ac9f7c16198c008fc58366d64699937193df4e4fcd1ff61aa51535f2c0`
(optional). All6,337 source inputs and the root desktop core remain unchanged.
Scheduling changes are recorded in the exact commands without relaxing any
AOT/signature/native gate. These are compile/package results, not Android
runtime, UI, production-signing or playability acceptance.
Standalone Debug is not part of this slice's final qualification.

Storage recovery touched only explicitly inventoried task-created caches and
source duplicates. Qualified binaries/packages, logs and hash/recreation receipts
remain. Removed original source archives matched both their historical hash and
a freshly streamed exact Git archive; source/native files remained intact. Three
older task-created tracked source snapshots were separately retired after exact
Git/blob verification, preserving all untracked outputs and complete recreation
metadata. Four additional Git-tracked source/server-source trees in two completed
PR856 snapshots were retired the same way (24,440 verified files); their commits
remain reachable from main and all untracked outputs are unchanged. Logical
cache sizes are not reported as physical disk recovery.

These checks qualify native-fallback private mechanics and scoped rendering.
Pad landings, ledge/ceiling acceptance, balance, imported model/animation fidelity,
physical Android controls/GPU, multiplayer and replay remain unqualified.

Rollback: leave MECHANICS at Native (the default), or revert this increment.
Remaining work includes laser, blaster/affinity, standing-body cylinder/headshots,
source Trooper/drone/cannon materials and animation, authenticated multiplayer
identity/authority and deterministic replay setup/state.

After the frozen qualification, commit `e6ebd69a4` changes only one test's
diagnostic description to match an overlapping main fix; no assertion predicate
or production code changes. The frozen core/tool/package identities above remain
the evidence identities. Later main changes outside the drone owners are not
claimed as covered by this frozen whole-binary proof.
