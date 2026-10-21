# Selected Trooper body and cannon in Arsenal practice

Windows runtime and limited capture inspection pass. Full imported characters
remain unfinished.

The existing Hunters BODY preference now has an explicit presentation route into
the existing offline TROOPER ARSENAL practice. Power/Ice/Wave/Plasma select their
own body and fixed cannon; Native retains native models. Cosmetics never select
the practice, grant abilities, change affinity or alter native weapons. No new
customization surface, preference, Hunter ID, native UI intent or wire/replay
field is introduced. The existing Arsenal notice explains the selected body,
cannon, native bolts and unsupported-pose/drone fallback.

## Launch and ownership

`LaunchPlan.LocalArsenalBody` is separate from the existing Native-gameplay-only
`LocalBody`. It defaults to Native and admits only the four finite Trooper
appearances in explicit Weavel/offline/Battle/Arsenal practice, with the already
admitted zero to seven native opponents and standard native movement/weapon
rules. Mixing ordinary imported body/drone requests, other practices, network,
replay, recording and unsupported platforms is refused. All launch/preflight
entry points revalidate the request before reading imported source files.

The closed `LoadForArsenalBody` factory captures the exact same-generation body,
materials, strict clips and generated cannon on the worker before the existing
`EnsureScene`/`LoadScene` barrier. Unknown, missing, corrupt and outdated explicit imports fail
launch, rather than silently discarding the requested appearance. Existing
authoring receipts, recipes, source materials, bundle versions and provenance
remain unchanged. Their historical authoring fields do not self-grant runtime
authority. Ordinary body selections cannot grant Arsenal authority, and the
Native body owner rejects an Arsenal selection.

After full native initialization, a separate sealed Arsenal actor capability
binds the complete35-joint/1,970-triangle body and35-joint/206-triangle cannon,
each with all three source program groups and bounded shader gathers. Its
identity includes the actual Scene/lifetime, local Weavel slot0, exact immutable
pack/appearance/generation, native body/gun references, configured native bots,
custom weapon registry and actual native Pirate Blaster owner.

Normal multiplayer initially loads an unspawned actor. Cosmetic resource binding
does not invent a Spawn, health or semantic life. Source currentness begins only
when the real native Spawn publishes an available Blaster life; subsequent life
changes reset presentation clocks from that owner. Death, wrong life, disposal,
changed actor/rules/registry or retired GPU/import context suppress source
packets. Step/Draw never reload a retired attachment.

A distinct sealed view-model subowner can own only the exact cannon instance,
native gun, pack and appearance; it cannot own body, alternate-form or other
actors. Currentness additionally requires the actual Blaster `Selected(player)`.
`CurrentWeapon==PowerBeam` alone is insufficient because the private Blaster
can suppress native fire while that enum remains PowerBeam. Native weapon
selection draws the native gun. Arsenal drone and planted half-turret retain
their native rendering.

## Pose, fit and fallback

The body reuses the existing seven-clip full-rig Trooper pose, private RootPin,
torso layering and native-clamped aim. Source root motion never moves gameplay.
The cannon reuses its own qualified fixed ordinal1/time0 cut. It is not a new
Pirate Blaster mesh or an authored firing animation.

Existing positive feet-fit scale times0.7, fixed source-to-cannon basis and native
late-prepared gun/muzzle-offset transform retain exact native effect-tip
alignment. No donor muzzle translation, fake recoil, native beam/projectile
origin or camera change is introduced. Body and cannon palettes remain separate.
Source material clocks advance once per supported completed native60Hz step;
the cannon clock additionally requires selected Blaster and freezes otherwise.
Pause, repeated pictures and fractional draw interpolation do not advance clocks.

Existing full native status/opacity/flash/protection/shadow/team/morph and
unsupported pose fallbacks are reused. Source cannon additionally requires its
native supported gun animation and prepared first-person gun/effect transforms.
All source segments validate before submission; fallback never leaves half a
source cannon mixed with half a native gun.

## Evidence checkpoint

Production source freeze: `9f61b262a9cd7d240fed7ace6b1b5c906d485c32`;
source tree `01a37ce29fefc2d68f80888e0e3f0694db879cec`.
Windows desktop core SHA256:
`c5e51a5973591e300cf39ab08db6fca7b8c799cec45c3db7a39c99c6b14dc7b2`.
Desktop/dedicated builds pass with the existing CS0649 readiness-fixture warning.

Tests first fail on the preceding core after exactly one check for the absent
explicit `LocalArsenalBody` contract, before source/Scene/native GL IO. On the
new freeze,562 pure finite-admission/Hunters capture assertions and the existing
971 offline-character assertions pass. Desktop2,708/server2,690 source-body,
24 filesystem/910 synthetic-rig,3,107 replay-format,694 replay compatibility and
architecture checks also pass. Local regression receipt SHA256:
`5a9beaf3516c90534c0465c76316466dfdbc08b40955bd95d13e5c76541cc095`.

The existing owned-AMHE1 headless Arsenal Scene regression also passes3,473
assertions on this core: native0/6/12/40 cadence and UA, native weapon selection,
aim, morph/retirement, configured bot population, projectile contacts, marks and
native generic damage/kill/score accounting. Its damage scenarios explicitly use
inert targets placed with public native Spawn and real mouse aim; they do not
qualify native-bot damage or imported cosmetics. They preserve the existing
gameplay baseline separately from the new source presentation tests.

