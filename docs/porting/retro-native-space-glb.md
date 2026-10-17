# PORT-23: native-space skinned Retro character authoring candidates

This is a private, testable third-person **presentation candidate** for Dark Samus and Space Pirate Trooper variants. It is not a selectable playable Hunter, an animation-authority replacement, an accepted skin, or a package distributed with Project Prime.

PORT-23 extends the native Samus/Weavel authoring-kit JSON with `bindWorldMatrix` (engine-composed bind rest matrix, converted into column-vector matrix rows) for exact native palette joint orientation. The candidate generator refuses nonrigid/sheared/reflected native bind matrices, incorrect palette identities, mismatching bind origins, unresolved source bone mapping, or unsupported source materials.

## Local input

Export private original-source OBJ/CINF/CSKR/ANCS sidecars (PORT-19), generate a local native reference via `-charactermodelkit Samus` or `Weavel` (PORT-22), and author an explicit mapping with `boneMap`, `anchors` and `sourceToNative`. Add **materialMap** mapping each source OBJ `g material_N` identity to a real material name from the native LOD0 biped:

```json
"materialMap": {
  "material_0": "body",
  "material_1": "body_full_bright"
}
```

This is a **format illustration, not an actual recovered Dark Samus material map**. Read the real native kit and source OBJ groups.

## Validate and export

```bash
python3 tools/retro-character-preview/build_native_glb.py \
  --source-root /private/source-character-previews \
  --character darksamus-echoes \
  --native-reference /private/samus-kit/native-reference.json \
  --mapping /private/darksamus.mapping.json \
  --validate

blender -b --python tools/retro-character-preview/build_native_glb.py -- \
  --source-root /private/source-character-previews \
  --character darksamus-echoes \
  --native-reference /private/samus-kit/native-reference.json \
  --mapping /private/darksamus.mapping.json \
  --output /private/darksamus.native-candidate.glb
```

Use `--character trooper-power`, `trooper-ice`, `trooper-wave`, or `trooper-plasma` with a **Weavel** native reference and measured source mapping for those individual source assets.

## What the Blender candidate contains

- Source mesh positions mapped into the explicitly calibrated native bind space; source UV loops and per-triangle material group identities retained.
- Rebuilt mesh triangles with reversed winding when the coordinate mapping reflects handedness.
- A native LOD0 biped armature with exactly the used mapped native joint names and native bind matrices, checked against the source mapping/calibration.
- Aggregated normalized source weights into the native 32-joint Weighted4 palette with 1-4 influences per source vertex.
- Native material names for Project Prime's existing presentation renderer, **without pretending proprietary source textures have been reconstructed**.
- A static skinned GLB candidate; separate source diagnostic motions from PORT-21 remain source-only. Gameplay animation still belongs to the native Samus/Weavel model.

Output is not installed by the tool. Do not copy it into a live `character-models/default` pack until inspected and validated against actual native data. Match-world scale, special transforms, corrected bind orientation, first-person weapon and alternate-form assets, independent character identity/selection and device acceptance are not claimed complete.

Asset-free unit tests exercise source-to-native coordinate conversion, material identity enforcement, 4x4 matrix guards, winding correction, and a headless `--validate` path without Blender or Nintendo files.
