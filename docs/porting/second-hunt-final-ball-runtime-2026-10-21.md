# Overcharged Dark Samus composite ball

The Overcharged Echoes appearance selected in Hunters now has its own composite
ball in local offline Samus matches: transparent shell, front-facing vents,
folded red vessels, outward blue lines and a beating heart. It shares the existing
per-player source appearance owner, transactional importer, source-rig loader,
GX compiler and Scene graph. Native roll, position, collision, bombs, effects,
camera and60Hz gameplay remain authoritative. Defaults remain native. This is
another limited cosmetic slice; full character integration remains unfinished.

## Independent source bake

The pinned donor is `41452f2d053fcfa679e33c00a73d120a53f59e61`, specifically
`Import/Hd/VeinBall.cs`, `Assets/HdBall.cs`, `Campaign/CampaignSuit.cs` and
`recipes/hd/MP2DarkSamusFinalBall.json`. Adapted code retains MIT attribution
and the repository LICENSE. Source payloads come only from the user's verified
local G2ME01 disc through the existing bounded archive requirements.

The full original Final `Battle_3` body uses CMDL `faa12c05`, CSKR `918e83d3`,
CINF `4abc8a50` and all39 source joints. Fixed ordinal52
`A_outofBoostBall_toChargeReady_dark_samus` is strict original ANIM `c2c4a405`,
sampled at time0, SHA256
`c29325b7b5ae5eb0efc3ae336d9e5a1d260a3aeef57745fe2b299851f2c0a872`.
The complete source skin is evaluated in double precision before folding.

Five semantic parts produce8170 whole triangles in six material batches:

| Part | Triangles | Material source |
| --- | ---: | --- |
| Shell | 2220 | Whole original ball, Final body material3 |
| Vents | 2220 | Whole ball again, derived original glow texture1 program |
| Red vessels | 822 | Final body material7, excluding the heart |
| Blue lines | 2098+722 | Outward selected Final materials4/5 |
| Heart | 88 | Compact connected component of Final material7 |

Connectivity uses original position indices. Only the88-triangle component
meets strict maximum posed extent `<0.18` and count `>40`. Body center uses the
unique inner-material positions'2nd/98th percentile box with round-to-even
indices. Vessels fold at radius0.445 with exponent0.2 and a99.5th-percentile
distance denominator; this is not clamped. Lines require strict outward dot
product `>0.25` and fold at radius0.47. Heart center is the mean of its59 unique
positions, enlarged2.3 times. Each folded part gets its own area-weighted smooth
normals. Shell/vent original normals and raw UVs remain unchanged; every selected
T1/T2 coordinate equals T0 before the existing transport aliases are admitted.

The generated static composite uses the original complete two-joint ball rig
and positive bone1 weights. It does not pretend that the baked39-joint body
motion is a native32-joint mapping. Original ball Ready1/time0 was independently
measured as exact identity on both joints; it cannot layer source travel above
native rolling. Only the heart receives a separate draw-time scale about the
shell center, after fixed source skin and before native ball placement.

Composite GLB SHA256:
`57158cb41d062bab7b93920016855c8fd7a8b899a48c8dde23e4838e4b95beab`.
Fourteen-input composite receipt:
`8eba953c52a647a6e4c8bea6c66b289bd5f4bc95abe692782959248522bb4a55`.
Own raw ball material archive profile8:
`28d5d167d48281c98234335eb8b6e766942d2832f6dbbcb35ffeb8854827e1eb`.
Full source inputs, current generation, model/provenance/animation hashes and
exact appearance/part identity are checked at load/revalidation boundaries.
Normal ball, Final body and cannon leases cannot substitute for the composite.

## Material and clock ownership

The composite derives six closed programs from immutable own Final body and
ball archives. Shell uses original Alpha material3 with its rim texture's alpha
remapped to115–242. Vessels/heart use original Opaque material7. Blue4/5 preserve
their original Additive programs with private filtered/tinted line pixels.
Vents use the original ball glow texture, its selected texgen, and a closed
one-stage unlit Additive program. Only vents cull back faces; all three additive
batches draw twice. The GX draw scope restores culling, program, texture, blend,
depth and color state. Original body/cannon programs and decoded pixels remain
unchanged; the cannon's0.55 shell-alpha variant cannot authorize ball materials.

