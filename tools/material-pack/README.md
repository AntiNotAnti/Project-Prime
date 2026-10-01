# Material pack authoring

The headless commands require no extracted game assets:

```
ProjectPrime -materials inspect texture-packs/default
ProjectPrime -materials validate texture-packs/default
ProjectPrime -materials inventory inventory.json
ProjectPrime -materials starter inventory.json texture-packs/MyPack
```

The runtime currently loads `texture-packs/default` beside the desktop application, or beneath Android’s initialized writable launcher directory. Other folders can be authored/validated and selected by placing their contents there. Existing five legacy filename variants remain supported when a manifest entry is absent. An explicit empty entry uses the original game texture. Missing/corrupt optional maps produce warnings and disable only that channel; unsafe references, duplicate identities and unknown formats reject the manifest. Invalid manifests are ignored at runtime while the safe legacy filename path remains available; validation still reports the error. PNG is the supported image format.

Inventory exports bounded observations retained during scene texture uploads (or native material inspection), saved when a scene closes normally to the user-local `ProjectPrime/material-inventory.json`. It contains keys, original dimensions and model names, never image content or guessed usage. It is a record of models observed in the process that last saved it, not a complete cartridge or map catalog. If no inventory exists the command reports that fact. Starter generation refuses to overwrite an existing manifest and leaves every assignment empty.

Stable keys use actual loader metadata:

- `model/<encoded-name>/texture/<id>/palette/<id>/recolor/<id>` for ordinary models.
- `room/<encoded-RoomMetadata.Name>/texture/...` for native rooms and legacy custom rooms without a MapId.
- `map/<existing-MapId-N-format>/texture/...` for community/generated rooms with a persistent MapId.
- `effect/model/<encoded-particle-model-name>/texture/...` for the actual shared model loaded by particle definitions. This intentionally identifies a shared asset, not individual effect instances that share its texture binding.

Names are invariant lowercase, and punctuation is encoded without collisions. Scene model copies retain their presentation identity. Runtime metadata carries the existing community MapId without changing serialized map definitions, compiled outputs, or content/package hash rules. Texture/palette slots are the actual observed slots in that asset version. Room/community scoped entries win over generic model entries. Shared effect/model aliases follow an explicit generic model entry, and are resolved regardless of effect-load order so reuse of an existing texture binding stays deterministic. Actual particle loading records the effect alias in inventory. Older model manifests and all five legacy filename candidates remain fallback paths.

Map Studio's material browser exposes native source keys, Browse/Clear controls, and downsampled channel images with channel semantics. Selected images are validated and copied into the local default pack under content-addressed names; manifest writes are atomic. Edited thumbnails are disposed promptly. These are local presentation pack changes, separate from map document undo/save and community packages. The viewport feeds normal/specular/roughness/emissive maps into the same material shader as gameplay, under fixed studio lighting. It computes face normals and preserves the original UV coordinate dimensions when albedo resolution changes. Material edits refresh textures and release old companion textures while retaining mesh geometry.

Generated maps can remap source texture slots during compilation. Their runtime keys are obtainable through observed inventory; the editor does not guess a source-to-generated slot mapping. Portable authored `.tex` channel assignments and per-effect-instance overrides require an explicit compiler provenance / binding contract and are not implemented here. Packs remain local presentation overrides rather than silently changing community package identities.

Rendering uses one resolved map contract before the existing `GraphicsApi` backend dispatch: normal XYZ, specular in red and roughness in green, emissive RGB. Android uses the same PNG header/chunk/dimension validation, the OS BitmapFactory decoder (unscaled, unpremultiplied), and explicit ARGB-to-RGBA conversion before the shared upload path. It uses the writable pack root and does not load the desktop-only Stb native dependency; physical Android acceptance remains pending. Pack reload changes the renderer material revision and retains main texture bindings while replacing their companion resources. Partial companion allocation failures release all earlier allocations.

Cross-API PBR appearance equivalence still requires rendered reference comparisons and hardware acceptance. The synthetic OpenGL check below proves resource lifetime and shared shader activation, not identical pixels across APIs.

Run synthetic content-free checks:

```
dotnet run --project tools/material-pack/material-pack.csproj -c Release
# Optional real OpenGL context; no cartridge data required:
dotnet run --project tools/material-pack/material-pack.csproj -c Release -- --gpu
```

The GPU check runs 12 success/failure upload cycles plus a partial-allocation fault (38 allocated / 38 released texture handles), and four lit editor refreshes. It asserts all material shader channels are enabled, the editor scope restores, only one mesh is uploaded, old map textures retire, and scene teardown releases the final set.

Limits: 2 MiB manifest, 8,192 materials, 32 MiB per image, 8,192 maximum dimension, 16 million pixels per image, 256 MiB pack, 32,768 filesystem entries. PNG chunk checksums and full decode are validated. Absolute/traversal/backslash paths and linked pack assets are rejected. The local pack should not be mutated concurrently while validation or upload is in progress.
