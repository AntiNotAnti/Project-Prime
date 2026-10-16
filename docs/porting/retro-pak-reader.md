# Retro PAK resources (PORT-02a)

This is a prerequisite decoder for local Dark Samus model and Echoes MREA imports, not a playable feature.

- Index MP1/MP2 PAK 3.5 over a seekable GameCube source without loading the whole archive.
- Decode only requested resources in memory: stored, MP1 zlib, and Echoes' 0x4000-byte segmented stored/zlib/LZO1X.
- Reject dangerous counts, name lengths, resource offsets, compressed flags, malformed segments and expansion budgets.
- Keep game bytes local: never serialize them into source control, a network packet, or a Community map package.
- LZO1X implementation adapted from Second Hunt's MIT-licensed clean-room lzokay port (credit preserved in the source).

Selftest: dotnet run --project tools/external-import-check -c Release

To check a particular MREA from a legally acquired local disc without exporting it:

dotnet run --project tools/external-import-check -c Release -- --probe-resource /path/to/Echoes.iso Metroid6.pak 09749b2f

The probe verifies the source archive SHA1 against the pinned recipe, decodes that single MREA in memory, then prints only the resource type and size.

Remaining: parse MREA geometry/collision/scripts, generate Project Prime map data, and render imported character rigs. No gameplay, lobby, map-selection or replay changes are made by this PR.
