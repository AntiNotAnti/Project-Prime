# Local GameCube source reader (PORT-01)

This slice adds a bounded, functional read-only filesystem source, **not** playable imported characters or maps.

- Opens user-supplied local uncompressed GameCube ISO/GCM for supported editions GM8E01 and G2ME01.
- Validates header, FST entry count, file ranges, directory nesting, filename and traversal rules.
- Exposes lazy seekable read-only windows over source files without copying entire PAKs.
- SHA-1 fingerprints pinned from Second Hunt's trooper, Dark Samus and Echoes arena recipes are edition identifiers, not trust signatures.
- Does not write generated assets, upload content or change current multiplayer/replay behavior.
- RVZ, WIA, other editions and Wii/Corruption are explicitly unsupported until verified readers and fixtures are added.

## Asset-free tests and local probe

```sh
dotnet run --project tools/external-import-check -c Release
dotnet run --project tools/external-import-check -c Release -- --probe /path/to/your/MetroidPrime2.iso
```

Default selftests build only synthetic FST bytes, never Nintendo game data. The optional probe shows disc ID, file count and archive verification status only. Later converter PRs will use this source to construct local character rigs and Echoes arenas in Project Prime's existing render/map runtime.

Acceptance remains pending real local legally obtained media, Windows/Linux/Android and dedicated-server regression testing.
