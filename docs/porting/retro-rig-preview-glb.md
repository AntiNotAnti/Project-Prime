# Retro source rigging bridge (PORT-20)

This offline tool assembles locally extracted source-character previews into
**skinned GLB files for visual inspection in Blender or a glTF viewer**.
It does not alter Project Prime's character slots, renderer, weapon emitters,
collision, network, multiplayer authority or replay wire format.

Prerequisite: run the local source command from PORT-19 with your own verified
Prime or Echoes GameCube ISO/GCM, producing OBJ, CINF skeleton, exact CSKR
weight-group and ANCS/ANIM sample JSON.

```sh
python3 tools/retro-character-preview/build_glb.py --validate \
  --root "/path/to/source-character-previews" --character darksamus-echoes

blender -b --python tools/retro-character-preview/build_glb.py -- \
  --root "/path/to/source-character-previews" \
  --character darksamus-echoes
```

Supported IDs: `darksamus-echoes`, `trooper-power`, `trooper-ice`,
`trooper-wave`, `trooper-plasma`.

The tool validates that source OBJ positions match skin-weight indices, bone
IDs and parents resolve, weights are finite, and no unrecognized output
types/symlinks are used. It creates a Blender armature from the source CINF
positions, assigns original CSKR weights and exports a **static skinned GLB**.

Animation is **not** applied to the GLB yet. Sparse motion sidecars are
preserved for the next retargeting slice. Source bone bind conventions,
original lighting/materials, skin deformation, first-person cannon,
alternate form, native gameplay clips and device performance must be
visually verified before production runtime use.

The CI check uses only synthetic data and `--validate` (no Blender,
emulator or Nintendo source assets).
