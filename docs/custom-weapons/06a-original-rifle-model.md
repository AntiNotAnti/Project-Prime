# CW-06a — original M7 model and bounded presentation data

Base: main `b1cc9a26cdd79481b89b1650ba72fd45beb04ef1` (CW-05t merged).
The user's world-pickup requirement remains the acquisition path. This slice
replaces its three-box proxy with an original low-poly rifle resource; it does
not mark the entire CW-06 presentation/rig/audio acceptance complete.

The built-in asset is a small JSON box model with two LODs, six opaque flat
materials, a muzzle socket and rigid body/bolt/magazine groups. Its numeric
source and CC0 dedication live in `Assets/Weapons/RiftlineM7/`. The palette is
intentional flat-color artwork. No third-party/extracted asset is distributed.
This narrow built-in representation has no external paths, texture/audio
codecs, scripts, shaders, animations or creator input. CW-11 owns package input.

`CustomRifleModel` checks compiled resource key, size and exact SHA-256 before
its CPU decoder. The decoder requires unique exact fields, schema/basis, bounded
finite coordinates/materials/groups, two bounded LODs and a muzzle aligned with
the forward geometry extent. Immutable arrays remain private; callers borrow
read-only spans. `SceneCustomRifleAssets` prewarms once from the simulation-owned
presentation update while custom availability is active. Native-only/headless
scenes open no model. Readiness loss, custom teardown and visual/context teardown
drop ownership; failure retains the original three-box fallback.

`CustomRiflePresentation` submits pooled vertex arrays into the established Box
render pipeline; its scene returns them as usual. It creates no GL IDs, shader
variants or texture handles. No draw opens assets, advances gameplay or queries
authority. Both offline and synchronized online pickup rendering call the same
model submission. Native IDs, packet/archive layouts, combat, physics, pickup
contact and resource authority are unchanged.

Next: bind the original module to seven playable arm rigs and the late-latched
first-person camera, add cosmetic bolt/reload/raise sampling, original audio and
VFX, and qualify visual muzzle alignment, LODs, context/scene rotation and Android
fallback. Visual muzzle sockets never become authority projectile origins.

Rollback: remove the model prewarm/submission binding to restore the retained
original fallback. No protocol migration or saved-data rewrite is required.
