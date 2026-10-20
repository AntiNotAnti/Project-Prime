# Offline Scout Drone laser and native bot combat

This increment extends [primary practice](second-hunt-scout-drone-primary-practice-2026-10-20.md)
with the SecondHunt cutting-laser timing and a separate explicit offline combat
practice choice. Native remains the default. Imported assets do not enable these
mechanics. The original seven Hunter identities, protocol, replay formats, native
UI action boundary and native canvas ABI remain unchanged.

The laser uses the existing native Zoom control in the mobile drone form. Its
134 subsequent simulation steps include windup, visible frames57 through122,
15 damage opportunities at57,61,...113, recovery and a separate600-step wait.
Releasing Zoom does not abort the clip; holding it can start another clip only
when its wait ends. A folded laser takes priority over simultaneous held burst
input. Neither attack interrupts the other, and each cooldown continues while
the other runs. Native merge/freeze cancels new laser damage with the full wait;
life reset or owner retirement clears the ray and state. Rendering consumes only
the current cached simulation ray.

Activation captures one of four donor sweep patterns and the first native
world/body target distance clamped to4..40. Each active step uses current native
aim and position, a60-unit ray and native current-player collision shapes. World
collision clamps both the offset origin and ray before choosing the closest
eligible opposing body. Hits call the existing damage owner with10 base damage,
normal native powerup/protection/modifier handling and no fabricated beam,
accepted-fire or weapon statistics. The private explicit attribution argument
cannot carry into nested public damage calls. The local laser label does not
change canonical native alt-attack classification.

Two crossed red native TrailSingle submissions use the player's existing trail
texture owner. Psycho Bit and native half-turret models remain visible fallbacks;
source drone/Trooper animations and materials are not integrated. The HUD shows
laser readiness/windup/sweep/recovery/cooldown and the existing Jump, Alt Attack
and Zoom inputs. Android already exposes Zoom through its existing touch mapping.

The four-step MECHANICS choice adds DRONE COMBAT PRACTICE after the existing
zero-bot Scout Drone practice. Entry captures Weavel, Battle and one native bot;
the user can select0..7 opponents and native difficulty. The existing local
population owner creates the human and native bots, with explicit FFA teams.
Only the exact pre-initialization human receives the private rule. Admission
checks the exact created/active population and leaves native bot AI and rules
unchanged. The older wall/drone choices retain their zero-bot restriction.
Invalid raw combat setup is refused before preference persistence. Network,
replay/live recording, campaign/Ship, automatic rematch and server career fences
remain in effect; exact Android handoff capture includes the combat discriminator.

Adaptation reference: SecondHunt41452f2d053fcfa679e33c00a73d120a53f59e61,
PlayerDroneLaser.cs and PlayerScoutDrone.cs, MIT (same retained repository license).
No donor global application flags, bot automation, simulation clock or packets
are imported.

## Qualification

Production source is frozen at `f373421ca0a9383f924b691b214a1b944b5b7706`,
including main through `decea85ca` and the configured-local visibility correction
below. Desktop Release on Mac SDK10.0.401 passes in93.37 seconds with one inherited
CS0649 warning and zero errors. Core SHA256 is
`2bc71280e3ca0b50c247fdeafddf2d721ac0a136a9f608a01eb8e93337854163`;
nettest is `d703c5b56c065aff0078b356d4d8792de966b81bb9c52da04dbb878ff3c70113`
(zero warnings/errors). All6,373 tracked files remain unchanged through this
build and its source-free checks. Later commits `5980274d3` and `80296264e`
change only expected aggregate/selector counts in two source-only capability
test files; all production and nettest inputs remain byte-identical to the freeze.
The final capability tool uses test revision802; source projection receipts keep
these identities distinct. Later documentation does not alter production.

| Check | Result and scope |
| --- | --- |
| Isolated dedicated Release |76.16 seconds, one inherited CS0649 warning/zero errors; retained core `b2506f0077e6183889c3db84260a3b72276a0e288e2faead5f6b19bf4de1ca3c` |
| Source-free geometry, owner and launch policy |495 independent literal laser geometry landmarks,276 wall-owner assertions,559 captured setup/preflight/replay policy assertions |
| Initialized owned Scene |11,949 assertions, including all original4,958 primary checks;134 processed laser frames,15 opportunities,66 visible frames, independent waits, same-step aim/form decisions, native exact10 damage on one actual opportunity, lifecycle cancellation,0–7 configured native bot populations |
| Native bot combat |One unmodified native Samus bot accepted a shot with the private player as its actual AI target and emitted a live registered native beam after24 steps; no bot damage/kill claim |
| Initialized wall-cling regression |1,403 assertions,608 fixed steps, two clings/kick/timeout, zero held drift and native fall recovery |
| Actual owned OpenGL |3,128 assertions; original primary sequence retained; laser0..133, visible57..122, absent56/123, two real native TrailSingle submissions, real merge/death removal, immutable state/health/animation/rays on extra draws,386 laser-Scene texture owners released |
| Actual native menu OpenGL |379 assertions/40 translated intents at EACH960x600 and640x480, including detached combat capture/busy/preflight and prior Aim Lab routes; frozen authored CSS/markup equality and all159 runtime asset hashes unchanged |
| Source-linked host |735 capability assertions including strict authored DOM membership and moved Social visibility; canvas197/schedule28/IME31/Cocoa35 plus full runtime defaults/100 reinitializations; zero build warnings/errors |
| Core-bound local launch and input |Full live-menu suite,347 Android handoff checks, full touch suite plus29 Zoom hit/hold/release/hidden/lifecycle checks; tool builds zero warnings/errors |

