# PORT-17: client executable local character preview command

The game now accepts a headless command that uses the existing bounded,
user-owned-source-only Retro importer without requiring an OpenGL window or
an installed MPH cartridge:

```sh
ProjectPrime -retrocharacterexport "/path/to/owned/MetroidPrime.iso"
ProjectPrime -retrocharacterexport "/path/to/owned/Echoes.iso"
```

The files are written to
`<user-data>/source-character-previews/` as OBJ meshes and skeleton
metadata only. No copyright-protected game assets are bundled, sent to a
server, installed into the runtime character packs or uploaded to
Community. Existing seven playable hunter IDs, hitboxes, network packets,
replays, and player selection remain unchanged.

The four Trooper appearances and Echoes Dark Samus remain **nonplayable
model previews**, not completed character ports. Real-media acceptance,
native skin/animation retargeting, HUD/viewmodel and alternate-form
rendering remain the next milestones.

The original developer asset-free regression check still runs
`dotnet run --project tools/retro-character-preview-check -c Release`.
