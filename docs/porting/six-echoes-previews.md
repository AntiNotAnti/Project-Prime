# PORT-10: all six Echoes arenas, local collision preview tier

Adds a dedicated local-only batch import for Sidehopper Station, Spires, Crossfire Chaos, Pipeline, Spider Complex and Shooting Gallery.

- Reuses Project Prime's native custom-map catalog and the same MREA collision, spawn/pickup and fallback geometry conversion from Spires.
- Assigns stable, distinct private map identities, retains Spires' prior ID and filename, and pre-validates all six before writing any of them.
- Source is the player's own uncompressed Echoes ISO/GCM. The import saves separate locally derived JSON recipes and does not create or distribute `.ppmap` archives.
- Current preview capabilities restrict the generated drafts to solo Battle; multiplayer, online download, objective modes and dynamic Echoes effects are not enabled.
- Adds `ProjectPrime -importechoesarenas <path-to-owned-Echoes.iso>` and updates desktop Settings > Profile to install all six previews off the render thread.

Remaining: faithful source MREA render geometry and textures, jump pads, moving/destructible props, water, bot navigation, real-media acceptance and compatible dedicated-server gameplay. These are not yet full Echoes arenas.

Asset-free regression tests include all six names, unique identities, Spires compatibility and refusal to generate any Community archive.
