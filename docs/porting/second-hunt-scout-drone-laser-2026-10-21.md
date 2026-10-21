# Imported drone laser presentation

Alpha/Gamma solo Scout Drone practice adds the original source laser body pose
over the existing native laser. The private source owner reads the native public
next-frame counter; it never consumes authored damage/sound events, emits beams,
changes cached ray endpoints or modifies native input, collision, damage or RNG.
The complete33-joint rig,1,390 triangles and two source material groups retain
the fixed derived Ready0 pivot and scale0.3 from the earlier runtime slices.

Admission remains the explicit solo Weavel Battle/playtest with zero bots,
native movement and no other body cosmetic. Online/replay and ordinary native
launches retain their existing fences. All status/opacity/morph/shadow guards
remain. Native laser drawing still follows the mobile-model choice. This slice
changes no wire/replay/native ABI, import recipe, bundle version or source bytes.

## Source and phase

The fifth exact admitted primitive is ordinal29 `A_cuttinglaser_drone`, original
ANIM `6d6b6f9f`, SHA256
`b9fd4217ace08e9431ead637825f8125e28978c212c465395e0c53edb6d8675c`.
It has68 keys and original duration2.233333110809326seconds. All five source
primitives are privately derived with the unchanged pinned-root/pelvis policy
and validated before Scene creation. The raw bank and shared rig remain intact.

Presentation state4 exists only while native `DroneLaserFrame >= 0`. Public
frame `f` in0–133 samples `D * min((f+1)/134d,1)`, including windup and recovery.
Entry Snaps; return to Ready uses the same held-pose0.12second/eight-step blend
as burst. Unsupported steps clear held blend history; re-entry samples the
current native frame. Extra pictures and pause cannot advance the pose.

The phase follows the pinned MIT SecondHunt `41452f2d053fcfa679e33c00a73d120a53f59e61`
DroneBody/NativeBodyAnimator advance-before-picture convention (Copyright2020
NoneGiven, LICENSE retained). The public next counter is distinct from the native
processed frame. At native processed57/public58, the source body samplesD*59/134,
approximately0.983333235seconds; this is about two ticks ahead of processed57/60.
No exact authored DamageOn/Off synchronization is claimed. Native beam visibility
remains processed57–122; its15 payment opportunities and one sound at57 remain
owned by the existing simulation. Processed133 returns the public counter to-1
and begins the existing600-step cooldown.

## Qualification evidence

Production freeze: `acb64a172896735725acbc7f57645c25304270f9`.

The initial actual Windows match smoke passes254,819 checks. Both appearances
replace the fixed script's268 native laser-body observations with complete
two-group source pictures and zero laser-body fallback. The existing1,418-step
native trace remains
`aa44ee6f8e9a3a78fe7ccf1b58b7e79db1d5cdf3fa39731a5218e43bd25acefc`;
the separate420-step burst/recovery trace remains
`8a738ab661fdb308a8e140142feca62602e83a4e995bb9fb666671d89bea299f`
across Native, Alpha and Gamma. The native visible laser count remains132 in
the repeated-picture script.

The expanded no-capture run passes364,571 checks. A separate3,083-step genuine
input sequence produces all four patterns2,3,0,1 in four natural activations,
then a fifth laser after a160-step collision-aware approach toward a wall.
Native, Alpha and Gamma share exact trace SHA256
`01f2395ccd4525d390e56f1a8285dbbb29b90b2702a174611f2fedc51f12e8e4`.
Each activation retains134 active frames,66 visible beam frames,15 payment
opportunities, one sound and600-step cooldown. The near-wall cached endpoint
clips to approximately2.7745256units despite the initial distance floor4.
Five cloak steps and two additional native opacity guard steps retain native
fallback, then source re-enters at current public laser frame78. No opponents
are present, so this supplies no accepted damage/kill claim.

The final raw log SHA256 is
`8fbd36ff76c67f6c5fb2ebef7d022733ae0b3e1c7376e75065559ae3d67304f6`;
compact `source-drone-laser-qualified-receipt.json` SHA256 is
`425bc0d4ab06a0fd3f4f59095761a08696674a2efac66f4b364d5d347409debc`.
The same binary also passes legacy burst255677 and Ready210448 runtime checks.
Per source appearance the combined no-capture fixture counts6,092 source pictures,
54 native pictures,3,613 repeated draws,3,046 full33-joint CPU skin checks,
5,060 phase checks and72 held return-fade steps. Actual GL uniform readback
checks17 selected phase/fade keys in the final batch per shared program, rather
than claiming33 simultaneously resident shader slots. All imported components
stay exclusively locked during the script; captured clip-read counters remain
unchanged. Pauses, fractional schedules and owner/context retirement, including
retirement during processed laser90, pass. Source/native hashes remain unchanged
and disposable imports are deleted. Final test commit is
`bfca0ee213ecf1f83d6669530b66b17e6a7c186b`; source/native trees remain
`a1f57c2a4589da5d296a87c5f3624a63537ab77f` /
`491e699c7767dfb1bb9deaf651688315d068387a`.

