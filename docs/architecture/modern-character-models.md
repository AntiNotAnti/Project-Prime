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

## Next slice: Samus proof of concept

The next implementation slice should use one authored Samus biped and one
first-person arm cannon to prove the rendering bridge:

1. parse POSITION/NORMAL/TEXCOORD_0 and triangle indices from the resolved GLB;
2. group `rigidNodes` primitives by their mapped source node;
3. upload immutable geometry through the retained renderer/display-list bridge;
4. after the native model has animated, submit each replacement segment using
   the mapped native node's current animation transform;
5. resolve albedo/normal/specular-roughness/emissive channels through the
   existing `ModernTextureAsset` / `TextureAssetManager` policy;
6. keep native biped/viewmodel draw active whenever any replacement resource
   fails validation or GPU upload;
7. benchmark desktop Metal/DX12/Vulkan and Android Vulkan before increasing
   default model detail.

After rigid-node parity is proven, `weighted4` can add JOINTS_0/WEIGHTS_0 and
smooth GPU skinning without changing the asset identity or fallback contract.
