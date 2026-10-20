# SH-04A source material and rendering gap

Research date: 2026-10-19 local / 2026-10-20 UTC. This is an implementation
boundary and owned-source metadata witness, not a completed source-material
feature or GPU acceptance report. No source texture, model, archive, decoded
image, or derived commercial payload belongs in this document or repository.

## Evidence identities

- Frozen Second Hunt reference: `41452f2d053fcfa679e33c00a73d120a53f59e61`
  in `/tmp/prime-secondhunt-reference`. The historical integration pin remains
  `c16a907b3658c2fd4079e394a73711596a85caee`; this research does not replace it.
- Project parser witness: immutable Release assembly built from
  `3e181df59aacb136e49e7d22d33108cacaef362c`, SHA-256
  `c98919449c1a3dac61aad432da88f6bbd26cf81910e9112f7a66b67bdff8da28`.
  The isolated metadata probe referenced that assembly, not mutable worktree
  build outputs. Its build completed with zero warnings/errors.
- Authorized owned media: GameCube Echoes USA, `G2ME01`, revision 0;
  RVZ length `1287435724`, SHA-256
  `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
  The complete media hash was reread at `2026-10-20T04:35:42Z`.
- The read-only probe verified `Metroid6.pak` through the existing archive
  verifier, SHA-1 `b8b92af37dc772ecd8234fb1ce664be0493188f6`, then parsed each
  known MREA and TXTR header in memory. No assets were exported. Counts below
  describe these six world material sets, not every static actor, sky, fluid,
  campaign area, or texture in Echoes.

## Actual six-map inventory

All listed materials are referenced by at least one world surface. Texture
counts are per-map material-set TXTR entries, summed across maps; shared TXTR
resources across different maps are not a globally deduplicated inventory.

| Arena | MREA | Materials | TXTR entries | CMPR / RGB5A3 | Exact common materials | Common surfaces |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Sidehopper Station | `02690de1` | 107 | 108 | 108 / 0 | 88 | 1368 |
| Spires | `09749b2f` | 47 | 57 | 56 / 1 | 29 | 589 |
| Crossfire Chaos | `44d34a2e` | 43 | 48 | 47 / 1 | 35 | 388 |
| Pipeline | `845dcf93` | 57 | 65 | 65 / 0 | 41 | 627 |
| Spider Complex | `e7095052` | 42 | 56 | 56 / 0 | 29 | 977 |
| Shooting Gallery | `d97a600b` | 51 | 56 | 55 / 1 | 38 | 735 |
| Total | | 347 | 390 | 387 / 3 | 260 | 4684 |

There are 5660 source surfaces overall. The common subset covers 260/347
materials and 4684/5660 surfaces; surface coverage is not triangle or screen
coverage. Observed texture dimensions reach 1024 pixels wide and 512 high;
declared mip counts reach seven. No used opaque material is a one-texture,
one-stage, static identity-UV recipe. A first-texture preview would often show
the lightmap in place of the base image.

## Exact common program and UV witness

All 260 common materials have the same parsed program, apart from resource
indices: flags `0x36882`, channel word `[0x3001]`, blend source/destination
`1/0`, two textures/stages/texgens, no konst colors, indirect stage, or UV
animations. Require the complete program when qualifying it, not just its
flags or texture count.

| Field | Stage 0 | Stage 1 |
| --- | --- | --- |
| Color inputs | `[15,4,8,10]` | `[15,2,8,15]` |
| Alpha inputs | `[7,7,7,6]` | `[7,7,7,4]` |
| Raw color / alpha operation | `0x300 / 0x300` | `0x100 / 0x100` |
| Texture / coordinate | `0 / 0` | `1 / 1` |
| KColor / KAlpha / raster selector | `0 / 0 / 4` | `0 / 0 / 4` |
| Texgen type / source | `0 / 4` | `0 / 5` |
| Matrix / post / normalize | `30 / 61 / false` | `30 / 61 / false` |

In GX selector terms, stage 0 writes
`C0 = clamp(C1 * lightmap.rgb + RASC)` and alpha 1; stage 1 writes
`PREV.rgb = clamp(C0 * base.rgb)` and `PREV.a = base.a`.
This is not simply an unconditional `base * lightmap` program: initial C1
and rasterized color are external inputs. Frozen upstream explicitly sets
TEV register 1 (`Reg[2]`) to white in `ArenaThumbnail`; its scene-light rig
is an external approximation. That proves its chosen rendering inputs, not
the original game's complete dynamic lighting. Project currently has no
bounded Echoes light-section reader beyond locating `LightingSection`.

The owned-source probe walked 109303 display-list corner occurrences for
this subset. Every T0/T1 reference was within its corresponding model UV
array, and every referenced float UV was finite. T0 uses the signed-short
UV array divided by 32768; T1 uses the float UV array. Model-local indices
must be offset independently when models are merged.

| Arena | Common corner occurrences | Lightmap UV component range | Base UV component range |
| --- | ---: | --- | --- |
| Sidehopper Station | 25795 | `0.00097656..0.99899292` | `-11.02036..11.69999` |
| Spires | 14460 | `0.00097656..0.99899292` | `-22.63215..8.85763` |
| Crossfire Chaos | 17185 | `0.00097656..0.99899292` | `-25.5..24` |
| Pipeline | 16792 | `0.00097656..0.99899292` | `-20.23528..21.23528` |
| Spider Complex | 19509 | `0.00100708..0.99899292` | `-184.96622..207.88986` |
| Shooting Gallery | 15562 | `0.00097656..0.99899292` | `-35.79074..27.10317` |

Do not clamp base UVs or route them through native texel-coordinate packing:
`GeometryCompiler` rejects coordinates with absolute value >=2048 and the
native display list has a fixed-range TEXCOORD representation. Multiplying
these source UVs by image dimensions can exceed that range. UV.z already
has matrix/index uses and cannot be appropriated for a second UV set.

## Remaining 87 materials are separate capabilities

| Family beyond the exact common program | Materials | Surfaces | Additional requirement |
| --- | ---: | ---: | --- |
| Static three-stage, three-texture `0x76882` | 54 | 742 | Third UV/texture; final additive texture stage |
| Animated three-stage `0x76882` | 3 | 26 | Third UV/texture plus source UV animation |
| Animated four-stage `0xf6882` | 1 | 10 | Four source layers/UV transforms |
| Animated five-stage `0x1f6882` | 3 | 36 | Five source layers/UV transforms |
| Two-stage konst-colored `0x1688a` | 4 | 16 | One lightmap, then konst modulation instead of base texture |
| Additional occluder flag `0x36a82` | 1 | 2 | Explicit occluder visibility policy; do not silently equate flags |
| Source-alpha blends, source/destination `4/5` | 10 | 70 | Source depth/sort/alpha-test flags and transparency ordering |
| Additive blends, source/destination `1/1` | 11 | 74 | Additive pass/depth behavior; some UV animation |
| Total | 87 | 976 | |

The static third-stage family retains the first two stages, then adds color
inputs `[15,8,12,0]`, alpha `[7,7,7,6]`, raw operations `0x100/0x100`,
texture/coordinate 2 and identity texgen source 6. Treating it as two-stage
would drop an actual additive layer. The konst family replaces stage 1 with
color `[15,2,14,15]`, uses a konst selector and no stage-1 texture. Blend
families additionally vary depth-sort, alpha-test and other flags; matching
the opaque pair's selectors does not qualify their render policy.

An optional edition that retains all 87 families as explicitly counted
existing gray fallback geometry is honest as a **partial material preview**.
It must disclose that fallback, preserve all original collision, and never
claim full SH-04A or faithful whole-map rendering. Silently hiding unknown
materials creates missing windows, glow layers, or architecture. Rejecting
every map containing an unsupported family would reject all six. Do not
infer original occluder behavior from the current gray preview or from the
upstream writer, whose pass selection only distinguishes blend modes.

## Current owners and concrete missing work

- `src/MphRead/Mods/Import/EchoesWorldGeometrySource.cs` presently retains
  positions and triangle position indices. It skips material-set TXTR IDs,
  retains only each material's vertex descriptor, and discards UV indices.
  Extend this bounded reader to retain qualified material programs, resolved
  resource references, model-local short/float UVs and per-corner indices.
  Keep its existing geometry/corner/surface limits and add checked UV and
  material-array budgets. Collision remains `EchoesCollisionSource`.
- `EchoesVisualLayer.cs` compiles flat material-0 visual meshes with
  `Solid=false`; original physics meshes become collision-only. Reuse that
  split. A material edition needs its own versioned identity and explicit
  fallback inventory. Do not change LOCAL, VISUAL, CANNON, ROUTED or STATIC
  map/replay identities. `EchoesStaticVisualLayer.cs` already retains CMDL
  materials/UVs for seven Spires actors, but only validates texture headers;
  those actors are a separate qualification inventory, not part of the 347.
- `src/MphRead/Mods/Import/Retro/Cmdl.cs` already parses the necessary program
  selectors, flags, texgens and UV animation data. Its public material reader
  needs boundary/count validation suitable for newly retained untrusted data;
  do not assume having parsed a descriptor qualifies its rendering.
- Project has no TXTR decoder. Frozen reference
  `src/MphRecomp.Core/Import/Retro/Txtr.cs` supports I4, I8, IA4, IA8, RGB565,
  RGB5A3 and CMPR; palette formats and RGBA8 are unsupported there. For these
  six world sets, a bounded CMPR + RGB5A3 decoder is sufficient. Validate
  header/type, dimensions, tile-rounded payload and mip extents with checked
  arithmetic before allocation; reject truncation/unsupported formats. Keep
  top-first rows, big-endian selectors and CMPR transparent-block semantics.
  Source mip/atlas handling and sampler policy require explicit qualification;
  do not silently copy upstream's lightmap magenta-filler modification.
- `src/MphRead/Mods/MapGen/MapDefinition.cs` owns `MapMaterial` and
  `Geometry/MapGeometry.cs` owns per-face material and one per-corner UV set.
  `BuiltMap.cs`, `Geometry/GeometryCompiler.cs` and `MapPacker.cs` likewise
  carry one UV set. Add optional source lightmap semantics and a second
  full-precision UV stream through the existing compiler/output snapshot.
  A compiled geometry companion must be tied to the exact model hash, map
  identity, source recipe and immutable generation; mismatches must fall back
  or reject, never bind UVs by guessed vertex order. Preserve UVs through fan
  triangulation, face partitioning, cache/snapshot copies and fingerprints.
- `Mods/Render/Materials/MaterialPack.cs` and
  `MapMaterialAssetRegistry.cs` own authored image identity/loading;
  `TextureAssetManager`, `TextureReplacementPack` and `RetainedWorldTextures`
  own shared uploads/lifetimes. Add a semantic lightmap channel there rather
  than use normal/specular/emissive as a hidden lightmap carrier.
  `Renderer.cs`, `Shaders.cs`, `Mods/Render/EsShaders.cs`, retained vertex
  arrays and `WorldShaderVariants` must consume the same qualified dual-UV
  material through existing Scene draws/backend dispatch. Do not introduce
  a second world renderer, duplicate draws, or repurpose existing attributes.
- `Mods/Render/Characters/CharacterSourceMaterial4Recipe.cs` is an exact
  captured additive character material with fixed proofs and one UV. Its
  source snapshot, CPU oracle, lifetime and negative-gating patterns are useful;
  its shader and hardcoded source identity are not a general world material
  translator. The common world recipe needs its own exact positive/negative
  contract and explicit C1/RASC inputs.

The smallest useful functional next slice is that exact static two-stage
recipe, real CMPR/RGB5A3 decoding, full-precision dual UVs and one shared
material shader path across all six optional offline previews. Supply explicit
Project lighting inputs to the two-stage evaluator and label them honestly;
original Echoes light fidelity remains gated until separately proven. Keep
unsupported geometry on disclosed gray fallback. Three-stage, animated,
blended, actor, sky and fluid capabilities remain unavailable.

## Publication constraints

Use `EchoesPrivateMapImport` and the existing `LocalImportBundle` writer lease,
immutable generations and receipt commit marker. Stage all map and image
components before publication, validate detached snapshots, and retain the old
good generation on cancellation or failure. New optional identities must not
overwrite historical files or loosen private-source/network/replay gates.

There is a concrete storage-contract gap: `LocalImportBundle` currently allows
64 flat `.json/.obj/.bin/.glb` components, 64 MiB each and 256 MiB total.
`MapAssets` resolves ordinary relative image/native `.tex` files, while private
source maps are intentionally rejected by both `MapPackageBuilder` and
`MapPackageReader`. A private `.ppmap` or arbitrary unreceipted image directory
is not an acceptable shortcut. Extend the existing receipt owner for bounded,
validated image/native-texture components or a verified existing-owner asset
container, with all resulting receipt/image/aggregate limits tested. The
common subset alone references 241 base TXTR entries after per-map
deduplication (78/28/33/38/28/36); a 64-component design is insufficient.

`MapMaterial.Texture` requires a single-entry native FPTX texture; otherwise
`MapPacker` borrows a native room material. HD `Albedo` alone does not create
an independent native fallback. FPTX v1 accepts power-of-two 8..1024 dimensions,
1..256 palette entries and byte palette indices; its current packer uses
opaque textures and repeat sampling. Define a deliberate native fallback
for qualified source materials, without embedding unrelated cartridge art
or calling a quantized fallback the source-faithful result. All private
derived files stay local; source payloads are never committed or packaged.

## Acceptance before enabling the optional edition

1. Source-free TXTR fixtures cover CMPR endpoint order, all subtiles, row
   orientation, transparent blocks, RGB5A3 alpha/opaque cases, tile padding,
   mip extents, unsupported formats, malformed lengths and allocation limits.
2. Extend `tools/external-import-check` world/material fixtures for multiple
   models and UV-array offsets, indexed triangle/strip/fan corner ordering,
   exact recipe qualification and single-field negative mutations. Preserve
   the existing positions/triangles, independent collision hashes, historical
   IDs, local-only gates and complete unsupported-face counts.
3. Compiler/snapshot/cache tests prove both full-precision UV streams survive
   triangulation and partitioning, large positive/negative repeated base UVs,
   source-generation swaps and corrupt/stale geometry companions. Publication
   fixtures cover cancellation, writer contention, missing/linked images,
   budgets, update/remove/reinstall and keeping old scene assets readable.
4. Add content-free shared-Scene GPU pixel fixtures to `tools/material-pack`:
   independent UVs must produce different known lightmap/base samples; test
   C1/RASC/clamp math against a CPU oracle, alpha policy, sampler behavior,
   large repeats, absent/corrupt companions and allocation/teardown failures.
   Existing tests do not yet exercise a source lightmap channel.
5. Extend the owned-source probe to assert the exact inventory above, decode
   every selected texture, verify all qualified UVs and collision identity,
   publish/reload all six editions and clean every derived payload afterward.
   Retain only metadata and test results. Inspect real gameplay/editor Scene
   draws and texture retirement on supported GPUs before enabling the edition.

Existing content-free acceptance commands (use the configured SDK, not a
runtime-only `dotnet` installation):

```sh
dotnet run --project tools/external-import-check -c Release
dotnet run --project tools/material-pack -c Release
dotnet run --project tools/material-pack -c Release -- --gpu
dotnet run --project tools/material-pack -c Release -- --pixels opengl /tmp/sh04-material-opengl.json
dotnet run --project tools/material-pack -c Release -- --pixels metal /tmp/sh04-material-metal.json
dotnet run --project tools/material-pack -c Release -- --compare /tmp/sh04-material-opengl.json /tmp/sh04-material-metal.json
```

The pixel commands use actual requested backends and production Scene material
draws; Metal needs its existing native runtime. Vulkan has an analogous
`--pixels vulkan` mode. Add the new recipe fixtures to those suites first;
passing their current 22 fixtures does not prove this unimplemented channel.
There is no current owned-material acceptance command or source-lightmap GPU
result. Physical Android, other unavailable GPUs, original lighting,
transparency and complete-map fidelity remain real gates. SH-04A is incomplete.
