# PORT-24: private third-person visual test pack and acceptance receipt

This slice hands a completed PORT-23 native-space GLB candidate to Project Prime's **existing model-pack validator** for private preflight. CPP-08 adds explicit catalog appearance binding inside that owner. It does **not** register Dark Samus or Space Pirate Troopers as new playable Hunters.

PORT-24's `tools/retro-character-preview/local_visual_pack.py` performs a bounded asset-free GLB contract check and writes a self-contained **staging folder** with one `characters.json` biped-only Weighted4 entry and `validation-receipt.json`. It never installs or enables assets in the actual game directory.

Dark Samus candidates target **Samus's existing biped rig/animation**; Trooper appearances target **Weavel's**. This intermediate native-rig candidate does not satisfy the source-native rig/clip contract. No source game files are shipped in the repository.

## Usage after local game-media authoring and Blender export

```bash
python3 tools/retro-character-preview/local_visual_pack.py \
  --source-root /private/source-character-previews \
  --character darksamus-echoes \
  --native-reference /private/samus-kit/native-reference.json \
  --mapping /private/darksamus.mapping.json \
  --candidate-glb /private/darksamus.native-candidate.glb \
  --output-root /private/darksamus-visual-test-pack
```

This creates a **new** output folder. It refuses existing paths, symlinks, and the default live model-pack destination. It verifies the source identity and calibration again, then inspects the entire bounded GLB header/JSON chunk, exactly matching native joint names, inverse-bind matrices, Weighted4 attributes, native material names, skinned mesh transform, and embedded glTF buffer.

## Private acceptance boundary

1. Run `ProjectPrime -charactermodelvalidate /private/darksamus-visual-test-pack` with your installed original game data. A nonzero exit requires correcting the candidate. This is the existing native runtime model validator, not a new bypass.
2. Keep this identity-keyed pack in its dedicated staging directory. Its entries do not resolve through ordinary Hunter-only lookups, even if copied into the default pack. The game has no new public selector or per-seat import selection UI in this slice.
3. Private runtime callers must explicitly pass the pack, per-seat `PrivateCharacterAppearance`, and preview capability. Live network, replica and replay rendering refuse this input. An unavailable lookup returns false so the caller retains native presentation.
4. Use the existing `-charactermodelcheck` for content-free identity/receipt/cache regression checks. These generated triangles do not prove real-media rendering or device acceptance. Actual offline visual inspection still needs a separately integrated private scene consumer and owned source media.

New entries bind `characterId`, `skinSource`, `hunter`, `provenance` and
`provenanceSha256` together. The contained receipt repeats canonical source and
catalog identities, witnesses source inputs and hashes the actual GLB. All five
supported PORT variants are accepted; reserved `MP3DarkSamus` is unavailable.
These local hashes witness consistency, not ownership or source-game correctness.
Legacy format-1 entries without the four new private fields retain their old
Hunter-only rendering behavior. They cannot establish independent extra-character
identity acceptance.

Record visual defects and fps/device performance separately. The generated receipt explicitly marks `approvedForGameplay=false`, `multiplayerEnabled=false`, `firstPersonVerified=false`, `alternateFormVerified=false`, `sourceMotionRetargetVerified=false`, and `devicePerformanceVerified=false`. Its SHA256s allow local file comparisons without uploading asset bytes.

## Remaining release gates

Source-native joint orientation and inverse binds must be reviewed under actual gameplay animations; native kit output must be generated from actual installed game media. First-person weapons/muzzle/VFX, alternate forms, unique playable character identities and selector/network/replay compatibility, all animation-state coverage, collision parity, Android/Desktop performance, and Echoes map real-media acceptance are **not complete**. This is a private presentation contract and preflight path. Synthetic CI does not prove any device acceptance.
