# Samus Source first-person cannon

Local production kit: `samus-hd-kit/sourceio-viewmodel/` (generated art remains
ignored by Git). Authoring file: `Samus_SourceIO_FirstPerson_Rigid.blend`.
Detailed asset/acceptance report: `VIEWMODEL-RESULT.md`.

The installed pack combines the unchanged Samus SourceIO Weighted4 LOD0 v1
with a dedicated **RigidNodes** cannon derived from Source `samus_a.glb`.
Only ArmCanon surfaces are selected; organic arm geometry is excluded. Native
closed `SamusGun` idle frames establish fit, local axes and animation targets.

The cannon has **2,263 vertices / 2,045 triangles**: eight Source shell sections
(Main, three rings, four nozzle petals) and nine native core/glow sections.
Native ring/nozzle surface cuts interpolate Source UVs. Small internal hinge
connectors address the observed narrower Source petal roots during missile
opening. Native effect material identities and gameplay emitter offset remain.

The generated rigid Blender helper is unchanged. Authoring frames are
conjugated into Blender axes so glTF Y-up export yields native node-local
coordinates. Independent native-frame reconstruction agrees with saved mesh
positions within 2.43e-6 game units. Fitted barrel-rim center error is 8.10e-9.

GLB SHA-256:
`ed6664d15e2c42021cdb9e93862779699f26c14e5b8621b538bde22a8b733b4d`

## Renderer contract

`CharacterEmbeddedMaterialLoader` shares the existing bounded embedded
PNG/JPEG, UV0, wrapping and factor contract between Weighted4 and RigidNodes.
Rigid primitives retain albedo and optional maps; scene compilation reserves
base textures first and deduplicates shared material uploads across sections.
Native-texture rigid packs remain supported. Status/cosmetic overrides retain
priority, and advanced materials gate companion-map submission without
invalidating resident bindings.

All rigid draw paths (player, launcher preview, halfturret) carry authored
texture UV/wrap identity. Player camera-attached charge/muzzle particles with a
presentation override now share the viewmodel projection as well as its
late-latched transform; ordinary world effects keep the world projection.

## Acceptance

```
ProjectPrime -charactermodelvalidate /absolute/path/to/starter -noupdate
ProjectPrime -viewmodelacceptancecheck 'TEST ARENA' -output /absolute/path/to/output -debuglog -noupdate
```

The diagnostic uses a real Metal bot scene and synthetic scene keyboard input,
independent of keyboard focus. It checks native rigid transforms, shared Source
atlas bindings, map toggles, native viewmodel projection, scaled world FOV,
firing, charging, missile animations and Imperialist zoom. It schedules
additional draws at 90/120/240/540 Hz and checks gun/muzzle presentation
alignment; this is not a physical display latency measurement.

Final first-person acceptance: **2,520 frames**, including **990 scheduled
high-refresh pictures**. Charge runs at FOV 60/78/120 each observed 113 frames
of cannon-projected particle output. Independent fit/export audit, generated
validator, embedded-material regression and frame-timing checks passed.
The combined pack also passed 1,671 eligible biped frames with zero failures
and 124/124 real launcher-return preview frames with zero fallbacks.

An asleep macOS display caused GLFW to return no primary monitor. Waking the
display before diagnostics worked around it; the new diagnostic explicitly
resolves its monitor and reports a managed failure if none exists. This does
not claim to fix general macOS input/focus behavior.

## Production boundary

LOD0 v1 is read-only with a separate polish duplicate. The biped GLB remains
byte-identical to the accepted SourceIO milestone. Rollback to that biped-only
pack: `python3 samus-hd-kit/sourceio-viewmodel/install-pack.py --rollback`.

Source LOD1 is now implemented; see [SamusSourceIoLod1.md](SamusSourceIoLod1.md).
Source Morph Ball, material repaint/PBR and Android acceptance are next.
Two 2048² cannon maps add ~42.7 MiB with full uncompressed RGBA mip chains;
the combined biped/viewmodel estimate is ~137.3 MiB per scene, before supporting
native textures. This is an estimate, not measured compressed residency.
Atlas repetition, compressed texture tiers and streaming/release behavior
still need work before converting the full roster. Native freeze overlay
remains accepted for now.

## Current material milestone

The final material pack preserves this geometry milestone. See
[SamusSourceIoMaterials.md](SamusSourceIoMaterials.md) and
`samus-hd-kit/sourceio-materials/MATERIALS-RESULT.md` for compact UV domains,
all six native suit colors, companion maps, acceptance and current rollback.
