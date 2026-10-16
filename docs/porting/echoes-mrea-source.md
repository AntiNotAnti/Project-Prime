# Echoes MREA source sections (PORT-03a)

Validates all six arena MREA source records from the player's local Echoes GameCube ISO/GCM, with no proprietary bytes written to disk or uploaded.

- MREA v0x19 header, section table and 32-byte cluster alignment.
- Bounded uncompressed and segmented-LZO cluster assembly, section indices and transform metadata.
- Stable local-only Echoes map source resource IDs for Sidehopper Station, Spires, Crossfire Chaos, Pipeline, Spider Complex and Shooting Gallery.
- Synthetic (asset-free) validation for both raw and segmented clusters, invalid sizes, truncated reads and invalid section refs.
- This is a **source section reader**. Geometry/collision conversion, map packages, spawn/pickup extraction, music, jump pads, visual fidelity, new modes, and playable map selection are not yet implemented.

Asset-free check:
dotnet run --project tools/external-import-check -c Release

Optional real local-media preflight:
dotnet run --project tools/external-import-check -c Release -- --probe-arenas /path/to/Echoes.iso

The probe requires the pinned SHA-1 of Metroid6.pak, decodes all six MREA sections in memory and prints only counts and names. No Nintendo data leaves the user's local image.
