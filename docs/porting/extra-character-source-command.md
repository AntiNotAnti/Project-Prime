# PORT-17: client executable local character preview command

The game now accepts a headless command that uses the existing bounded,
user-owned-source-only Retro importer without requiring an OpenGL window or
an installed MPH cartridge:

```sh
ProjectPrime -retrocharacterexport "/path/to/owned/MetroidPrime.iso"
ProjectPrime -retrocharacterexport "/path/to/owned/Echoes.iso"
```

The files are written to
immutable generations under `<user-data>/source-character-previews/.local-imports/<appearance>/<generation>/`. The command prints the selected OBJ paths; use their containing generation directory as `--source-root` for authoring tools. Each receipt binds geometry, skeleton, weights, sparse motion and any optional raw graph proof. Existing loose exports are kept as unmanaged artist files and are never silently reactivated. Interrupted updates retain the prior receipt. Settings → Profile offers the same verified local-source importer with Update selected, Update all from source, Check and Remove actions. Remove withdraws discovery and keeps generations for current consumers; it does not promise disk reclamation.

Outputs remain local authoring data. No copyright-protected game assets are bundled, sent to a
server, installed into the runtime character packs or uploaded to
Community. Existing seven playable hunter IDs, hitboxes, network packets,
replays, and player selection remain unchanged.

The four Trooper appearances and Echoes Dark Samus remain **nonplayable
model previews**, not completed character ports. Real-media acceptance,
native skin/animation retargeting, HUD/viewmodel and alternate-form
rendering remain the next milestones.

The original developer asset-free regression check still runs
`dotnet run --project tools/retro-character-preview-check -c Release`.