The first actual Windows Scene/GPU smoke passes153 checks on this core and tool
`27ab68f0c53453ca34c58830f6c768cc7aeb805634a510b7eddfc57e2e6cae17`.
Native and all four imported appearances each launch through actual MatchStart
with one native bot. Before genuine shoot-to-spawn, the native weapon life is
invalid and source currentness/packets remain absent. Native input then spawns
the selected Blaster with60UA/zero emitted shots and activates the captured
source owner. Each appearance renders its own complete35-joint/206-triangle/
three-program first-person cannon and35-joint/1,970-triangle/three-program body,
with no partial submission, GL errors or extra-draw mutation. EndScene retires
both parts. Owned RVZ/native inventories remain unchanged and temporary derived
imports are deleted. This initial run saves no captures.

The final strengthened no-capture run passes557,045 checks on the same production
core and tool `e3d627d579d38be53e81be83d196b872837c2c4b50e963d5e515e087a2de6e1c`.
Native and all four appearances retain the exact1,094-step canonical hash
`9a20d4831737b5ee58084412205ff357916a75e08018fd1354ed4b448d2f6114`.
Each source case observes368 complete source body pictures and2,782 complete
source cannon pictures,9,450 CPU segment-gather checks and242 actual GPU palette
slots across11 body/weapon phase keys. Maximum gather error is zero; maximum
source-tip error against the current native prepared tip is1.9073486328125e-6.
Both native prepared gun/effect transforms are required and compared on actual
source gun pictures. The Blaster's existing NoMuzzle policy emits no observed
native muzzle/charge effect geometry, so rendered-effect tip checks are zero;
this is not a visible muzzle-flash or beam-emergence claim.

The matched trace covers real burst/cooldown/payment/tails, native weapon
selection/reselection, movement, jump, native alternate form, declared public
lethal damage and genuine respawn with one actual native AI bot. It retains
exact native actor/selected AI scalars, animation, health/ammo/UA, aim/muzzle,
prepared gun/effect matrices, Blaster state/timeline/marks, native/private bolts,
effect/particle state, RNG and totals. Only each fresh Scene's match/authority
namespace is canonicalized, after validating its exact current-owner relation;
slot/generation/life and raw owner contexts remain checked. Trace rows follow
completed steps and native effect preparation. No hit or mark was observed in
this script; the separate native damage/score regression remains distinct.

Pause/repeat/status guards,90/120/240/540 fractional schedules, source clocks,
exclusive source-file locks, native first-person tip/projection/normal basis,
old life/queued-part fences and active context retirement pass. Actual launch
checks exercise all four unchanged13-file generations and literal model/material/
bank/cannon/rig/ANCS pins, wrong-profile refusal before IO, cancellation and
BeforeCommit rollback. Missing discovery/materials, corrupt receipts/components
and outdated explicit imports refuse launch while retaining the prior Scene.
Remove withdraws discovery while the already captured immutable residents stay
usable; explicit refresh/retirement cannot resurrect an old owner. Same-byte
reinstall, fresh repair and relaunch use fresh captured owners.

Final no-capture raw log SHA256:
`a6e5b6bc808958d505b3d6d7fecbf99ef98b254a88ada6cfe6d7f26541a4ec0e`.
Final combined metadata receipt SHA256:
`33ac321e6df3013bc6f044b5616d0134c258cd7978acbabd1c8da9afac8af28e`.
Runtime test commits: `13b9bc2b4` and `08ba88528`; the latter additionally compares
the entire canonical native record between first and second actual pictures,
including all native/private bolts, selected AI scalars, particles and matrices.
The production source tree remains unchanged. The earlier557,044-check run and
its raw log/receipt remain retained separately.

Three fixture assumptions were corrected with original failures retained.
Native first-picture `PrepareEffectDraw` drains pending fixed effect steps and
may consume RNG1; a second picture is stable. Gameplay/source clocks and palettes
remain fixed across both, while full RNG is retained in matched traces after
native preparation. The initial source-only paused-call guard sequence also had
to run identically in Native. Finally, `FrameTiming.Reset` retains measured-Hz
diagnostics: a preceding source fractional tail enabled high-refresh native gun
presentation in the next case. Resetting diagnostics and pinning the matched
script to60 restores exact prepared matrices. No production change, matrix
rounding, RNG omission or weakened pose tolerance was needed for these failures.

The separate capture run passes557,095 checks on the same core with tool
`ae3dd26f381ebe459229ad6aa19f9183f9e3ed59ef542637aeb45ba7356d449a`,
retaining the same1,094-row trace. Native and all four source appearances have
one first-person paid-burst frame and one third-person moving frame each.
Actual view/projection/eye/type/FOV/zoom and form-handoff state match for each
comparison; no Roam call or active Scene handoff is involved. Manifest SHA256:
`192434e629816ab6b34f11339cca692a17ce713d6287084dde5a24ea7ee8c446`.
The primary inspected all ten images, verified every hash and exact parent,
then deleted each image individually; zero remain. Inspection receipt SHA256:
`7b0dea93380dc557ba88d9a4733caa488e792deac333642b9c890abdbfdf36c6`.
All four distinct source material sets are visible in the match alongside native
world/HUD. The fixed first-person cut sits mostly low/right and presents a large
colored pointed plate; third-person lower legs extend below the image edge.
These pictures do not qualify broad view-model fit, firing-animation fidelity,
every camera angle or exhaustive occlusion. Opponent combat and broad visual
acceptance remain unqualified.
Online/per-seat appearance, accepted remote ability/content authority, replay,
source projectile/effect programs and broader character engineering remain
unfinished. Android remains deferred; no Mac or other-platform claim is made.
