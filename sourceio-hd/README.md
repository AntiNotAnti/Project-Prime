# SourceIO hunters → native Project Prime Weighted4

Pass A converts **third-person LOD0 bipeds**. Source geometry, UVs and smooth
weights remain the artwork foundation. Native MPH rigs, animation and muzzle
positions remain authoritative. All seven hunters now have configurations.

Generated assets are local, under `artifacts/sourceio-hd/`; source game assets,
textures and Blender files are not shipped in this directory.

## Dependencies

- Current ProjectPrime desktop build with character kit/validator commands.
- Blender with glTF import/export; Blender 5 needs the COLLADA Support extension
  for the generated native reference (already installed in this workspace).
- Python 3 with Pillow and NumPy for texture assembly. No SourceIO addon is needed for
  an already converted GLB.
- Locally supplied `converted-sourceio/` assets, matching each config's SHA256.

From the repository root, using your Pillow-enabled Python:

```bash
python sourceio-hd/common/pipeline.py build \
  --config sourceio-hd/spire/config.json \
  --source-root /path/to/converted-sourceio \
  --output /absolute/path/to/artifacts/sourceio-hd/spire

python sourceio-hd/common/pipeline.py accept \
  --config sourceio-hd/spire/config.json \
  --output /absolute/path/to/artifacts/sourceio-hd/spire
```

`build` generates the native kit, imports and bakes its reference pose once,
imports selected source bodygroups, fits each collapsed joint frame, assembles
materials, exports with the **unchanged generated Weighted4 exporter**, audits
and runs `-charactermodelvalidate`. A frozen output refuses rebuilding.

`accept` validates and temporarily installs an individual one-entry pack. It
runs the real scene simulation/render harness with the configured hunter,
then **restores the entire previous live pack even on a game-check failure**.
Saved graphics settings and launcher choices are restored byte for byte.
Archive the prior acceptance output and snapshot, or use a fresh build output
directory for another acceptance attempt: snapshots are never overwritten. Diagnostics are serialized with a filesystem lock.

Review the captured images for intersections/deformation; packet assertions
cannot establish that every surface is clipping-free. After that review and
successful acceptance, merge into the full roster:

```bash
python sourceio-hd/common/pipeline.py merge \
  --config sourceio-hd/spire/config.json \
  --output /absolute/path/to/artifacts/sourceio-hd/spire \
  --snapshot /absolute/path/to/rollback-before-spire

python sourceio-hd/common/pipeline.py rollback \
  --config sourceio-hd/spire/config.json \
  --output /absolute/path/to/artifacts/sourceio-hd/spire \
  --snapshot /absolute/path/to/rollback-before-spire
```

`merge` requires acceptance of the **same GLB hash**, no packet failures,
complete scripted state coverage and 60/60 Weighted4 preview frames before
and after the match. Existing entries and mobile-model paths are preserved.
Directory replacement is transactional; the snapshot also retains files that
were not referenced by the manifest. Rollback verifies every restored file.
Merge also requires a successful `visual-review.json` naming the same
`modelSha256` as the audited and accepted GLB.

For isolated acceptance, point `PROJECT_PRIME_USER_DATA` at a test data directory
with copied settings/native paths and maps. Pass that directory with `--userdata`
and its `character-models/default` path with `--live`. The harness installs only
there; the normal launcher and live roster remain available. The two paths must
agree. Use one acceptance process at a time.

## Hunter configuration

- `config.json`: inspected source hash, selected GLB meshes and Blender
  bodygroups, counts, facing/root scale fit, per-native-joint bind anchors,
  chain endpoints, vertical silhouette limits, weapon/muzzle constraint.
- `bone-map.json`: every used Source bone → an eligible native palette joint.
  Collapsed weights merge and normalize. More than four influences rejects
  the asset instead of silently trimming it. Native bones are never renamed.
