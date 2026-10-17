# PORT-21: private source-motion animated GLB diagnostic preview

This slice adds a bounded animation-bake option to the PORT-20 offline skinned-GLB authoring tool. It does not install or enable Dark Samus or Space Pirate Troopers as Hunters. All seven standard Hunters, gameplay, hitboxes, first-person emitters, spectator/replay logic, and network packets are unchanged.

Run PORT-19's private exporter with a verified, user-owned Prime or Echoes source. It creates character OBJ, skeleton, weights, and motions sidecars in the local authoring directory. Then:

    # Headless structural validation (no Blender required):
    python3 tools/retro-character-preview/build_glb.py --root /local/previews --character darksamus-echoes --animate-source --validate

    # Blender 4.x: skinned GLB with separate diagnostic source clips:
    blender -b --python tools/retro-character-preview/build_glb.py -- --root /local/previews --character darksamus-echoes --animate-source

    # Inspect one named, sampled primitive clip:
    blender -b --python tools/retro-character-preview/build_glb.py -- --root /local/previews --character trooper-power --animate-source --clip Idle

Without --animate-source, PORT-20's default remains a **static** source-rig GLB. The animated output is written to a separate source-motion.preview.glb suffix. Supported IDs: darksamus-echoes, trooper-power, trooper-ice, trooper-wave, trooper-plasma.

## Meaning of baked animation

These sparse ANIM channels are not calibrated gameplay poses. Each clip has its own Blender NLA track and retains its original source ANIM ID in its name. Quaternion order converts source [x,y,z,w] into Blender [w,x,y,z] and enforces sign continuity. Rotations and translations are *deltas from the first sampled source pose*, making the CINF rig neutral at the start. Five source sample timestamps are keyed at 60 FPS.

These are only sparse diagnostic previews, **not** reconstructed full-resolution ANIM key streams. Source bind axes, parent-space translations, compound ANCS blends and root-motion basis are not accepted. The validator refuses unknown/duplicate IDs, malformed timestamps, changing channels, nonfinite or non-unit quaternions, excessive clip/bone budgets and ambiguous requests. Composite/unavailable/unsampled clips are omitted rather than fabricated. The --clip argument requires --animate-source.

## Remaining acceptance

1. Visually inspect owned Echoes Dark Samus and all four Prime Trooper variants in Blender; validate CINF bind axes, skin deformation, and the approximate motion. No actual disc or Blender media has been tested in CI.
2. Export native Samus/Weavel reference rigs with -charactermodelkit; calibrate a deliberate source-to-native mapping with the existing 32-joint Weighted4 runtime palette. Do **not** install this source preview as a normal Hunter pack.
3. Author and validate distinct first-person gun/viewmodel, muzzle/VFX, alternate form, combat/state clips, LOD and materials before any gameplay wiring. The original native Model must remain simulation-authoritative.
4. Run physical device, multiplayer/replay, actual map/media and performance acceptance. The Echoes arenas retain their separate source-media/device acceptance gates.

CI runs the synthetic motion-bake tests without Blender or Nintendo content; no licensed game files or exported geometry leave the local authoring environment.