Texture copies are bound by independently measured filtered-pixel hashes.
GPU cache keys use the actual decoded variant pixels. The fixed program budget
is39: existing28 body programs, at most9 cannon-alpha variants, normal ball1
and derived vent1. Complete shader source remains the program identity; culling
and copy count stay on each owned binding. There is no second renderer.

The donor Gaussian heartbeat is adapted to **48 completed unpaused60Hz steps**
per cycle. `HeartScale(seconds)` retains the donor's floating-time equation;
`HeartScaleAtStep` canonicalizes the owner's integer cycle before evaluation.
This deliberately prevents floating division at2.4 seconds from choosing the
previous endpoint: the donor Gaussian has different endpoint values. Tests
retain that direct-time diagnostic and prove bit-identical step cycles, even
near `long.MaxValue`. Material clocks remain on the existing completed-step
owner; heartbeat uses the same counter and resets per life. Repeated pictures
and pauses cannot advance either. Palette and normal/texgen transforms include
the pulse exactly once for the heart; shell, vents, vessels and lines retain
their fixed geometry.

The outer shell's complete posed bounds define centering and fit to the native
collision radius. Morph/unmorph, overlays, dynamic shadows and other unsupported
states retain complete native presentation. Context/appearance retirement
releases all parts and cannot resurrect through subsequent native morphing.
There is no draw-time IO, RNG, gameplay mutation, new UI intent or bridge ABI.

Final bundle8/body6/ball4 adds composite GLB/provenance and its own ball material
archive. It has27 committed files,25 required components,13 ball roles and
manifest role3. All22 prior non-manifest/index components stay byte-identical.
Actual frozen bundle7 cancellation preserves its old discovery pointer and all24
old files. Normal bundle7's recipe, generation and all25 files remain unchanged.
Existing Check/Update/repair/reuse/Remove transactions own the whole generation.
The source viewer remains Body/Cannon; no new ball viewer is claimed.

## Windows evidence

Production geometry/material implementation is `dfd93723c`, with canonical
step-clock/runtime fixture freeze `a19100979`. Desktop core SHA256:
`5a554d0cca117290dc1b7dd2d3004b9ef5108d70b4075eb6a7d52eeb9131a9be`.
Dedicated core:
`b5cd36c6300cb6df34e96c44c2ff827caa1c9d4c890381ce44c2370b219f7f53`.
Both Release builds pass with one existing readiness warning/zero errors.
External tool:
`75ef2de671efa37492c94b3836a4636075b5bbd0a9ba3afcd9550b8f30bd87f7`.

- An independent original-source bootstrap on authoring core `e54eaff0…fc8b6`
  compares all24510 corners with zero position, normal or raw UV error, exact
  six-batch inventories/weights and Ready1 identity. It proves22 old Final files
  preserved and normal output unchanged without granting runtime authority.
- Independent runtime-source CPU probe:54169 checks/59 refusals. It repeats
  original39-joint pose/selection/fold/normal/UV calculations, verifies all
  generated corner bits, own raw archives/private pixel variants, complete rig,
  file locks, missing/corrupt inputs, cross-lease refusals, retirement/no
  resurrection, no RNG/cache writes and cleanup.
- Full pure suite passes, including12839 Final presentation/clock checks and
  all existing GLB/material/GX/cannon/normal-ball tests. Test tool SHA256:
  `1b01bd33348dacfaf42ad33a68b71b77e5fb9c1fbd20edce92cd147f6f57f97f`.
