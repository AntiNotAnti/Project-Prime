# PORT-05: locally generated Spires collision blockout

This slice takes real user-source Echoes MREA surfaces and spawns to an **in-memory Project Prime MapDefinition** and compiles that definition using the existing `MapBuilder`. It is the first map conversion vertical slice; it is not a playable HD arena yet.

- Source coordinates convert Z-up metres to MPH Y-up units using the upstream scale 1.6/2.7.
- Source triangle surfaces are separated by terrain and beam-through flags into bounded `MapMesh` chunks.
- SPAWN and supported PICKUP objects become normal native map entities with stable local GUIDs.
- The visual graybox is collision-backed and borrows default MPH material, not Echoes graphics. Native Echoes MREA render geometry, textures, statics, water and movement still need importing.
- The generated `MapDefinition` is explicitly `LocalSourceOnly` and **cannot** be packed via `MapPackageBuilder` or accepted in an existing `.ppmap`. No export path is provided.
- `--probe-spires-blockout /path/to/Echoes.iso` verifies the local disc, builds the native map in memory, prints summary counts, and discards the geometry.
- Asset-free selftests cover conversion, deterministic IDs, vanilla map serialization and rejection of nonredistributable map packaging.

Offline/online play is not enabled by this change. Future runtime registration must require each participant and dedicated server to import matching data locally and must not transfer commercial assets.
