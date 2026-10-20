# CW-10b — independent affine pose handoff

This slice proves and supplies the pose-copy/math boundary before game physics
integration. The shipping game/server still do not reference the experimental
assembly. Only the diagnostic tool gains that reference. All native profiles
continue to report PhysicalQualified=false. Collider fitting, static export,
actual body draw handoff and full scene/life ownership remain subsequent CW-10
work; the new API is not death authority or a physical skinning qualification.

`CorpseAffineBinding.TryCapture` copies the actual composed affine node matrices
and node-to-body mapping. It stores only private matrix/byte arrays, with no
model, node, palette, actor, asset or engine reference. Capture every LOD
independently: Samus/Kanden static metadata differs between tiers, and legacy
Node.Transform is not the character AnimateNodes bind transform.

For initial native world matrix N and initial rigid body frame B, the stored
residual is N*inverse(B). Future output is residual*B(t), with no additional root,
parent or world multiplication. World translations are subtracted before rotating
to reduce cancellation. Copied body quaternions are normalized consistently at
capture/sample. The complete native linear matrix is retained; it is never
replaced by a rigid rotation decomposition. This preserves nonuniform scale,
shear and zero-scale hidden bones. Native Trace/Noxus animation samples include
approximately0.706 scale, so this distinction has actual asset coverage.

Input is bounded to1–128 nodes, exactly15 finite body poses, a complete mapping,
affine matrices (no perspective), bounded world positions/linear rows and local
bone-to-body offsets within4 units. All sampling results are validated in bounded
stack scratch before writing caller storage. Invalid later nodes cannot leave a
partially written palette. Successful capture defensively copies inputs; samples
are independent, allocation-free and safe for concurrent readers. The owner still
must bind accepted scene/actor/life/timeline and own each corpse's output storage.

`CorpseHumanoidMap` maps a bounded, unique named tree to the reference fifteen
part indices without changing skin weights or fabricating native joints. Complete
legs, shoulders/elbows and head/spine are required. Missing wrists use forearms;
Pelvis can remain virtual. Trace/Noxus share a Collar above head/shoulder helper
branches. The shared helper remains torso-owned; exclusive Neck/collar branches
follow their head/shoulder group. This is explicitly tested. A known layout is
not permission to enable physics or accept untrusted model/package content.

The native CPU fixture captures all14 actual native rigs across26 animation banks
at first/mid/last frames. Fifteen independently oriented frame carriers are placed
near the current actual joints; these are diagnostic frames, not collider fits.
It checks immediate pose preservation, common rigid covariance, isolated head
motion, native matrix/palette invariance, caller mutation and later native
animations not overwriting an earlier corpse-owned capture. No extracted asset
is committed. No native or HD GPU rendering is claimed in this slice.

Run the standalone checks with the existing explicit pilot flag. Run the native
CPU fixture with `tools/custom-weapons-check --corpse-affine-native <paths-dir>
<output-dir>` through dotnet and an isolated PROJECT_PRIME_USER_DATA. The separate
Android pilot links the same synthetic math/map tests before its shared benchmark;
it packages no native Hunter data and cannot qualify native rendering on Android.
See [evidence](evidence/CW-10b.md) for actual results and timing limitations.