- `material-map.json`: every selected Source material → a native identity.
  Native lighting/emission/status behavior is retained; source albedo/normal
  artwork is embedded. A single-source material can preserve its original
  periodic UVs/texture. Multi-source groups use lossless periodic atlases.
  `compactPeriodicUVs` moves each triangle by integer UV tiles before packing,
  preserving periodic interpolation while avoiding unused repeated regions.
  `powerOfTwoAtlases: false` avoids rounding padding into oversized textures.
  Different color/normal resolutions use integer nearest expansion, retaining
  their source texels. The original UV layer remains available for audit.
  `teamRecolorMaterials` selects surfaces for native orange/green variants.
  `common/recolors.py` adds the existing `projectPrimeRecolors` material metadata
  and images after generated export, preserving all original binary bytes,
  base artwork, geometry and exporter. The final augmented GLB is independently
  audited and validated. Team acceptance asserts the variant bindings on every
  team frame. This deterministic color conversion is not a final art repaint.

### Original Source material layers

The converted GLBs contain base and normal images but omit Source VMT detail
node groups. `sourceMaterialRoot` and `sourceDecodedTextureRoot` enable
`common/source_materials.py` to recover the original material settings and
decoded detail images. Mode 0 detail is composited in linear color space with
its authored UV frequency, tint and blend factor. Original self-illumination
masks supply emissive images. Unsupported detail modes/transforms reject the
build. Integer repeat folding preserves the detail phase; the default 1024
dimension limit bounds the bake's sampling density. It is not a 4K repaint.

Authored constant Source Phong boost, tint luminance, exponent and normal/base
alpha mask also supply the existing specular/roughness map. The generated
exporter validates first; `common/material_maps.py` then appends the companion
image and binding while preserving all generated binary bytes. The runtime
reads glTF roughness G and converts metallic B to its specular-strength R.
This is a bounded approximation of Source's glossy response: RGB specular tint,
view-dependent Fresnel and HDR boost are not reproduced. Native lighting and
the renderer's highlight gain remain authoritative. The audit records these
limits; no base artwork is brightened to imitate a reflection.

`SOURCE-MATERIALS.json` records the original VMT/detail hashes, color spaces,
formula, image dimensions and any bounded emissive/specular intensity. The untouched
embedded source images remain under `source-original/`. Base, normal and
emissive/specular atlas images are checked against their exported embedded texels.

Bind fitting is baked into mesh positions before export. One global height,
facing and root transform establishes units; each native joint then has an
explicit translation/direction fit. The six reviewed configurations use
`sourceShapeChains` for connected limb fitting with rigid rotations and original
Source segment lengths; native joints supply bend hints and animation pivots. Shared
collapsed chains use a single frame, preventing helper-bone shell splits.
No automatic remesh, subdivision, decimation or post-export rescale occurs.

Spire's distal right-forearm crystal is the weapon landmark, fitted to the
native `Metadata.MuzzleOffests[Spire]` through `R_elbow`. The audit verifies
that this exported landmark is rigid to that joint. The gameplay harness
checks its skinned position against the authoritative native muzzle every
eligible frame, including aiming, firing and transitions.

## Acceptance evidence

- `audit.json`: native hierarchy/rest frames, topology, original and runtime
  UVs, collapsed weights, finite normalized ≤4 influences, inverse binds,
  native materials, unchanged generated references/exporter and muzzle fit.
- `validation.log`: native runtime contract/GLB validator.
- `acceptance/acceptance.json`, `frames.json`: movement, strafe, jumping,
  aim/turn/fire, injected damage/freeze/double damage, death/respawn,
  native alternate-form movement and return to HD biped, bright/team colors,
  material toggles, every primitive's complete native joint palette.
- `launcher-before.json`, `launcher-after.json`: Weighted4 packets on every
  preview frame; drawing a native/block fallback does not count as passing.
- Same-pose native/Source captures and state-specific captures for review.

