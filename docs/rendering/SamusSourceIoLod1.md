# Samus SourceIO LOD1

Local production kit: `samus-hd-kit/sourceio-lod1/` (generated art remains Git-ignored).
Authoring: `Samus_SourceIO_LOD1_Native17.blend`; detailed evidence: `LOD1-RESULT.md`.

Installed pack `samus-sourceio-lod0-lod1-and-rigid-cannon` contains the unchanged
10,106-triangle Weighted4 LOD0, a **5,052-triangle / 6,314 exported-vertex LOD1**,
and the unchanged 2,045-triangle RigidNodes first-person cannon.

LOD1 derives from the frozen Source LOD0. Matching Source UV-boundary records are
welded before collapse to keep seams coherent; per-loop UVs and layered visor
faces remain. Helmet, emissive, muzzle and material boundaries are protected.
Its authoring mesh has 482 genuinely blended vertices; GLB splits produce 1,291
blended records. All influences remain normalized and bounded to four native joints.
Native LOD1's small right-shoulder rest difference is baked into the derivative.
The generated export helper and all source atlas bytes remain unchanged.

## Renderer and acceptance

The existing native player draw selects LOD1 at a distance of three game units
for non-main players when maximum player detail is off. No threshold/gameplay
change was needed. Missing replacement tiers retain the existing native fallback.

Scene character texture uploads now share identical embedded content across LOD
GLBs. Keys include upload class/channel, opacity and map factors, texture quality
and sampling policy; scene unload and quality invalidation clear the shared cache.
The second tier leaves tracked character texture residency unchanged at
99,265,191 bytes (~94.7 MiB). This is allocation accounting, not GPU-driver residency.

```
ProjectPrime -lod1acceptancecheck 'TEST ARENA' -output /absolute/output -noupdate -debuglog
```

The diagnostic runs real Metal simulation/render with an AI opponent and synthetic
Samus controls. It verifies the native threshold, full palettes, selected HD
packet identities, repeated switches, maximum-detail override, material toggles,
shared bindings and unchanged residency when the second tier loads.

Final acceptance: **2,091 eligible frames (1,911 LOD1 / 180 LOD0), 16 transitions,
zero failures**. Coverage includes movement/jump/aim/fire, damage/freeze/thaw,
death/respawn, morph/move/unmorph, bright/team/double-damage states. Preview scene
before/after checks drew 60/60 each. LOD0 regression passed 1,671 frames; cannon
regression passed 2,520 including 990 scheduled high-refresh pictures.
Loader/helper and frame-timing checks pass; release build has zero errors.

Review: 24 paired Blender bind/motion captures, gameplay samples and same-pose
runtime comparison. Earlier independent-island simplification is archived as a
rejected visual iteration; the final seam treatment improves those outline artifacts.
These samples do not establish exhaustive clipping-free coverage or physical
high-refresh display behavior. The native freeze overlay remains accepted.

## Installation and remaining work

`python3 samus-hd-kit/sourceio-lod1/install-pack.py` validates and snapshots the
previous pack before installation. `--rollback` restores the prior LOD0+cannon
pack. Diagnostics preserve saved preferences with the local `run-check.py` wrapper.
Production/export evidence is frozen in the local `releases/lod1-v1/` snapshot.

LOD1 SHA-256:
`7a8a7248bc961c1a60082af5b2e7b4bf57954f55e67561aa4e324a1485d1fdb6`.

Source Morph Ball is now implemented; see [SamusSourceIoMorphBall.md](SamusSourceIoMorphBall.md).
Next: material repaint/PBR, texture tiers/compression and Android
performance acceptance. LOD1 shares body textures but does not solve the existing
~137.3 MiB combined biped/cannon uncompressed estimate or CPU image-copy overhead.

## Current material milestone

The final material pack preserves this geometry milestone. See
[SamusSourceIoMaterials.md](SamusSourceIoMaterials.md) and
`samus-hd-kit/sourceio-materials/MATERIALS-RESULT.md` for compact UV domains,
all six native suit colors, companion maps, acceptance and current rollback.
