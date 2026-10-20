# Private source geometry companion

This slice carries the paired source coordinates introduced by
[PR821](https://github.com/AntiNotAnti/Project-Prime/pull/821) through authoring,
CPU analysis, final compiler emission and model loading. It implements private
transport infrastructure for SH-04. It does not activate an arena import
edition, source textures, lightmaps, TEV, secondary GPU attributes or mechanics.
The existing native renderer continues to draw the explicit white fallback.

## Implemented contract

`MapMesh.SourceUvFaces` is nullable and omitted from old JSON when absent.
A present list has exactly one nullable entry per mesh face. Each source entry
owns two finite float2 arrays, BASE and lightmap, with one pair per corner and
the exact `echoes-base-float-t1-lightmap-s16-t0-v1` contract. Source values are
never converted to native texels, normalized by image size or quantized into
the native packed UV field. Shared positions keep per-corner seams.

Compilation copies these arrays into read-only snapshots. Analysis, viewport
and compilation-cache owners preserve them without enabling new drawing.
Transforms and whole-object clones preserve corner order. Topology, legacy UV,
material-paint and model-reimport operations refuse protected meshes before
mutation. Prefab save/insert and portable packaging refuse them. Presence of
the coordinate field alone makes a map private even if outer flags are stripped.

The initial profile requires a persistent map ID, authored visible meshes and
unanimated procedural-white source materials with no active image/bank channels.
Q3, native-room and model-source mixing is refused. A material cannot mix
ordinary and source-coordinate visible faces. Existing collision checks and ordinary geometric
validity tolerances remain in force; this format does not repair source geometry
or infer playable collision. Nullable unqualified faces can use separate materials; every declared source-profile
face emits zero native UVs. For non-solid, non-collision source triangles only,
normal calculation uses origin-relative double intermediates and requires finite,
strictly positive area. It retains tiny source triangles without changing their
positions, winding or UVs; exact collinear triangles are still refused. Ordinary,
solid, collision-only and non-triangle tolerance rules remain unchanged. This
computed normal is the native fallback's normal, not original source lighting.

The packer collects pairs at the actual vertex emission call, after spatial
partitioning, material ordering, fan expansion and vertex-budget splitting.
Within each native list it emits triangles before quads. A polygon fan carries
corners `(0,i,i+1)` from both source arrays. Native fallback UV commands remain
zero. A declared/built source-presence mismatch is refused.

## Binary and publication

A source map adds `<prefix>_SourceGeometry.bin` as its sixth output. Ordinary
maps retain the five-output record constructor/deconstructor, five manifest
hashes and compiler version 10. Source builds use compiler version 11 and
component profile 1. All model/companion bytes are admitted before replacing any
output; the complete manifest is published last. Cache checks and installation
include the conditional sixth file. Installation refuses mismatched source and
destination output contracts before changing destination files.

The little-endian PPSG v1 layout is:

| Section | Content |
| --- | --- |
| 192-byte header | Version/extents/profile, map GUID, recipe SHA256, exact native model SHA256, coordinate-contract SHA256, directory SHA256, zero reserved bytes |
| 96-byte sorted entry | Native display-list/material ID, emitted corner/triangle/quad counts, contiguous payload range, exact raw native command SHA256, coordinate payload SHA256 |
| Payload | Four float32 values per emitted corner: BASE U/V and lightmap U/V |

Limits are 65,535 entries, 65,000 corners per native list, 1,200,000 source corners,
64 MiB per component and 256 MiB per bound native model. Authoring preflights the
expanded source-corner budget. Encoding admits aggregate extents before output
allocation, then copies and validates exactly the indexed values it writes.
Decoding validates all ranges, counts, finite values, digests and native command
grammar before retaining coordinate arrays. Native table lookup is bounded and
indexed once. Display-list parsing reuses the shared command decoder, which now
checks argument extents against each list rather than the containing model.

The static profile admits only generated triangle/quad groups, per-face
color/normal, zero native UV, VTX16 vertices and trailing NOPs. Each transported
list has one unambiguous native mesh/material binding. Counts alone cannot
retarget coordinates: changed/reordered native commands fail their digest even
when byte and corner counts stay unchanged.

## Runtime ownership

Room metadata captures map/recipe/archive identity without content IO. Worker
loading reads a bounded manifest and component, verifies both against the
**same initial model bytes used by the parser**, and checks that publication's
manifest is unchanged afterward. Missing, stale, corrupt, linked or unsupported
source evidence fails that model load explicitly. It is not silently discarded.
Headless loading validates the raw native grammar too, even though normal
headless model loading does not retain render commands.

The detached immutable snapshot belongs to that model generation and is copied
into scene instances. New room registration uses the existing model/prewarm
invalidation owners; retained models keep their old data. No global mutable map
lookup can retarget their coordinates. The snapshot owns no streams, GPU
objects or protected output files.

## Qualification

Frozen source `e0bc67d709bec332b0cab6c4a681dd237b971cce` passed all25 serial
commands on the user-authorized Mac fallback. Two exact Git archives, each
containing5914 tracked files, remained unchanged through the run. It includes
concurrent PR822 and uses protocol67 with no wire change. Later concurrent
custom-weapon changes are outside this frozen whole-binary proof.

| Evidence | Result |
| --- | --- |
| Desktop / dedicated Release | Both pass, zero warnings/errors |
| Android Release |119 AOT assemblies; signed arm64-v8a/x86_64 APK;25 existing XML-comment warnings, zero errors |
| APK verification | v2/v3 signatures pass; ProjectPrime AOT present for both ABIs |
| Source geometry |2126 synthetic checks, including final emission, tiny-triangle policy, stale generation, actual model adoption, immutable scene copy, cache corruption/missing repair and edit refusal |
| Bounded codec | Impossible inventory refused after560 allocated bytes, without declared large arrays |
| Existing source/material owners |101 coordinate,952 material-reader,191 TXTR,137 bank and128 bank-material checks pass |
| Editor / shared material GPU |486 editor and58 material-tool checks pass; synthetic bank upload/readback/reload/quality/teardown regressions pass |
| Other contracts |10642 pure custom-weapon checks; replay543/314/84/51 and campaign-map91 checks pass; character/source-rig and owned body/ball lifecycle pass |
| Independent legacy witness | Old qualified PR821 DLL and current DLL produce identical five file lengths/hashes, manifest, recipe and cache key |

The owned transport probe compiles **all six** source arenas without filtering
or repairing faces:68470 original triangles,347 used materials,260 candidate
materials,54069 candidate triangles,835 companion entries and162207 transported
corners. Per-material multisets match both original UV float bits and exact
expected native packed-position/UV tuples. Unknown material families receive
no companion channel. Source collision is not inferred: each probe has only
one explicitly invented collision scaffold, which is absent from source visual
counts. All temporary derived payloads were removed.

The first exploratory run passed four arenas and found four finite tiny
triangles rejected by old authored thresholds. The narrow visual-only normal
path fixes that admission defect; two original absolute-position Newell
calculations cancelled to zero and one reversed direction. All four remain
present even though existing native packing collapses them to coincident
vertices. No full-float native-position or original-lighting claim is made.

The RVZ SHA256 before/after is
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
The existing Apple material-editor sampler warning remains in the GPU log;
all readback, binding and teardown assertions pass. These are existing material
infrastructure checks, not a draw of the source UVs or complete source arenas.

| Frozen binary | SHA256 |
| --- | --- |
| Desktop |`6c19c0f7edfd58eed95ce3f1b63cadba52c621ad56fc2aef022d919b6cd63433`|
| Dedicated |`172d6c79d8728b3229b5f96fba1f353acf5e98e5674571b2db1601710df10a34`|
| Signed APK |`c951c6150aa6eef5a1b6ff33ff18874352d9166274ca8c18c57fd24812c8824b`|

Ignored receipts and source-integrity manifests live in
`artifacts/sh-integration/geometry-e0bc67d70/`. Exploratory failures and their
corrections are retained separately. Completed temporary build copies were
removed after disk exhaustion; source receipts and the old compiler needed by
the independent comparison were retained. Windows SSH authentication remains
refused. No physical Android, Windows/Linux, full-game or source-rendering
acceptance is claimed.

## Disabled, remaining and rollback

Source rendering is not activated. Remaining work includes a complete arena
recipe, a separately qualified BASE/lightmap material pair and sampler policy,
optional secondary GPU UV layout, source shader semantics, lighting policy,
unsupported-material fallback tiers and arena mechanics/physics acceptance. Native
positions still use the existing signed16 fixed-point packing; the companion
preserves full-float UVs and does not claim full-float native position fidelity.
The source parser's common-opaque-candidate flag is not shader qualification.

Removing the source-coordinate field restores ordinary authoring/output shape
only after explicitly choosing how to re-author its native UVs. Existing maps
without that field need no migration. Reverting this slice leaves legacy
content unchanged; compiled source generations must be rebuilt by a compatible
compiler rather than loading their companion with an older parser.