- Actual NVIDIA RTX5080/OpenGL4.6:376487 checks. Native, normal and Final each
  produce the same1415-step gameplay trace SHA256
  `f427384c4531686dfaef0d11cb0f35fce029e55dc4823bea0d85e11f136d6ca6`.
  Both source balls render1372 pictures, including separately counted pause,
  guards and simulated90/120/240/540 schedules;110 pictures follow respawn.
  Final palette/center error is at most0.00000213249; fitted shell radius matches
  native exactly. GPU world projection/part palettes, independent heart pulse,
  native rolling, real owned bomb, declared injected death/native respawn,
  exclusive import-file locks, repeated draws, fallback and retirement pass.
  This does not claim opponent-caused death, physical controls or achieved refresh.
- Actual launch66, Body/Cannon viewer1787/94frames/two contexts, and Trooper
  shared-render279841 checks pass on the same core/tool. All four Troopers keep
  their675-step native trace and native alternate forms.
- Desktop source-body2708, dedicated2690, root24/rig910, replay3107, offline
  policy971 and architecture pass. Final nettest SHA256:
  `b7b05bfe99d00dcfbfb37b038a98a4f6fd56f36258fe79cce5b2d7f34c76532e`.
- Owned migration records3708 passing labels on the earlier `3100d364…21a4`
  binary, before the heartbeat-only follow-up. Source authoring is unchanged.
  Actual frozen v7/v6/older migrations, cancellation/repair/reuse/removal and
  all369 files across three independent baseline directories remain unchanged.

Main PR956 remote package hosting and PR958 detached replay bootstrap were
inspected and merged without character-path conflicts at combined source
`3bf6d3c2df079701f7583ca9f7f0c1893acfac46`. That inherited main raises protocol
to83; this source cosmetic adds no wire or replay change. Combined desktop:
`1e0305adb833be179922b8e387f69788364bb96da8b008d97be069fa0a80f43b`;
dedicated:
`f812e390a8a3548adbd229e0aa18d4f96818a9705d18958ae8d9b33067c6c804`.
Both Release builds pass with the same existing warning. Combined external tool
`74ec9489178a8a71e3a4e0ddc2816ef4e326c73e3af48e18325e50f3e670fd00`
passes376478 actual world checks with captures disabled (the earlier run's nine
optional capture checks are absent), the same1415-step trace, both balls1372
pictures/110 after respawn, and66 actual launch checks. Combined desktop2708/
dedicated2690 source-body, root24/rig910, replay3107, offline policy971 and
architecture pass. Combined nettest SHA256:
`e1ff303dd2750e1d28f2ee71bc1ed31a91bd07d85644da3e5bdb11b635677a2f`.
Independent pure/source/migration/viewer/Trooper evidence retains the explicitly
frozen binary identities above; it is not relabeled as a combined-core run.

Raw failures remain retained: the first authoring attempt wrote Final's shared
temporary manifest instead of its appearance-local staged manifest; Update-all
caught this and the corrected importer passes. A culling API compile mismatch
was corrected before runtime. A private helper's stale incremental assembly
reference was isolated into a fresh output. An initial research vent oracle
incorrectly multiplied byte RGB by255; its corrected literal donor calculation
and independent CPU fixture agree on the pinned pixel hash. Initial pure tests
overconstrained unreferenced prepared generations on cancellation, then exposed
the genuine floating-time heartbeat endpoint issue described above. No failure
is relabeled as a pass; source/provenance geometry pins are unchanged by those
fixture/clock corrections.

Eight normal/Final ball-roll/bomb and cannon idle/fire captures were inspected,
hashed and individually deleted. Final's translucent blue shell and red heart
are visible in the native arena/HUD. This is not an original-game art/lighting
comparison. Logs/metadata remain ignored under `artifacts/sh-integration/final-ball-*`.
Owned Echoes RVZ SHA256
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`
and native inventories are unchanged; temporary protected imports are deleted.
No payload/capture upload, CI/external-review pass or complete-character claim
is made. Windows testing follows the user's preference; Android remains deferred.

Rollback is Native selection or disabling custom cosmetics. Full source gun
animation, normal's procedural core/trail, remaining transitions, imported
drone/ability presentation, other weapons, multiplayer/replay identities,
original lighting/fixed-point GX and other-platform qualification remain work.
