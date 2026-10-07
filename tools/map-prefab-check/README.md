Run `dotnet run --project tools/map-prefab-check -c Release -p:MphReadAvalonia=true`.

The fixture uses the existing MapDocument, bounded command history, MapBuildSnapshot, MapCompiler and MapPackageBuilder. It covers stable source/instance IDs and revisions, transform flattening of geometry/entities/navigation, field overrides, local deletions, upstream additions/removals, update/detach undo and redo, snapshot isolation, missing-source and invalid-transform transactions, structural category comparisons, provenance-free runtime fingerprints and packages that compile after the prefab library file disappears. An inline FPTX texture makes these checks independent of cartridge assets.

Animated prefab checks cover insertion into its source owner, repeated instances,
stable native track names across source updates, user-authored name collisions and
local overrides, exact Undo/Redo, and independent material identities. Canonical
native builds verify the emitted UV and flipbook tracks against every resolved
material name; packages remain compilable after the prefab library is deleted.
