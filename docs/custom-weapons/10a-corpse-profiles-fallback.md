# CW-10a — native profiles and bounded fallback

This first CW-10 slice makes the CW-08 fallback explicit before any physical
handoff. It does not qualify or enable physical skinning. The shipping game still
has no reference to the experimental CW-09 assembly.

`CorpseRigProfile` records immutable native model/LOD identity, named head/spine
and leg nodes, actual palette count and whether the head is weighted. All seven
Hunters and both LODs are covered. A canonical SHA256 layout signature includes
length-framed UTF8 node names, signed parent indices and ordered palette-node
bindings. These signatures establish a known layout, not asset authenticity,
geometry quality or physical bind offsets. Profiles are weakly cached by model.
Native node indices, palette slots and fifteen-body physics indices remain
separate. Trace/Noxus do not require a fabricated Pelvis; Spire's head remains
explicitly unweighted.

The body pose scope refuses an unknown name, parent or palette mapping before
borrowing node matrices. Player prewarm requires both LODs to match the actual
Hunter and their tier. If either fails, both retain the original catalog/native
route; Classic extra-body visibility is disabled and nonzero death effects retain
their original root, surface, particles, duration and disappearance. No renderer
profile is marked physically qualified by matching names alone.

The existing cosmetic quality setting limits active head-death overlays to
Off/Low/Medium/High =0/2/4/8, within0/24/48/72 world units of the fixed-step camera.
The fixed-step pass ranks eligible overlays by squared distance, with slot as a
stable tie-break. It cancels excess/far overlays without changing health, the
accepted death cause or its time. Returning near or increasing quality cannot
restart an already consumed cause. Catalog effects keep their existing clocks;
retiring this skeletal overlay does not prolong or cancel the selected effect.
Candidate sorting uses two stack spans of eight values, with no persistent queue
or new physics allocation. Headless/thumbnail dispatch remains an early no-op.

The existing body submission still copies each RenderItem palette and restores
all composed nodes/palette in the same scoped draw. CW-10a does not claim new
multi-actor physical palette or modern/HD qualification. Native replay keeps the
existing accepted-fact reconstruction and timeline fences; no archive field or
wire changes occur. Moving-surface physical contact and exact physical replay
remain unsupported. Subsequent CW-10 work must capture actual composed affine
poses independently for each LOD, preserve scale/shear residuals, own corpse
palettes per full actor/life/context and validate copied static geometry. Legacy
Node.Transform is not an animation bind transform, and Samus/Kanden static bind
metadata differs between LODs; name equality cannot justify sharing bind offsets.

See [evidence](evidence/CW-10a.md). CW-10 remains in progress after this slice.