Pass A intentionally keeps unconverted alternate forms, first-person weapons
and distant LOD1 native. Those are not eligible LOD0 fallback failures.
Android performance and final material repaint remain later milestones.

## First-person weapons

The six non-Samus hunters have separate `viewmodel-config.json` files and a
shared rigid conversion workflow. Samus's existing finished cannon and its
desktop/mobile assets are preserved. Use the same locally supplied Source GLBs:

```bash
python sourceio-hd/common/viewmodel_pipeline.py build \
  --config sourceio-hd/spire/viewmodel-config.json \
  --source-root /path/to/converted-sourceio \
  --output /absolute/path/to/spire-first-person

python sourceio-hd/common/viewmodel_pipeline.py accept \
  --config sourceio-hd/spire/viewmodel-config.json \
  --output /absolute/path/to/spire-first-person \
  --userdata /absolute/path/to/isolated-user-data \
  --live /absolute/path/to/isolated-user-data/character-models/default
```

`build` generates a fresh native kit. The kit now includes
`viewmodel-native-idle.json`, an engine-computed pose for native Power Beam
idle (animation 24), and the authoritative gameplay emitter offset. The Blender
reference's compact animation index 3 is not a runtime animation ID. The engine
pose retains native handling of unanimated nodes and parent relationships.
An existing **unlocked** kit can be supplied with `--kit`; it must contain this
pose file. Frozen biped kits and releases are never edited.

Configuration chooses the Source weapon/bodygroups, collapsed forearm weights
or right-hand-only body selection, material mapping, roll angle and native
effect exclusions. The selected Source geometry receives one baked axial/radial
fit to the native emitter. Its cross-section aspect is preserved. The six
reviewed configs use `sourceRigidNode` to attach the complete Source weapon
and selected hand to the native presentation root. This retains every Source
triangle, corner UV and split normal. Cutting a continuous Source mesh across
independently animated native mechanical nodes produced large artificial
cracks, despite passing packet and rest-pose surface checks. Those cuts are
no longer used by these configs. No subdivision, remesh or decimation is applied.

The **unchanged generated rigid exporter** validates the native authoring rig,
groups and material identities. The resulting positions and UVs are retained
while authored Source corner normals are added. The generated helper accepts
native roots even when they have no original mesh; it still requires native
bone identities and rigid weights. Supplemental native gun meshes are excluded
because they intersect the differently shaped Source shells. The complete
Source gun follows native root motion and recoil; its shell does not reproduce
the original DS gun's mechanical opening animation. Engine particle charge,
muzzle and affinity effects retain native emitter transforms. Source lights
come from restored emissive maps. Albedo/normal images and orange/green team
variants use the existing material contract.

`audit` independently reconstructs the exported surfaces through the native
pose, checks normals, UVs, area, native effect colors and exact embedded image
texels, and runs the runtime validator. `accept` installs only the isolated
one-entry test pack and restores the whole previous pack and settings afterward.
The harness checks 3,090 eligible first-person frames, including 990 simulated
draw schedules at 90/120/240/540 Hz, normal/affinity fire and charge, missiles,
material toggles, six suit palettes, FOV 60/78/120 and Imperialist zoom. The
native and Source comparison explicitly selects the same raised native idle
pose; the recorded live native frames must agree with the authoring frames.
This is not a measurement of physical display latency.

Packet submission is only one part of acceptance. The texture/weapon review
also compares Source triangle corners independently, samples the generated
native gun animation frames for artificial seam separation, verifies the
physical weapon axis/tip, checks VMT detail composition against an independent
pixel reference, and inspects actual gameplay captures. Evidence is under
`artifacts/sourceio-hd/material-weapon-review-v2/`. The normal-map derivative
guard is relative to UV scale in desktop, ES and generated Metal shaders, so
small atlas UV regions do not incorrectly disable valid normal mapping.

