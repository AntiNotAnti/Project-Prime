# Community Hub CH-05B2 | Selected-map previews and compatibility filters

**Stacked on CH-05A PR #607.** Merge that first. Independent of CH-05B1 network/storage PR #608.

## Changes
- Player Community UI requests the server's existing `packages/{hash}/preview` only when a map is selected. The preview never downloads a full `.ppmap`. Switching maps, leaving the page and disposal cancel the previous fetch and release the decoded bitmap.
- Network response is bounded to 1 MiB PNG, rejects unsupported media and dimensions exceeding 2,048 pixels/4.2 megapixels before decode. Missing preview or legacy server support is nonfatal.
- Discover and Favorites reuse server-side mode filters and new `players` compatibility filtering. Compact filter options: All, 2P, 8P and Team Battle. Legacy pre-v2 fallback filters in memory without weakening exact identities.
- Controller navigation stays in the existing Avalonia shell. Existing immutable catalog snapshots and install/Ready paths remain unaffected.

## Acceptance
```bash
dotnet run --project tools/community-service-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet build src/MphRead/MphRead.csproj -c Release
```
Loopback checks cover bounded preview reads and server-side player-count filters. Manual desktop/Android checks must verify rapid selection, rotation, touch/controller navigation, DPI and 830x390 layout. No device GUI screenshots or performance timings were collected here.

## Further visual work
Featured/trending needs a curated/moderated signal source and should not be invented from favorite counts alone. Grid virtualization and additional image thumbnails await hardware UI acceptance; a bounded selected-map image is deliberately the first compatible slice.
