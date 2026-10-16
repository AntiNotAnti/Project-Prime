# Extra character source preview exporter

This slice converts **locally owned** Metroid Prime 1 and Metroid Prime 2:
Echoes disc sources into artist-facing OBJ mesh previews and CINF skeleton
metadata. It does **not** make these characters selectable or replace
the game's native animated Samus/Weavel rigs.

## Content generated

- Echoes Dark Samus: `darksamus-echoes.obj`
- Prime 1 Space Pirate Trooper: Power, Ice, Wave and Plasma OBJ bodies
- One `*.skeleton.json` for each original bone hierarchy/character

All output is local under the user's app data
`source-character-previews/`. The tool does not bundle or upload original
Nintendo model data and does not modify default character model packs,
network packets, hunter IDs, replay formats or Community maps.

## Usage

```sh
dotnet run --project tools/retro-character-preview-check -c Release
dotnet run --project tools/retro-character-preview-check -c Release -- --source "/path/to/owned/Echoes.iso"
dotnet run --project tools/retro-character-preview-check -c Release -- --source "/path/to/owned/MetroidPrime.iso"
```

Only exact known GameCube editions/PAK fingerprints are accepted.
Compressed RVZ/WIA, Trilogy and Corruption/Wii are not yet supported.

The output contains **geometry without textures, rigged animations,
first-person cannon or morph form**. Follow-up work must retarget the
native skeleton/animations and convert supported materials into the
existing Project Prime HD character runtime before enabling gameplay.

CI runs synthetic geometry tests with no game assets. Real-source
validation, visual comparison, and performance acceptance are pending.
