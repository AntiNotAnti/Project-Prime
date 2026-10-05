# Samus SourceIO LOD0 milestone

The current local production authoring kit is `samus-hd-kit/sourceio-lod0/`.
Its full report is `SOURCEIO-RESULT.md`; production Blender file is
`Samus_SourceIO_LOD0_Native17.blend`. Generated art/Blender kit output is ignored
by Git under the existing repository policy.

Source: `converted-sourceio/models/samus/samus_a.glb`, selected body and armed
Arm1 bodygroup. Original selected topology: **9,272 vertices, 10,106 triangles**.
The unneeded alternate arm is excluded. No subdivision/remeshing was applied.

50 Source bones collapse onto the native Samus **17 deform joints**, preserving
2,087 vertices with multiple distinct mapped influences. Native rest matrices
and hierarchy are unchanged. Alignment is a height-derived global conversion
plus documented baked bind-pose corrections, including a native muzzle fit.

Four runtime identities retain Source base colors, normals and light artwork:
armor → body, light → body_full_bright, ArmCanon1/3 → Gun, ArmCanon2 → GunTip.
Periodic texture atlases preserve original texels and UV islands; original UVs
also remain in a separate Blender layer. Export uses the unchanged generated
`prepare-biped-weighted4.py` and a biped-LOD0-only manifest.

Final GLB SHA-256:
`e39f730cc72220178ece489b1fdb2d5e8d46e33b7cd204cc8fbec1ca0b4177b1`

Validation and independent source/rig/weight/UV/bind audit passed. Metal bot-scene
acceptance submitted **1,671 eligible biped frames**, with every native joint
palette checked and no missing packets. Real launcher/match/Leave Match return
passed **124/124 preview frames**, without a block fallback. Captures and JSON
are in the local kit's `gameplay-acceptance/` and `launcher-return/` directories.

The acceptance command now checks only material channels actually authored in
each primitive, supporting partial companion-map sets. The launcher-return
harness avoids a redundant Offline navigation click when Leave Match already
restores that panel.

Rollback to the pre-conversion live pack:
`python3 samus-hd-kit/sourceio-lod0/install-pack.py --rollback`.

LOD0 v1 is now frozen in a read-only local release snapshot. Its separate
polish duplicate was inspected in 18 native motion views; no body geometry
change was warranted. The Source first-person RigidNodes cannon is now
implemented; see [SamusSourceIoViewModel.md](SamusSourceIoViewModel.md).
Source LOD1 is now implemented; see [SamusSourceIoLod1.md](SamusSourceIoLod1.md).
Source Morph Ball is now implemented; see [SamusSourceIoMorphBall.md](SamusSourceIoMorphBall.md).
Final material remaster and Android device acceptance remain later work.
The native freeze shell is accepted for now. Desktop texture residency estimate is
94.7 MiB per scene; mobile atlas/memory optimization is still required.

## Current material milestone

The final material pack preserves this geometry milestone. See
[SamusSourceIoMaterials.md](SamusSourceIoMaterials.md) and
`samus-hd-kit/sourceio-materials/MATERIALS-RESULT.md` for compact UV domains,
all six native suit colors, companion maps, acceptance and current rollback.
