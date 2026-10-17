# PORT-24: private third-person visual test pack and acceptance receipt

This slice makes it possible to hand a completed PORT-23 native-space GLB candidate to Project Prime's **existing, opt-in HD character rendering system** for *local testing*. It does **not** register Dark Samus or Space Pirate Troopers as new playable Hunters.

PORT-24's `tools/retro-character-preview/local_visual_pack.py` performs a bounded asset-free GLB contract check and writes a self-contained **staging folder** with one `characters.json` biped-only Weighted4 entry and `validation-receipt.json`. It never installs or enables assets in the actual game directory.

Dark Samus is shown using **Samus's existing simulation/animation** when manually tested; Trooper appearances use **Weavel's**. The original guns, alt forms, gameplay, hitboxes, networking and replay state remain native. No source game files are shipped in the repository.

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

## Manual runtime test

1. Run `ProjectPrime -charactermodelvalidate /private/darksamus-visual-test-pack` with your installed original game data. A nonzero exit requires correcting the candidate. This is the existing native runtime model validator, not a new bypass.
2. Back up any installed character model pack and settings. **Only after this passes**, manually install the accepted test pack in your user-local `character-models/default` directory and enable the existing `HD character models` setting. Do not distribute its original game source GLB.
3. In a private offline game, select **Samus** to visually inspect the candidate Dark Samus body, or **Weavel** for Trooper variants, and exercise idle, turning, locomotion, fire, damage/death and respawn. The preview substitutes biped presentation only, not the first-person gun or alt form.
4. Revert the test pack and setting when complete. No default mod, production asset, new Hunter identity or multiplayer content is shipped by this tool.

Record visual defects and fps/device performance separately. The generated receipt explicitly marks `approvedForGameplay=false`, `multiplayerEnabled=false`, `firstPersonVerified=false`, `alternateFormVerified=false`, `sourceMotionRetargetVerified=false`, and `devicePerformanceVerified=false`. Its SHA256s allow local file comparisons without uploading asset bytes.

## Remaining release gates

Source-native joint orientation and inverse binds must be reviewed under actual gameplay animations; native kit output must be generated from actual installed game media. First-person weapons/muzzle/VFX, alternate forms, unique playable character identities and selector/network/replay compatibility, all animation-state coverage, collision parity, Android/Desktop performance, and Echoes map real-media acceptance are **not complete**. This PR creates a narrowly scoped **manual presentation-test path only**. Synthetic CI does not prove any device acceptance.
