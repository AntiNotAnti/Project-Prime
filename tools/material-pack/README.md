# Material pack authoring

The headless commands require no extracted game assets:

```
ProjectPrime -materials inspect texture-packs/default
ProjectPrime -materials validate texture-packs/default
ProjectPrime -materials inventory inventory.json
ProjectPrime -materials starter inventory.json texture-packs/MyPack
```

The runtime currently loads `texture-packs/default` beside the application. Other folders can be authored/validated and selected by placing their contents there. Existing five legacy filename variants remain supported when a manifest entry is absent. An explicit empty entry uses the original game texture. Missing/corrupt optional maps produce warnings and disable only that channel; unsafe references, duplicate identities and unknown formats reject the manifest. Invalid manifests disable pack replacement until repaired/reloaded. PNG is the supported image format.

Inventory exports bounded observations retained during scene texture uploads (or native material inspection), saved when a scene closes normally to the user-local `ProjectPrime/material-inventory.json`. It contains keys, original dimensions and model names, never image content or guessed usage. It is a record of models observed in the process that last saved it, not a complete cartridge or map catalog. If no inventory exists the command reports that fact. Starter generation refuses to overwrite an existing manifest and leaves every assignment empty.

Map Studio's material browser exposes stable model keys for native source materials, Browse/Clear controls and channel image previews. Selected images are validated and copied into the local default pack under content-addressed names; writes to the manifest are atomic. Edits are local presentation pack changes, separate from map document undo/save and community packages. Albedo previews refresh the material table without rebuilding geometry. Normal/specular/emissive previews display channel images, not a fully lit PBR viewport. Runtime model overrides apply to that model identity; generated custom-map model names can differ from their source model. Custom `.tex`, room/effect aliases and portable community material identities are not implemented by this slice.

Rendering uses one resolved map contract before the existing `GraphicsApi` backend dispatch: normal XYZ, specular in red and roughness in green, emissive RGB. Existing Android replacement disablement remains pending device/decode acceptance. Pack reload changes the renderer material revision and retains main texture bindings while replacing their companion resources. Cross-API PBR visual equivalence still requires rendered hardware acceptance; schema checks are not that evidence.

Run synthetic content-free checks:

```
dotnet run --project tools/material-pack/material-pack.csproj -c Release
```

Limits: 2 MiB manifest, 8,192 materials, 32 MiB per image, 8,192 maximum dimension, 16 million pixels per image, 256 MiB pack, 32,768 filesystem entries. PNG chunk checksums and full decode are validated. Absolute/traversal/backslash paths and linked pack assets are rejected. The local pack should not be mutated concurrently while validation or upload is in progress.
