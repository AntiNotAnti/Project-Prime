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
ordinary and source-coordinate visible faces. Existing collision and geometric
validity checks remain in force; this format does not repair source geometry
or infer playable collision. Nullable ordinary faces can use separate materials.

The packer collects pairs at the actual vertex emission call, after spatial
partitioning, material ordering, fan expansion and vertex-budget splitting.
Within each native list it emits triangles before quads. A polygon fan carries
corners `(0,i,i+1)` from both source arrays. Native fallback UV commands remain
zero. A declared/built source-presence mismatch is refused.

## Binary and publication

A source map adds `<prefix>_SourceGeometry.bin` as its sixth output. Ordinary
maps retain the five-output record constructor/deconstructor, five manifest
hashes and compiler version10. Source builds use compiler version11 and
component profile1. All model/companion bytes are admitted before replacing any
output; the complete manifest is published last. Cache checks and installation
include the conditional sixth file. Installation refuses mismatched source and
destination output contracts before changing destination files.

The little-endian PPSG v1 layout is:

| Section | Content |
| --- | --- |
| 192-byte header | Version/extents/profile, map GUID, recipe SHA256, exact native model SHA256, coordinate-contract SHA256, directory SHA256, zero reserved bytes |
| 96-byte sorted entry | Native display-list/material ID, emitted corner/triangle/quad counts, contiguous payload range, exact raw native command SHA256, coordinate payload SHA256 |
| Payload | Four float32 values per emitted corner: BASE U/V and lightmap U/V |

Limits are65535 entries,65000 corners per native list,1200000 source corners,
64MiB per component and256MiB per bound native model. Authoring preflights the
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

Qualification results will be recorded after the frozen source run. Windows
SSH remains unavailable because authentication is refused; the user-authorized
Mac fallback is used. No physical Android/device acceptance is claimed.

## Disabled, remaining and rollback

Source rendering is not activated. Remaining work includes a complete arena
recipe, a separately qualified BASE/lightmap material pair and sampler policy,
optional secondary GPU UV layout, source shader semantics, lighting policy,
unsupported-material fallback tiers and arena mechanics/physics acceptance.
The source parser's common-opaque-candidate flag is not shader qualification.

Removing the source-coordinate field restores ordinary authoring/output shape
only after explicitly choosing how to re-author its native UVs. Existing maps
without that field need no migration. Reverting this slice leaves legacy
content unchanged; compiled source generations must be rebuilt by a compatible
compiler rather than loading their companion with an older parser.
