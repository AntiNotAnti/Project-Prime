# CW-06b — arm-mounted rifle presentation

Design base: CW-06a `0c3a281b683ac062848bc21a7fe86ef24e194cc5`; integrated main `33a0de54083bfa4d04927adddc35baa99816a20a`.
All geometry, animation, audio and VFX in this slice are cosmetic. Existing
availability/pickup acquisition, controls, magazine/reload, source certification,
collision, damage, native identities and network/replay formats remain authority
contracts. There is no native SetGunAnimation call or imported creator code.

The original +Z-forward M7 module mounts to the already-presented R_elbow on each
of seven native rigs, using explicit per-Hunter scales and local orientation.
The native arm/cannon remains its support, avoiding floating hands and a risky
new two-handed skeleton. First person uses the exact prepared late camera/gun
transform within BeginViewModelItems; third person submits before canonical
bones are restored. The module's visual muzzle sits above/ahead of the supporting
cannon. It never overwrites native emitter/bone/source-aim matrices. Missing
resources retain original three-box held geometry. Native LOD selection controls
world detail; Android first person uses the small LOD1. Flat palette materials
require neither texture uploads nor a UV/image decoder.

Fixed-step tracks own raise, recoil, bolt, magazine and flash clocks. Display
sampling interpolates these values without advancement. Scoping lowers/fades
only first-person geometry; remote scoped rifles stay visible. Full owner/life,
receiver bootstrap, manifest and immutable weapon revision fence each online
track; offline presentation is explicitly tagged. Baselines restore silently.
Monotonic sequence plus exact source bootstrap prevents duplicate/old cues.
Current life/form/selection reconciles before each fixed-step cue clock; entering
morph cannot emit a due bolt cue. Prediction/refusal affects only its own current
cosmetic shot; an old refusal
cannot cancel a newer accepted flash. Rebinding/selection loss stops old tails.

Four original integer-synthesized PCM cues (shot, bolt, reload, empty) are
prewarmed into eight fixed player voices on the existing shared output. Each
scene captures its original device; it never owns/stops the shared device.
Cue/gain publish as one atomic command; audio callbacks interpolate immutable
samples and never wait, allocate or decode. Retirement fences all callbacks
before removing components from the captured mixer and releasing them. Missing
output uses the existing native SFX fallback. The layer is distance attenuated;
it is not a new positional sound engine. Replay ownership changes stop live
rifle tails. Headless scenes neither open model resources nor construct audio.

Original muzzle boxes, cross-ribbon tracers and contact sparks use the established
pooled procedural renderer. Offline tracers read the tagged projectile's existing
prepared draw pose; online tracers read accepted launch/contact facts. Cosmetic
contact sparks require canonical removed health and never imply additional hits.
The first two offline flight steps can join the exact prepared local barrel in
world projection. Recent online local launches may do the same only with exact
current bootstrap and a recent authority world; receipt age alone cannot
authorize that join. Projectile spawn/back/front, muzzle and aim stay untouched.
Native beam drawing remains unchanged. The original vector ammo icon and
precision reticle preserve configured custom crosshairs and HUD visibility.

Prewarm runs on the scene owner before draws. Readiness loss and scene/visual
teardown release the model, voices and tracks. New resources retain no GL IDs;
the existing scene Box/Quad shader pipeline owns all transient geometry. No
asset/path/authority lookup or timer advancement occurs in a draw.

Validation must distinguish synthetic input/display sampling from physical
controls or achieved display Hz, and declared seven-rig presentation grants from
the separate genuine public pickup checks. Rollback removes this cosmetic
binding and restores prior fallback presentation without changing the live protocol76 or historical custom-pickup boundary75.
