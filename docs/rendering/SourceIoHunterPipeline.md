# Configured SourceIO hunter conversion

The reusable converter lives in [`sourceio-hd/`](../../sourceio-hd/README.md).
It covers third-person LOD0 bipeds: Source geometry/UVs and embedded textures,
generated native rig/animation, collapsed normalized Weighted4 influences,
native material identities and muzzle alignment.

All seven hunters have configurations. Spire passed individual-pack,
merged-roster and signed Mac app acceptance: 12,895 triangles, 13 native joints,
2,503 smoothly blended vertices, 1,665 eligible gameplay frames with no packet
failures or fallback, and 120 successful launcher preview frames per run.
Existing Samus desktop/mobile assets and entries were preserved; its merged
roster regression passed 1,671 eligible frames. A fresh native Samus kit also
reproduced the prior conversion's topology and weight histogram through the
shared pipeline.

Each hunter stays in a one-entry test pack until its exact GLB hash passes
validation, scripted acceptance and image review. The merge command snapshots
the complete current pack and preserves existing mobile paths. See the README
for build, acceptance, merge, rollback and readonly release commands.

Local evidence is under `artifacts/sourceio-hd/spire-v1/SPIRE-RESULT.md` and
`FINAL-CHECKS.json`; `releases/lod0-v1/` freezes the accepted baseline. The
installed Mac bundle includes the authored-UV guard fix needed for Spire's
embedded crystal material. Restart any already-running older game process.

Spire retains native LOD1, first-person weapon and alternate form in Pass A.
Android certification and final repaint remain subsequent milestones. Screenshot sampling does not establish every surface is clipping-free.

## Full Pass A roster (2026-10-05)

Noxus, Kanden, Sylux, Trace and Weavel are now validated, accepted and installed
as third-person LOD0 bipeds with embedded Source color/normal textures and
native team variants. The signed installed Mac app passed a complete-roster
check for all seven hunters, including unchanged Samus and Spire regressions.
Each new hunter also passed an individual one-entry pack before merging.

Noxus and Trace inherit root scale 0.7060547. Blender edit bones drop that
scale, so the shared converter records full native bind matrices and corrects
exported joint/default-pose and inverse-bind matrices after the unchanged
generated exporter validates the authoring rig. Mesh/image payloads are not
changed by that correction. Independent audit reconstructs the native node
transforms; runtime acceptance verifies inverse binds against native rest
values using the character animation transform order. This fixed Noxus's
thin limbs and detached knee appearance. Source core silhouette is preserved
while limbs fit native animation endpoints; all fit offsets are explicit.

Local evidence: `artifacts/sourceio-hd/roster-pass-a/ROSTER-RESULT.md`,
`full-roster-checks.json`, `INSTALL-RESULT.json`, and each hunter's
`<HUNTER>-RESULT.md`. Each new baseline is frozen in `releases/lod0-v1/`.
The pre-addition pack snapshot is `roster-pass-a/live-merge-snapshots/noxus/`,
and the prior app is `roster-pass-a/rollback-installed-app/Project Prime.app`.

New hunters retain native LOD1, viewmodels and alternate forms in Pass A;
Weavel retains its native halfturret. Source artwork is not a final repaint
and no Android device certification was performed for these conversions.


## Source feature fidelity review (2026-10-05)

The six non-Samus LOD0 configurations now retain Source split normals and
connected Source limb shapes, using native animation pivots and bend hints.
Earlier per-segment stretching flattened distinct lower-leg/foot and arm
features. Spire and Weavel now also preserve the original core silhouette.
Audit adds uniform limb scale/rotation and shared-anchor checks; native joint
rest transforms, generated exporter and original normalized collapsed weights
remain authoritative.

The renderer also honors GLB `doubleSided` on embedded rigid/Weighted4 surfaces.
Weavel's Source hair BLEND subset is preserved separately under its existing
native body material identity; opaque Source gloss alpha still remains opaque.
The alpha split adds index/material metadata while retaining geometry/image
attribute bytes and total triangle inventory.

`-characterfidelity` adds 24 frozen-pose launcher captures at eight angles,
including companion maps off/on and neutral texture-free geometry. This helps
compare anatomy with Blender Solid references independently of dark albedo or
Source/native lighting differences. The gameplay harness asserts culling and
transparent packet submission as well as animation, native binds and muzzle.

Local evidence: `artifacts/sourceio-hd/fidelity-review/FIDELITY-RESULT.md`, the
six `<hunter>-v2/` builds and their readonly `releases/lod0-v2/` snapshots.
These visual samples establish reviewed feature presence and improved fit;
they do not certify every animated surface against self-intersection or replace
Android, LOD1, viewmodel, alternate-form and final material work.

## First-person roster milestone (2026-10-05)

All seven hunters now have Source-derived first-person weapon replacements in
the installed 16-entry `sourceio-hd-roster-v3` pack. Samus's completed cannon
and all existing biped/mobile assets remain unchanged. The six new weapons use
configuration-driven RigidNodes conversion, engine-native idle/emitter fitting,
Source corner UV/normal preservation and reviewed native mechanical cuts.
See `SourceIoFirstPersonWeapons.md` for the renderer contract, acceptance
scope, evidence and rollback paths. Non-Samus final material repaint and new
Android weapon tiers remain subsequent work.
