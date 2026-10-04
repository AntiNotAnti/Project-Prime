# Modern character models

Project Prime's modern character path is a presentation layer over the native
Metroid Prime Hunters player model. It does **not** replace the cartridge model
object that owns animation timing, gameplay dimensions, collision, weapon
simulation, networking or replay state.

The migration is intentionally staged so every slice can fall back to the native
model without changing a match.

## Slice 1: safe asset and retarget contract

Local model packs live under:

```
<user data>/character-models/default/
  characters.json
  samus/
    biped.glb
    viewmodel.glb
```

`characters.json` format 1 identifies a hunter and presentation part, the GLB
file, a skinning mode, and an explicit map from source glTF node/joint names to
native MPH node names.

Example:

```json
{
  "format": 1,
  "id": "my-hd-hunters",
  "models": [
    {
      "hunter": "Samus",
      "part": "biped",
      "model": "samus/biped.glb",
      "skinning": "rigidNodes",
      "boneMap": {
        "Body": "Body"
      }
    }
  ]
}
```

Supported presentation parts are `biped`, `viewModel`, `alternateForm` and
`halfturret`.

Two skinning contracts are reserved:

- `rigidNodes`: the first renderer slice. Imported segments are attached to
  one already-animated native node transform. This can substantially increase
  silhouette/mesh detail without changing native animation.
- `weighted4`: the follow-up smooth-skin path. Up to four influences per
  vertex will consume a bounded native pose palette. The current contract caps
  the mapped weighted rig at the generated World's 32-matrix limit.

Slice 1 validates and resolves assets but intentionally does not draw them yet.
That makes it safe to land the file/retarget contract before adding new GPU
geometry submission.

### Safety and limits

Character model packs are local presentation assets. They are not sent through
lobby packets, match state or replay files.

The loader rejects:

- absolute, parent-traversal, backslash or drive-qualified asset paths;
- symbolic links/reparse points inside the pack;
- manifests over 1 MiB or more than 64 model entries;
- models over 128 MiB;
- non-GLB input or GLB files that are not glTF 2.x;
- GLB JSON chunks over 4 MiB;
- duplicate JSON properties and duplicate named GLB nodes;
- more than 4096 source nodes or 4096 mesh primitives;
- missing source nodes named by the retarget map;
- `weighted4` declarations on GLBs that contain no skin;
- weighted native retarget sets larger than 32 nodes.

At runtime, `CharacterModelPack.LoadDefault` converts any optional-pack IO,
JSON or validation failure into an empty catalog plus an issue string. The
native MPH model therefore remains the guaranteed fallback.

Run the content-free contract check with:

```sh
ProjectPrime -charactermodelcheck
```

The check creates a synthetic GLB/manifest in a temporary directory and verifies
successful rigid-node discovery plus rejection of path traversal, missing source
nodes and false weighted-skin declarations. It needs no extracted game data.

## Slice 2: rigid-node retained rendering bridge

The first replacement renderer is now implemented for biped and first-person
viewmodel geometry.

The GLB reader accepts core triangle primitives with:

- FLOAT `POSITION` VEC3;
- optional FLOAT `NORMAL` VEC3 (missing normals are generated);
- optional FLOAT `TEXCOORD_0` VEC2;
- unsigned byte/ushort/uint indices, or a non-indexed triangle stream;
- interleaved buffer-view strides.

Rigid replacements reject skins, JOINTS/WEIGHTS, morph targets, sparse accessors,
external buffers and non-triangle primitives. The current total budget is
500,000 vertices and 1,500,000 indices per replacement model.

Each mapped source node may own a mesh. Its vertex positions are interpreted in
that source node's local space; the source node's authored transform is replaced
at draw time by the mapped native MPH node's already-animated transform. That is
the key compatibility boundary: Project Prime continues to run the original
hunter animation system and the HD geometry follows the resulting pose.

GLB primitive material names map to the existing native `Material.Name`.
An unnamed primitive inherits the first native material attached to its mapped
node. This reuses native material bindings and therefore automatically reuses
the existing HD material-pack path for albedo, normal, specular/roughness and
emissive companions. No second character-only texture system is introduced.

Compiled replacement primitives use the existing display-list bridge.
`EndList` therefore promotes immutable geometry into persistent native buffers
on the modern retained renderer, while OpenGL keeps its compatibility display
list. Handles are scene-owned and released from `Scene.UnloadGl`.

Player drawing is atomic per presentation part:

1. native animation/material state is updated exactly as before;
2. Project Prime asks for a validated compiled biped or viewmodel replacement;
3. every replacement segment submits with its mapped native node transform and
   native material identity;
4. if discovery, rig validation, parsing, material mapping or GPU compilation
   fails, the original `GetDrawItems` call runs unchanged.

Bright skins, palette overrides, player outlines, cosmetic material overrides,
double-damage binding overrides and first-person viewmodel projection all pass
through the same `Scene.AddRenderItem` path.

The feature is opt-in through **HD character models** and defaults off.
Installing files alone cannot alter the rendered hunter.

The content-free `-charactermodelcheck` now also builds a real synthetic GLB
with POSITION/NORMAL/TEXCOORD_0/index buffer views and verifies the decoded
triangle exactly.

## Next slice: authored Samus acceptance + LOD

The code path is ready for a real Samus biped/arm-cannon authoring test. The next
slice should:

1. export a segmented Samus proof asset aligned to native MPH node-local spaces;
2. use native material names in the GLB so existing 4K/PBR packs attach directly;
3. add screen-space/distance LOD selection for replacement geometry;
4. verify muzzle/effect attachment alignment, death/unmorph transitions, bright
   skins, outlines and team recolors;
5. benchmark Metal, desktop Vulkan/DX12 and physical Android Vulkan;
6. only after rigid parity is accepted, implement `weighted4`
   JOINTS_0/WEIGHTS_0 smooth GPU skinning.

Weighted skinning remains an additive renderer tier; it does not change the
asset identity or native fallback contract.
