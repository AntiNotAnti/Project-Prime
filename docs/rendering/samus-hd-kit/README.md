# Samus HD character authoring kit

Generated from the currently configured extracted game data.

## Reference exports

- `reference/Samus_lod0/` contains the native near biped DAE, textures and Blender import script.
- `reference/Samus_lod1/` contains the native distant biped reference.
- `reference/SamusGun/` contains the first-person model reference.
- `reference/SamusAlt_lod0/` contains the alternate-form reference.
- `native-reference.json` is the exact node, parent, matrix-palette and material inventory.

The generated Blender scripts already reconstruct the native armature and assign
vertices to native node groups from the MPH matrix IDs. Use those exports as the
proportion/pose reference.

The kit also contains `prepare-biped-rigid.py`,
`prepare-biped-lod1-rigid.py`, `prepare-viewmodel-rigid.py`, and
`prepare-altform-rigid.py`. After the corresponding native
Blender import is loaded and your upgraded mesh is bound to
those native rigid groups, run the helper. It evaluates the current armature
pose, converts every segment back into native bone-local coordinates, preserves
UV/material identity, rejects soft/missing weights and cross-bone triangles, and
exports directly to the expected starter GLB path.

Apply subdivision/remesh/topology-changing modifiers before running the helper.
A live topology-changing modifier is rejected so vertex-group identity cannot
silently drift.

## Rigid replacement contract

The shipping GLBs must be segmented, not skinned:

1. Keep source mesh node names equal to the native node names in
   `starter/characters.json`.
2. Each segment's vertices must be authored in that native node's local space.
3. Do not export Armature modifiers, JOINTS_0/WEIGHTS_0, morph targets or external buffers.
4. Use native material names from `native-reference.json`. This makes Project Prime
   reuse the existing hunter texture/PBR replacement bindings.
5. Export biped as `starter/samus/biped.glb`.
6. Export the arm cannon as `starter/samus/viewmodel.glb`.
7. Optional: author `starter/samus/biped_lod1.glb` and append
   `starter/biped-lod1-entry.json` to the `models` array.
8. Optional: author `starter/samus/altform.glb` and append
   `starter/alternate-form-entry.json`.
9. Validate before installing:
   `ProjectPrime -charactermodelvalidate "starter"`.
10. Copy the completed starter contents to `character-models/default` and enable
   **HD character models** in Graphics.

The biped starter maps 17 native matrix nodes.
The viewmodel starter maps 20 native matrix nodes.

## Weighted4 smooth-skin alternative

For smooth shoulders, elbows and other joints, use the matching
`prepare-*-weighted4.py` helper instead of the rigid helper. Weighted exporters
keep the Armature modifier and standard glTF skin, enforce at most four non-zero
native-bone influences per vertex, temporarily mark only the native 32-joint
palette as deform bones, and preserve native material names.

The starter folder contains matching `*-weighted4-entry.json` snippets. A rigid
and Weighted4 entry for the same hunter/part/LOD are alternatives: include
**one**, never both, because that presentation identity may resolve to only one
replacement asset.

Weighted4 GLBs use the same local-only pack, material/PBR bindings, fallback,
LOD identity and validation command as rigid replacements. Run
`ProjectPrime -charactermodelvalidate "starter"` after substituting the desired
weighted entry into `characters.json`.
## SourceIO Samus proof

Run Blender in background with `retarget-samus-source.py` to recreate the Samus A
body and armed bodygroup replacement. The converter fits the native animation
frames, bakes those frames into the rest skeleton, preserves the original UVs
and embedded albedos, and checks the exported inverse binds numerically.

The locally generated candidate is `starter/samus/biped_sourceio_weighted4.glb`; copy it to
`starter/samus/biped_weighted4.glb`, validate the starter pack, and install it.
Generated `.blend`/`.glb` outputs are intentionally Git-ignored and must not be
committed because they contain user-extracted game content.
The fixed export has 17 joints, 8 primitives and 39,000 triangles.

Use the rebuilt game: its Weighted4 loader now reads embedded albedos and the
regenerated Metal/Vulkan/DX12 shaders support the packed four-joint palette.
Old builds can show black patches because they interpret weights as colors.

`retarget-samus-source.buggy-backup.py` and the installed
`biped_distorted_backup.glb` retain the previous conversion for diagnosis.
