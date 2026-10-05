# Samus SourceIO Morph Ball

Local production kit: `samus-hd-kit/sourceio-morphball/` (generated art remains
Git-ignored). Authoring: `Samus_SourceIO_MorphBall_Rigid.blend`; evidence:
`MORPHBALL-RESULT.md`, `audit.json` and the acceptance/capture folders.

The installed four-entry pack `samus-sourceio-lod0-lod1-cannon-and-morphball`
adds `samus_b.glb` as a native alternate-form RigidNodes replacement. Existing
LOD0, LOD1 and first-person cannon bytes remain unchanged.

## Asset contract

- Original Source topology: 2,058 triangles / 1,913 records; no subdivision/remesh.
- Native glow plane: two triangles. Export total: **2,060 triangles / 1,919 records**.
- Native rigid mappings: `MorphBall`, `pPlane1`; three material primitives.
- One baked uniform fit to native radius 0.5110449787, scale 1.3116393358.
- Armor → `SamusAlt_refM`, stripe → `SamusAlt_stripeM`, glow → `SamusAlt_glowM`.
- Existing Source body/light atlases are reused byte-for-byte with converted UVs.
- Glow uses its native binding and spherical billboard, independently of the shell.
- Generated rigid export helper unchanged. Validator passes all four pack entries.

Independent audit verifies exact Source topology/original UVs, normalized rigid
weights, local-axis reconstruction (maximum error 4.2147e-8), unchanged atlas bytes
and intact prior production assets. Runtime physics, rolling, bomb positions,
trails, death effects and the accepted freeze shell remain native.

The native reflection material uses Normal texgen. The rigid compiler now allows
its identity only when embedded albedo supplies authored UVs; the existing packet
submission chooses Texcoord mode for that binding. Native-bound unsupported
texgen remains rejected. Status bindings retain their existing priority.

## Acceptance

```
ProjectPrime -morphballacceptancecheck 'TEST ARENA' -output /absolute/output -noupdate -debuglog
```

The diagnostic runs real Metal simulation/render and focus-independent controls.
It checks every eligible ball primitive, node/glow transform, billboard, authored
UV/material toggle and shared texture allocation, alongside biped joint palettes.
Actual boost, alternate-attack bombs, bomb jumping, airborne morphing and ball
death/respawn must occur. Damage/freeze/double-damage states are injected.

Final run: **1,594 rigid ball frames + 2,188 Weighted4 LOD0 frames, zero failures**.
Preview scene before/after drew 60/60 each. LOD1 regression passes 2,091 frames and
16 native distance switches. Cannon regression passes 2,520 frames, including
990 scheduled high-refresh pictures. Loader/helper and frame-timing checks pass;
release build has zero errors and 97 existing warnings.

Reviewed authoring views, gameplay/contact sheets and a same-pose native/Source
comparison. Bright/team packets are checked with damage flashes suppressed;
team visuals use High Contrast Textured mode. Regular Textured bright mode keeps
Source artwork colors; authored team albedo variants remain final material work.
Sampled review is not exhaustive clipping/camera coverage or physical display
latency testing. The native ice shell remains accepted.

Loading the ball adds no character texture allocations: **99,265,191 bytes before
and after (~94.7 MiB)**. This is manager accounting, not GPU-driver residency.
CPU image copies, atlas repetition/compression and Android/device performance
remain pending; the earlier combined biped/cannon estimate remains ~137.3 MiB.

## Install and rollback

`python3 samus-hd-kit/sourceio-morphball/install-pack.py` validates, snapshots the
previous LOD0/LOD1/cannon pack and replaces the combined manifest last. `--rollback`
restores that three-entry pack. The local `run-check.py` restores saved settings
and launcher choices; high-contrast test settings are not left enabled.
The accepted complete release is frozen under local `releases/morphball-v1/`.

Morph GLB SHA-256:
`d39b3706de54f564ed060b6b9997ae6eb034acb9a1a7003aeb6296f2404e8e16`.

Next: material repaint/PBR, team albedo variants, texture tiers/compression and
Android performance acceptance. See [LOD0](SamusSourceIoLod0.md),
[LOD1](SamusSourceIoLod1.md) and [cannon](SamusSourceIoViewModel.md) for prior milestones.

## Current material milestone

The final material pack preserves this geometry milestone. See
[SamusSourceIoMaterials.md](SamusSourceIoMaterials.md) and
`samus-hd-kit/sourceio-materials/MATERIALS-RESULT.md` for compact UV domains,
all six native suit colors, companion maps, acceptance and current rollback.