Each owned run preserves the3,970-file/845,958,857-byte AMHE1 inventory, with
before/after per-file hash receipts. GPU is Apple M4 Pro/OpenGL2.1 Metal91.7.
The laser Scene runs1,705 fixed steps with11 normal pictures and10 render-only
opacity controls. Actual trail opacity changes258/638/635/577/571 framebuffer
pixels at57/87/113/117/122; pixels exist only in cleared temporary memory. Full
runtime outputs and key tool/package binaries are independently APFS-cloned and
hash-verified; fresh test contexts are removed. No owned-game images, audio, meshes or extracted source
payloads are retained by these tests. Windows SSH previously reached the host but
authentication was refused before commands ran; the authorized Mac fallback is
used, with no Windows laser claim.

The actual damage fixture places one inert native target along an observed ray
using public Spawn; it is synthetic placement with no pinned target, forced aim,
health writes or fabricated hit. Native gravity and knockback continue. Exactly
one opportunity pays10 damage; it does not prove all15 hits, every target shape,
world occlusion edge, powerup/team modifier, balance or bot damage/kill outcome.
Native0–7 population uses the actual shared launcher owner, with no actor added
after admission or AI override. The accepted-fire bot arm is separate from the
inert-target laser damage arm.

Exploratory receipts remain under `scout-laser-exploratory-01`. Its first CPU
run paid the strict native10 damage, then failed a redundant query against the
target AFTER knockback; the corrected fixture snapshots native current-body
geometry just before payment, retaining exact damage and one-attempt assertions.
The7c combined freeze passes mechanics/GL but exposes an actual UI915 regression:
Open Social moved outside the hidden social rail. Existing configured-local
capability tests reject that visible unsupported action. Commitf373 adds the
moved sibling to the same post-publisher visibility owner, preserving dispatch
refusal. New strict initial/publisher/direct-override checks pass. Subsequent
source-only fixture failures at old aggregate488 and Home selector17 counts are
retained; the test-only corrections account for exactly three additional
assertions and one additional authored selector, without deleting old predicates.
All final corrected checks pass. The superseded7c Android snapshot was prepared
but never compiled or packaged.

Both exactf373 Android Release profiles pass clean serial-AOT compilation,
packaging and v2/v3 signature checks with the local Android Debug certificate.
Default completes in270.43 seconds (26 existing warnings/zero errors), APK
SHA256 `12879d0880b2a653c9cdbba3bdda1480e1c6162398d6e7f5409bcdb3714260d7`
(147,871,817 bytes). Package+Experiment completes in274.05 seconds (27 existing
warnings/zero errors), APK
`0a2ff5f18e08f445fc38f5277a99ccdfdd6ff376b8f772a6b569934aceb2fa7b`
(180,015,127 bytes). Both strict APK inventories contain119 AOT libraries per
ABI, all correct ELF64/little-endian architectures. Default excludes the optional
bridge; optional matches the unchanged native closure fingerprint
`65eb5d70c119715a3c941aa68792512d53adf760bee7bc8980fe227fd8403ec9`.
Android core hashes are
`cb3a64f90608d6bf510222f163d28995409cfdfbf30b8287b56ced1d10362fc4`
(default) and `e1e83ce59fa28bb5ba74ec493898df95b05d067c5ae7164c4895773643bb7199`
(optional). All6,373 exact Git inputs and untracked native/nonbuild files remain
unchanged in the isolated snapshot, as does the root desktop core. Both packages,
cores and full inventories/signature logs are retained. The768MiB storage guard
never trips (lowest observed1,100,505,088 bytes); no interrupted-cache retry is
used. Standalone Debug is unrun. Package/signature/AOT results do not qualify
Android runtime/UI, physical controls/GPU or production signing.

Storage recovery is limited to inventoried, completed task-created build caches
only. Exact qualified binaries/packages,
logs, source/Git recreation metadata, native bridge evidence and all files outside
the specified cache scopes remain unchanged. Logical cache sizes are not treated
as physical disk recovery. The full scopes and actual free-space readings remain
in local `scout-laser-7c533de00/storage/` and
`scout-laser-f373421ca/storage/` receipts. The prepared7c source/native inputs
remain; no additional source archive/input retirement or binary compaction occurs.
No user ROMs, private imports, unrelated checkouts,
backups or filesystem snapshots are removed.

Rollback: leave MECHANICS at Native or revert this increment. Remaining work
includes Pirate Blaster/affinity, source Trooper/drone/cannon presentation and
animation, standing-body geometry/headshots, full Overcharged runtime integration,
network/replay identity and physical Android gameplay acceptance. These mechanics
are an explicit native-fallback practice mode, not completed imported characters.

PR: [916](https://github.com/AntiNotAnti/Project-Prime/pull/916). Later unrelated main
changes are not claimed as covered by this frozen whole-binary qualification.