Inspect the captured states, then record `visual-review.json` with `pass: true`,
the accepted `modelSha256` and specific observations. `merge` requires that
review and matching configuration, audit and acceptance hashes, plus an explicit
`--live` and new `--snapshot`. `rollback` restores that snapshot. The commands
use the same arguments as above. Freeze accepted output with:

```bash
python sourceio-hd/common/viewmodel_freeze.py \
  --config sourceio-hd/spire/viewmodel-config.json \
  --output /absolute/path/to/spire-first-person
```

These six weapons use the original Source artwork. Their final material repaint
and Android texture/performance acceptance remain separate work; Samus's
existing optimized texture tiers are retained.

`common/review.py` assembles recorded captures for inspection. After recording
`visual-review.json` and the result report, `common/freeze.py` creates a readonly
local `releases/lod0-v1/` snapshot with per-file hashes and locks the build output.

### Native inherited scale

Noxus and Trace have a 0.7060547 scale on their native root. Blender edit-bone
matrices cannot retain that scale. `native.py` saves the full reference pose
separately, and `binds.py` corrects only exported joint transforms and inverse
binds after the unchanged generated exporter validates the authoring rig.
Mesh positions, topology, weights, UVs and embedded artwork remain unchanged.
The default GLTF joint pose and inverse binds must both match that full bind.
The independent audit also reconstructs the native pose from the generated
node inventory. The gameplay harness checks inverse binds against native
scale/rotation/position using the character animation transform order.
A packet-only check is insufficient to detect a supplied incorrect bind.

The source core silhouette can be preserved with `preserveSourceShape`, while
limbs are fitted to native animation endpoints and the weapon to its exact
native muzzle. `nativePivotOffset` records any source-preserved offset.
Source bone helpers are collapsed explicitly in each hunter's bone map.


### Source feature fidelity (LOD0 v2)

The six non-Samus configurations also preserve authored Source split normals.
`preserveSourceNormals` transforms each original corner normal through the baked
fit, retains sharp/smooth boundaries, and audits the authoring and exported
normal vectors independently (one-degree Blender storage tolerance).

`sourceShapeChains` uses connected two-segment fits with uniform scale and rigid
rotation. The Source core, limb cross sections, segment lengths and complete
feet/claws remain available in the replacement. Foot contact derives from the
Source sole. The weapon tip still meets the authoritative native muzzle;
necessary pivot/root offsets are explicit in `retarget-report.json`. The audit
checks orthogonality and shared chain endpoints in addition to native inverse
binds. This is a rest-fit change; the runtime keeps native animations and
normalized original collapsed Weighted4 weights.

`preserveSourceMaterialAlpha` separates Source transparent triangle subsets
using their disjoint atlas UV regions. `common/material_contract.py` preserves
attribute/image bytes and the total triangle inventory, and gives the subset
its original alpha mode under the same native material identity. Weavel's 682
hair triangles retain BLEND; its remaining body surfaces retain OPAQUE, which
ignores Source gloss stored in albedo alpha. Source material double-sided flags
now reach both rigid and Weighted4 preview/game render packets.

Add `--fidelity` to `pipeline.py accept` to request eight frozen-pose launcher
angles with companion maps off/on, plus texture-free geometry views (24 images).
The CLI equivalent is `-characterfidelity` alongside
`-characteracceptancecheck ROOM -hunter HUNTER`. Gameplay acceptance additionally
checks double-sided packet culling and authored transparent submission.

Weavel's Source Weapon bodygroup selects Handgun.smd or Scythe.smd. Its armed
LOD0 selects Handgun; showing both Source variants together is not the intended
armed bodygroup. Alternate-form/scythe/halfturret work remains in Pass B.

Accepted v2 builds freeze with `common/freeze.py --release-name lod0-v2`,
preserving earlier readonly v1 releases. Local comparisons and exact signed app
roster evidence are under `artifacts/sourceio-hd/fidelity-review/`.
