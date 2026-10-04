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
2. Project Prime asks for the validated replacement for that presentation part;
3. every replacement segment submits with its mapped native node transform and
   native material identity;
4. if discovery, rig validation, parsing, material mapping or GPU compilation
   fails, the original `GetDrawItems` call runs unchanged.

Bright skins, palette overrides, player outlines, cosmetic material overrides,
double-damage binding/texgen overrides and first-person viewmodel projection all
pass through the same `Scene.AddRenderItem` path.

The feature is opt-in through **HD character models** and defaults off.
Installing files alone cannot alter the rendered hunter.

The content-free `-charactermodelcheck` now also builds a real synthetic GLB
with POSITION/NORMAL/TEXCOORD_0/index buffer views and verifies the decoded
triangle exactly.

## Slice 3: native authoring kit and acceptance validator

A game-data-backed authoring command now turns the exact installed hunter assets
into a working art reference instead of asking an artist to infer node/material
names from code.

Generate Samus:

```sh
ProjectPrime -charactermodelkit Samus -output samus-hd-kit
```

The kit contains:

- `reference/Samus_lod0/`: native biped DAE, decoded recolor textures and the
  existing Blender helper that reconstructs the MPH armature and rigid vertex groups;
- `reference/SamusGun/`: the same for the first-person arm cannon;
- `reference/SamusAlt_lod0/`: alternate-form reference;
- `native-reference.json`: exact model scale, node hierarchy, transforms,
  matrix-palette slots, mesh ownership and native material names;
- `starter/characters.json`: an identity retarget map generated from the
  native matrix palette for biped and viewmodel;
- `README.md`: the rigid export contract and install/validation steps.

The starter directory deliberately does not contain fake GLBs. It becomes a
valid pack only after authored `biped.glb` and `viewmodel.glb` files are
placed in its hunter folder.

Biped entries may optionally add `"lod": 1`. Missing `lod` is LOD0 for
backward compatibility. Project Prime follows the same near/distant LOD choice
already made by `PlayerDraw`: it asks for matching HD LOD0 or LOD1 and falls
back to the matching native biped tier if that replacement is absent. Other
presentation parts currently accept LOD0 only.

The authoring kit exports the native distant biped too and writes
`starter/biped-lod1-entry.json`, an exact optional LOD1 mapping that can be
appended to the starter manifest after `biped_lod1.glb` is authored.

It also writes optional `alternate-form-entry.json` plus
`prepare-altform-rigid.py`. Weavel kits additionally include the native
`WeavelAlt_Turret_lod0` reference, `halfturret-entry.json`, and
`prepare-halfturret-rigid.py`. These optional entries are not added to the
starter's live `models` array until the corresponding GLB actually exists.

## Slice 4: complete rigid presentation targets

The runtime replacement path now covers:

- biped LOD0 and optional LOD1;
- first-person viewmodel;
- alternate forms, including Kanden's segmented pose and Spire's attack pose;
- Weavel's halfturret.

Alternate-form submission preserves its dedicated cosmetic skin context.
Halfturret submission preserves owner bright skins, outline color, freeze/damage
suppression, cosmetic turret material context, and double-damage emission,
binding and generated-coordinate matrix. The launcher hunter preview uses HD
geometry for biped, viewmodel and alternate-form modes when the feature is
enabled, so authored assets can be visually inspected without entering a match.

Validate a completed pack against the currently installed native game data:

```sh
ProjectPrime -charactermodelvalidate samus-hd-kit/starter
```

Validation reuses the production GLB reader and additionally checks the mapped
native rig and native material names. It prints primitive/vertex/triangle totals
for every resolved replacement and exits non-zero before installation if a
contract mismatch exists.

This is intentionally built on the pre-existing Collada/Blender export path:
that exporter already decodes MPH `MTX_RESTORE` matrix IDs, creates native
bones, and assigns every source vertex to the corresponding native node group.
The authoring kit therefore describes the same skeleton the game actually
animates.

## Slice 5: Weighted4 smooth skinning

`weighted4` is now a real additive renderer tier rather than a reserved
manifest value.

The first implementation accepts a standard glTF 2.0 skin with one shared skin
per replacement, 1-32 named joints, `JOINTS_0` and `WEIGHTS_0` VEC4
attributes, optional inverse-bind matrices, and identity transforms on skinned
mesh nodes. Joint names are retargeted through the same `boneMap` to native MPH
nodes; Project Prime still runs the original animation and uses those animated
native transforms as the per-frame pose source.

For joint `i`, the uploaded skin matrix is the glTF inverse-bind correction
composed with the mapped native node's current animation transform. The shader
normalizes the four authored weights and blends up to four of those matrices for
position and normal transformation.

No retained vertex-format migration was required. The legacy persistent geometry
bridge already stores 15 floats per vertex. Weighted character geometry
reinterprets two existing attributes only when the render item explicitly sets
`WeightedSkinning`:

- vertex color RGBA carries the four normalized weights;
- texcoord Z carries four packed 5-bit joint indices.

The four indices consume 20 bits total, so their packed integer is exactly
representable by IEEE-754 float. Legacy cartridge geometry never sets the flag
and continues to interpret texcoord Z as its original single matrix-stack index.

Forward rendering and deferred PBR both implement the same weighted matrix
blend. Weighted packets deliberately stay out of the direct-retained WebGPU
packet fast paths in this parity-first slice; they still use immutable retained
geometry, but per-item state travels through the compatibility submission path
until visual/backend parity is accepted.

The authoring kit now generates `prepare-*-weighted4.py` exporters and matching
`*-weighted4-entry.json` snippets. The exporter validates native materials,
1-4 non-zero mapped bone influences per vertex, a maximum 32-joint palette, and
identity mesh/armature alignment before asking Blender's glTF exporter to write
the skin. Rigid and Weighted4 entries for the same hunter/part/LOD are
alternatives, never duplicates in the same manifest.

The content-free `-charactermodelcheck` includes a real two-joint skinned GLB
with normalized weights and inverse-bind matrices, and the standalone
**HD character model contract** CI job runs it independently of networking/map
regressions.

## Next slice: authored Samus acceptance and weighted parity

The engine/tooling path is now complete enough that the remaining proof needs
real authored assets and visual/performance acceptance:

1. generate the Samus kit from extracted game data;
2. author a higher-detail biped and arm cannon, using rigid or Weighted4 per
   asset as appropriate;
3. validate native material names, muzzle/effect alignment, idle/combat/death
   poses and LOD0/LOD1 transitions;
4. verify bright skins, team recolors, double-damage texgen, alt transitions and
   launcher preview presentation;
5. benchmark Metal, desktop Vulkan/DX12 and physical Android Vulkan;
6. after parity is accepted, promote Weighted4 into the direct-retained packet
   fast paths and tune final desktop/Android triangle budgets.