The optional capture/diagnostic-picture run passes366,858 checks on the same
frozen core/tool. All26 images were viewed, verified against the exact filename/
SHA manifest, then individually deleted; none remain. Both appearances show the
complete source poses with a coherent visible native beam at processed57/90/122
from gameplay, front and oblique cameras; each also has a native-fallback view
and three near-wall views at57. The beam fades at122 according to native draw
timing. No beam-start clip, mesh recenter or scale change was needed for these
samples. HUD overlap limits gameplay-camera detail, and these selected map/aim/
lighting views do not establish exact source-locator/native-emitter equality or
broad art/occlusion acceptance. Diagnostic cameras are restored without stepping
simulation; exact native state, source clock and pose remain unchanged.
Capture inspection receipt SHA256 is
`506e6e73b86f1628fb9d9ed054965671f4e92f8411df6aa0611604c1381fca05`;
the26-file manifest SHA256 is
`b9825168220e6fbe8f6c735c4bb51cbe380636822ccf4d47c64311a7e554ba12`.

Desktop/dedicated source-body2708/2690, character filesystem safety24 and
synthetic rig910, replay-format3107, offline-character971, replay-compatibility694
and architecture checks pass. Actual native Hunters contracts pass469 checks at
each1280x720/640x360 viewport with the new hint; these use the native CPU backend
and no GL window. Native bridge validation passes. Desktop and dedicated builds
have zero errors and the existing CS0649 readiness-fixture warning; focused tools
build with zero warnings/errors. Regression receipt SHA256:
`df7da7f245db0c8cb5b95f05024339bd6bc020f6306b0d447d3179c979bea7d4`.
Rev2 recipe/component metadata checks also pass199 on the frozen candidate core;
their separate receipt SHA256 is
`e3f1b94411573f5baa02633cbf98219e99ca38c55a4ce73cfb3a687890929974`.

| Binary | SHA256 |
|---|---|
| Desktop core |`5e6bfcb147a3d472eb7ec1045684e1ea040c21aaa3cebccf0672683ef097c151`|
| Dedicated core |`0352f0a0d20523ce717936b996911cb5687c5c5ffff4a59f0b179f96d259f05e`|
| Initial runtime tool |`77277c408208a0be68df9e704dcf0b9814d0572c4ba5d2ecc7510a3753335e27`|
| Qualified runtime tool |`c4c41f22b3ee7de08a4b188c32dbb8947df6b1f31e93e4278202a9882a45645f`|
| Nettest |`740266003b23222e7bf0fb55b5c7ed890fe7007cfe9c0197d34c150945e815a0`|
| Native Hunters tool |`165138f0c8e17d4df6417749182162c40677fdb4ae793af36b02c1564b9e72fe`|

Tests first on frozen burst core `8480e22d…e6914d` refuse the missing fifth
ordinal before owned/native asset I/O, Scene, GL or gameplay. Raw failure log
SHA256 is `2e9f3b502517bf808a6fb4a12966a5df893e481b8e38773f278184cac3bed26c`.
The complete previously qualified burst binaries/dependencies were copied and
individually hash-verified before any new build.

Independent frozen83fe parsing/sampling passes twice with identical metadata
`ce69d512007cec13aea01a2d91bdbcbf298680a264b9678bdb7c092a401a851c`:
2,126 full-rig poses and3,535,538 source-position evaluations, including all134
public laser phases per appearance. Independent raw sampler error is zero.
Separate native-boundary policies contribute1,886 assertions. Float reproduction
hashes and held return fades are distinct from independent double math; existing
rigidity and acceptance tolerances remain unchanged. Oracle receipt SHA256 is
`cfee03b6859490da12145d0fb29f338b00e099d2f01ea9d70162c3c6dd06104b`.

The expanded runtime's first retained failure was an exact-float oracle defect,
not a production correction: its held-fade reproduction multiplied rotation by
a translation matrix, whereas the unchanged renderer assigns `Row3.Xyz`
directly. The two equivalent operations differ in two signed-zero float bits on
source bone31 at each of the eight fade steps, with numeric maximum difference
zero. All134 raw laser phases, the held endpoint and Ready target hashes already
matched. A separate frozen-parser diagnostic reproduces the exact assignment
operation; the earlier oracle and failure log remain intact. No tolerance was
relaxed and no production pose/sampler/native state changed.
The corrected diagnostic passes twice with metadata SHA256
`f2582fa1f66f066d1411779c2d8ab1e6cd49179e93239475db6ef89db47bb7d3`,
exact eight-row gold `1142cef016502577f28899521d39c0d38d801e3fab40604f38ca824e25420793`
and receipt `db337bb7d0e5cae545d19fb9f572a886fb979876b81752c4ab56baa982343565`.
Both appearances pass all134 phase hashes and all eight corrected held-fade
hashes in the actual candidate fixture. The raw pose and independent double
oracle remain unchanged.

Independent whole-mesh/ray research finds the unchanged native centerline meets
an opaque front triangle approximately0.006–0.016world units from its origin at
selected phases/patterns. That is shallow emergence through the source surface,
not evidence of an unobstructed central opening or a broken visible beam. Source
gun locators differ from the native origin and never redirect it. Fixed mesh fit,
native origin/end and native beam draw remain unchanged pending actual visual
qualification. Fit receipt SHA256 is
`594547675439499ecbd26432ba0008679672d7528838ca12252c0f4c2ea47ca0`.

Source movement/hit animation, combat/arsenal admission, independent multiplayer
seats and the remaining character/integration engineering remain unfinished.
Windows is the user's selected machine; Android work remains deferred. No
external CI/review approval or full character/art/performance acceptance is claimed.
