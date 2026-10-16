# PORT-22: native rig calibration contract (offline)

Dark Samus from Echoes and the four Prime 1 Space Pirate Trooper appearances remain **private authoring previews**, not playable network Hunters.

The existing Project Prime native character-kit exporter now records a composed `bindWorldPosition` for each model node. Unlike the node-local Position alone, this value comes from the engine's own `Model.ComputeNodeMatrices(0)` for the authoring reference.

This slice introduces `tools/retro-character-preview/retarget_contract.py`. It reads the verified, user-owned source OBJ/CINF/CSKR sidecars from PORT-19 and the native `native-reference.json` from `-charactermodelkit`. A human-authored bone map and coordinate calibration is **required**. It does not guess bone identities or generate a runtime Hunter.

## Local workflow

1. Run the existing private Retro character exporter against your verified owned Prime/Echoes disc.
2. Generate the native reference from installed MPH media: `ProjectPrime -charactermodelkit Samus -output /private/samus-kit` for Dark Samus, or `Weavel` for Trooper appearances.
3. Author an exact mapping JSON for the relevant source character and measured native reference.
4. Run the offline preflight:

```bash
python3 tools/retro-character-preview/retarget_contract.py \
  --source-root /private/source-character-previews \
  --character darksamus-echoes \
  --native-reference /private/samus-kit/native-reference.json \
  --mapping /private/darksamus.mapping.json \
  --output /private/darksamus.retarget-diagnostic.json
```

Format 1 input example **contains placeholder bone IDs and coordinates**. Do not assume these names/axes match either real game:

```json
{
  "format": 1,
  "sourceCharacter": "darksamus-echoes",
  "nativeHunter": "Samus",
  "nativePart": "biped",
  "sourceToNative": {
    "axis": [[1, 0, 0], [0, 1, 0], [0, 0, 1]],
    "scale": 1.0,
    "offset": [0, 0, 0]
  },
  "boneMap": {"10": "Pelvis", "11": "L_shoulder", "12": "R_shoulder"},
  "anchors": [{"sourceBoneId": 10}, {"sourceBoneId": 11}, {"sourceBoneId": 12}],
  "maxAnchorError": 0.01
}
```

## Validation boundaries

The offline validator checks source identity (Dark Samus maps only to Samus; Troopers only to Weavel), CINF hierarchy and bone uniqueness, native LOD0 biped palette membership, 1-4 weighted influences per source vertex, normalized weight groups, 32-joint native palette budget, coordinate basis invertibility, three or more noncollinear anchors, and bounded maximum anchor error. It merges source weights when multiple bones map to one native node, and explicitly marks any axis reflection so a later exporter can correct triangle winding.

The private output is a **retarget diagnostic**, not an installable character pack. No copyrighted input assets are committed or transmitted. A passing numerical calibration is not visual evidence, source animation equivalence, first-person weapon behavior, morph mode, collision equivalence, or physical-device performance. All those remain release/acceptance gates.
