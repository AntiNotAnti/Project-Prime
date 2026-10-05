# Samus Source material milestone

This document records the frozen desktop material baseline. The subsequent
[mobile texture milestone](SamusAndroidTextures.md) adds an Android tier,
compressed upload and unused-color eviction to the installed mixed pack.

The installed material pack is `samus-sourceio-final-materials-v1`. Detailed art,
acceptance, image-generation provenance, allocation accounting and rollback are
in `samus-hd-kit/sourceio-materials/MATERIALS-RESULT.md` (generated kit, ignored by
Git). Prior LOD0/LOD1/cannon/Morph authoring files and releases remain unchanged.

## Material contract

Each Source UV domain becomes its own primitive, retaining the native material
name. Repeat UV0 replaces the previous repeated atlas regions. Positions,
normals, joint indices, weights, skin matrices and native transforms remain
byte-identical per triangle to the prior validated pack. Counts remain 10,106
LOD0, 5,052 LOD1, 2,045 cannon and 2,060 Morph Ball triangles.

The optional GLB material metadata is:

```json
{
  "extras": {
    "sourceMaterial": "Samus_Armor",
    "projectPrimeRecolors": {
      "1": { "index": 3 },
      "2": { "index": 4 }
    }
  }
}
```

Base albedo is native suit 0. Optional variant keys are unique native indices
1–5; texture indices use the regular GLB texture table. Embedded bounded PNG/JPEG,
UV0, no texture transform extensions and matching sampler wrapping are required.
`sourceMaterial` is authoring metadata. Rigid and Weighted4 runtime selection,
player/turret/launcher draw paths use the native recolor index; native status and
custom cosmetic binding overrides retain priority.

Variants upload lazily and share scene image allocations by content plus upload
semantics. Companion maps are shared across colors. Scene unload and texture
quality/sampling invalidation release these bindings. Admission failure retains
base albedo. All variants currently stay allocated until scene cleanup.

This engine uses its existing normal/specular shader, not a complete metallic
PBR BRDF. Packed GLB G is roughness, B becomes legacy specular strength; runtime
maps use R/G. Native muzzle/glow effect primitives remain native. Advanced
Materials controls companion submission; base colors/variants work with it off.

## Art scope and acceptance

Main armor/cannon albedos use registered built-in image-generation edits plus
Source detail and restrained procedural surface work. Inputs returned at 1254²;
shipping main body is 2048², cannon 1024². A derived 4096² body authoring master
exists, with no claim of an independently detailed hand-painted 4K sheet. Source
mechanical normals are retained. Roughness/specular/emissive masks are authored.

Generated validators and character fixtures pass. Metal acceptance: 2,958 biped
frames, 2,084 ball frames, all six native palettes, team/bright/status/transitions
and map toggles, zero packet failures. LOD regression: 1,911 LOD1 + 180 LOD0
frames, 16 switches. Cannon: 2,880 total draws, including 990 scheduled high-refresh pictures,
FOV/Imperialist zoom/muzzle/charge/colors passed. Launcher return and 24 clean
material views passed; visual review is separate from packet checks.

Default scene allocation estimate drops from 94.67 to 63.58 MiB; visiting all six
colors reaches 214.0 MiB. No driver/CPU memory or Android benchmark is claimed.
JPEG saves disk only. Mobile tiers, compressed texture loader support, variant
streaming/eviction and actual Android profiling remain next-phase work before a
roster rollout.

Useful diagnostics:

```sh
ProjectPrime -charactermaterialpalette -output native-colors.json
ProjectPrime -charactermaterialacceptancecheck 'TEST ARENA' -output material-check
ProjectPrime -lod1acceptancecheck 'TEST ARENA' -output lod-check
ProjectPrime -viewmodelacceptancecheck 'TEST ARENA' -output cannon-check
```

Use the kit's `run-check.py` wrapper to preserve settings and actual launcher
preferences. Frozen hashed snapshot: `sourceio-materials/releases/materials-v1`.
Rollback: `python3 samus-hd-kit/sourceio-materials/install-pack.py --rollback`.
