# Original credits CPU widget sampling

This SH-08B slice evaluates admitted model/animation bytes into detached flat
triangles. It is an internal CPU component, with no original frontend route,
GPU resource, texture-pixel cache, Scene, source-file reopening, audio, save or
timeline ownership. Target main is `3dd5a0b833e67e43d840997c6a30b475fad5e017`
(PR833). Reference behavior remains Second Hunt41452f2 MenuWidgets and the
existing engine's Model/LegacyGeometryBatch. Protocol69 is unchanged.

## Implementation

`CampaignCreditsWidgetSampler` constructs one private ModelInstance through
the admitted exact-byte adapter, then decodes static mesh vertices once.
Each mesh resets position/UV/matrix state. Signed native VTX16/VTX10 and
partial XY/XZ commands preserve carried axes; normalized signed texcoords use
the actual texture dimensions. Untextured materials retain UV0. No normal
lighting, ambient/specular addition or world shader is introduced.

The existing pure `LegacyGeometryBatch` expands quads and quad strips with
source winding, including degenerate triangles. Source admission already
bounds all instructions/counts and proves mesh/list/node ownership. Final
vertex/triangle totals must match that admission. The sampler does not clone
or replace the engine's triangle-expansion implementation.

Sampling belongs to the creating thread, accepts explicit nonnegative long
age and finite0..1 caller alpha, and reduces frame age before narrowing toint.
Material/texture animation, static node matrices, node animation and matrix
stack evaluation reuse existing Model calls in their original order. Group
frames are selected independently. Row-vector stack transformation precedes
uniform model scale; no item translation, projection or Y inversion occurs.

Current diffuse and material/caller alpha supply flat vertex color. A current
zero-alpha mesh is omitted only for that sample; a static zero-alpha mesh is
retained for later animated visibility. Source texture identity includes the
model SHA, texture and palette, with separate wrap axes and explicit(-1,-1)
untextured identity. Static TexgenNone transforms remain ignored as in the
reference. CPU output contains no driver texture ID or mutable Model state.
Every published position/UV/depth/color is finite, counts stay bounded and
immutable value records/read-only wrappers detach earlier samples from later
model mutations. Failure publishes no partial snapshot.

## Qualification

Frozen source `3821154f7af628d9b95e336e004cf1f99b11442f` passed all21
serial commands. Both archives retain all5,991 tracked files unchanged.
Desktop/dedicated/credits-tool Release builds have zero warnings/errors.
Android Release/AOT has25 existing warnings, zero errors,119 AOT libraries
on each arm64-v8a/x86_64 ABI and valid v2/v3 APK signatures.

Tests pass90 independent numeric widget cases plus all prior credits fixtures
(239 source/87 duration/89 timeline/158 model/139 binding/48 decoder/798 bitmap/
104 text checks and2,000 corruptions). Historical replay, movie lifetime,
ending, presentation, post-match save and timing regressions pass.

Read-only owned CPU sampling passes6,813 checks across26 private samplers:
1,294 samples including all1,156 animation frames +8 still frames,
19,848 triangles/59,544 corners, maximum40 visible triangles per sample,
26 ageA/B/A checks and1,268 earlier-snapshot stability checks. The separate
source/timeline/model/font/background/text probes also pass. Source hashes
and existing caches remain unchanged. This is bounded geometry/evaluation
acceptance, not independent original coordinate/pixel parity or GPU/story/
device acceptance.

Desktop DLL SHA256:
`cd767b1d31f98f3fb07d60fcf26689eeef63f35166654dff26110de5e85426ea`.
Dedicated DLL:
`4aa2219385a9791aa0c765275e0ee178dc5261968255e7b520bc1aed3c442332`.
Credits tool:
`8fee756708a6c2647da0a0b457c377e768a5c248b41dc3674ec14120b3d41974`.
Signed APK:
`2e65ee32fd6330e12bbe3b2b2f938b2896d178bc8759f81f769cf39a6d5164c6`.

Receipts: `artifacts/sh-integration/credits-widgets-3821154f7`. Tests scrub
inherited Project Prime overrides and remove their temporary contexts.
`--probe-widgets OWNED_MPH_ROOT` is opt-in metadata-only source acceptance.
Windows SSH authentication remains unavailable; Mac fallback is authorized.
Physical Android and Windows/Linux runtime remain unqualified.

Concurrent CW-05e merged as `a2cea73e876f4121aed042a4b918817eb096c1b4`
during qualification. Its HUD/scope/input changes touch none of this slice's
production or test files and do not change protocol69. That concurrent change
is outside the frozen whole-binary proof; this document does not claim its
qualification. Credits-file identity is checked again after merge.

## Remaining and rollback

A catalog/frame owner still must enforce aggregate instance/geometry/frame
limits, decode and deduplicate bounded texture/palette pixels, bind generated
result strings and preserve document/root/receipt epochs. Per-model PixelCount
alone does not bound RGBA for multiple palettes; this sampler deliberately
owns no such decoding/cache. Native viewport geometry/texture submission,
sampler/premultiplied-color policy, retirement, audio transitions and Android
ending ownership remain implementation work. The original frontend remains
unregistered. Rollback removes the unused sampler/probe flag without changing
current campaign presentation or simulation.
