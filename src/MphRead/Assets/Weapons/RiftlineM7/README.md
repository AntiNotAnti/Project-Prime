# Riftline M7 original presentation assets

`model-v1.json` is an original low-poly arm-mounted precision rifle, authored
for Project Prime on 2026-10-20. It contains only independently authored numeric
box geometry, a six-color flat material palette, two LODs, a muzzle socket and
rigid body/bolt/magazine groups. No extracted geometry, textures, animation,
audio samples, logos or third-party design reference is included. The JSON is
its editable source; no external modeller/exporter or dependency is needed.

The +Z forward / +Y up metre-space asset is deliberately separate from any
Hunter/native weapon. The mount is a presentation attachment, never a gameplay
muzzle or collision definition. Palette materials intentionally use flat color;
there are no texture decoders, external references, shaders or GPU asset handles.
The scene-owned immutable decoded copy is loaded before drawing, checked against
a compiled SHA-256, and dropped on visual teardown. The three-box original
fallback remains available if the asset cannot be validated. Headless scenes
never open the resource.

These original asset files are dedicated to the public domain under CC0 1.0
Universal; see LICENSE.txt. This dedication applies only to the original assets
in this directory, not to Nintendo game files or the repository's program code.
CW-06a delivers the bounded model resource and pickup use. Viewmodel/rig/audio/
VFX/animation qualification follows in CW-06; this file does not claim those pass.

`Mods/CustomWeapons/CustomRifleCues.cs` is editable source for four original
procedural shot, bolt, reload and empty signal designs. Integer triangle-wave
chirps, shaped transients and fixed sample-index hash noise generate 22050 Hz,
16-bit mono PCM WAVs of 0.20, 0.12, 0.40 and 0.08 seconds respectively. They use
no extracted samples, third-party audio, gameplay RNG or simulation clock.
The original signal designs and their generated audio are also dedicated to
CC0 1.0 Universal; this does not relicense unrelated program code. Generating
these assets does not by itself establish playback, scene lifetime, animation,
mixing or full CW-06 acceptance.
