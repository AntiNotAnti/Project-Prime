# PORT-04: Echoes collision and script extraction

This is a local-only parser, not yet a registered playable arena.

- Reads bounded MREA collision surfaces, edges, material bits and vertices with strict index validation.
- Reads script layers for active spawn transforms, pickup type and amount, and editor metadata.
- Adds synthetic tests for valid, flipped and malformed collision data and a SCLY spawn object.
- Keeps the original Echoes Z-up metres in this source layer; a later compiler performs the explicit world conversion.
- Adds `--probe-surfaces <local Echoes ISO/GCM>` to report triangle, vertex and active-spawn counts for all six arenas.
- The probe verifies the local Metroid6.pak before decoding, never writes proprietary bytes or uploads them.

Remaining work: authored map geometry, spawns/pickups, lighting, collision materials, runtime registration, and authoritative dynamic objects.
