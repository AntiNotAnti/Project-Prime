# Enhanced Material Authoring

Project Prime ships a bounded, deterministic inspection tool for optional enhancement packs. The tool is presentation-only: it reads `materials.json` and local PNG metadata through the same fail-soft loader used by the enhanced renderer, and never changes simulation or installed content.

## Inspect a pack

```bash
dotnet run --project src/EnhancedMaterials.Tool/EnhancedMaterials.Tool.csproj -c Release -- \
  inspect enhancements/default \
  --inventory material-inventory.json \
  --output material-report.json
```

Omit `--inventory` when model and room usage is not available, and omit `--output` to write JSON to standard output. A clean pack exits `0`. A readable pack with validation issues writes the complete report and exits `3`. Malformed command or inventory input exits `1` with a concise error.

Reports are sorted by canonical `TextureAssetKey` and include original albedo metadata, replacement albedo/normal/emissive presence and dimensions, material values, and sorted model/room usage. Pack issues are included in deterministic order.

The optional inventory format is:

```json
{
  "format": 1,
  "textures": [
    {
      "key": "model/samus/texture/3/palette/0/recolor/0",
      "originalAlbedo": {
        "path": "models/samus/texture/3",
        "width": 64,
        "height": 64
      },
      "models": ["Samus"],
      "rooms": ["Celestial Archives"]
    }
  ]
}
```

The inventory is limited to 1 MiB and 4096 textures. Each texture may list at most 256 models and 256 rooms. Dimensions and stable keys are validated before a report is produced.

Effect particles use `effect/<effect-name>/texture/<current-texture-id>` for both model-node and billboard draws. The effect name comes from the parsed effect definition; keys are canonicalized to lowercase. Standalone particles without an effect owner use their particle model key. Hunter meshes use model keys with the active texture, palette, and recolor; temporary binding overrides retain their original appearance.

## Generate a starter manifest

Create a JSON array containing stable texture keys:

```json
[
  "room/archives/texture/2/palette/0",
  "model/samus/texture/3/palette/0/recolor/0"
]
```

Then run:

```bash
dotnet run --project src/EnhancedMaterials.Tool/EnhancedMaterials.Tool.csproj -c Release -- \
  starter texture-keys.json \
  --output enhancements/default/materials.json
```

An inventory document may be used instead of the string array. Keys are validated, canonicalized, deduplicated, and sorted. The starter contains no guessed replacement paths, so generating it cannot make an otherwise valid original asset depend on missing HD content.
