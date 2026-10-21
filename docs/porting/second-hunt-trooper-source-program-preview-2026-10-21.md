# Prime Rev 2 Trooper source-program preview

All four verified Prime USA Rev 2 Trooper imports now open in the existing
animated source viewer with their complete source geometry, textures, material
programs and strict animation clips. They use the real Weavel HunterPreview
owner. This extends the existing importer, source-rig loader and Scene renderer;
it adds no second animation decoder, renderer or simulation clock.

This is a source viewer slice. Imported Trooper match actors, state-driven poses,
first-person weapons, ball/drone geometry, gameplay capabilities, multiplayer and
replay integration remain work. `LoadForLocalBody` explicitly refuses Troopers.
Existing native-fallback practice modes do not acquire imported presentation
through this change. The full integration plan remains incomplete.

## Immutable generated profiles

Rev 2 bundle v5/body v4 adds one required `characters.json` to the eleven
authoring components delivered in [PR945](https://github.com/AntiNotAnti/Project-Prime/pull/945).
All eleven old files remain byte-identical. Original Prime recipe v1 and Echoes
recipe v6 remain unchanged. Frozen v2, v3 and v4 receipts remain explicitly
valid under their old recipes and inspect as Outdated under the current recipe.
Inspection never migrates them. Missing/corrupt material or manifest components
cannot leave the current bundle Ready.

Four closed generated profiles independently pin the complete GLB, original
authoring provenance, animation bank, graph descriptor and raw material archive.
The provenance retains its original truthful authoring scope; its flags do not
self-grant actor or gameplay authority. The manifest admits only a Weavel LOD0
Weighted4 Biped, the matching canonical Trooper skin, no native bone map, no
mobile tier and no cannon. Every source file belongs to the same retained pack
generation and existing physical-root witness.

The shared imported-animation adapter preserves the real Prime root3/external
parent2 contract, all 35 joints plus the identity mesh node, 142 ANIM resources
and 167 raw roots. Only the 113 character-declared, strict, rigid primitive
clips appear in this viewer. Composite/named graph semantics remain unsupported;
no convenient leaf is substituted. Existing Echoes root0/parent97, manual
provenance and cannon admission retain their separate contracts.

## Shared GX compiler and Scene ownership

The compiler adds packed post selector3 (post slot1) and TEV color selector4
(C1, internal register2). Raw stage destinations remain the observed PREV/C2
subset. Dependency analysis tracks live C1 reads through earlier writes and
removes dead mix dependencies. CPU evaluation and generated GLSL120/300 share
the same mapping. Existing Echoes zero-register shader initializer bytes and
policy identity are preserved.

Power's closed profile explicitly initializes C1 RGB to `(253,239,86)/255`,
alpha zero; the other profiles keep explicit zeros. This follows the pinned
Second Hunt `CampaignRetroBody.GameRegister1` behavior at
`41452f2d053fcfa679e33c00a73d120a53f59e61` (MIT, Copyright2020 NoneGiven;
repository LICENSE retained). Finite unit-range validation precedes compilation.
The value is part of immutable shader text, rather than inherited driver state.
Trooper material policy is separate from the existing Echoes policy in captured
cache and texture residency identities.

The existing Scene owns programs, textures, draw passes and state restoration.
Its shader budget is bounded by the six closed inventories: at most 28 source
programs (Echoes7+9, Prime4×3), with identical generated shaders shared. Actual
all-six switching produces 12 distinct programs and 43 texture residents. Source
poses remain per instance, full-rig palettes gather into at most32 joints per
draw, original material ordinals and all source triangles are retained.

The shared stage now resolves the request's catalog native hunter instead of
always choosing Samus. Camera root compensation recognizes the exact Prime
root name as well as Echoes. Rejecting an attached clip/time/model retires its
source binding and records the failed owner/pack/part/appearance attempt, so
repeated draws cannot reload it. A fresh explicit viewer owner can recover.
No native bridge intent or ABI change is required.

## Frozen Windows verification

Final production freeze is `3bfef7faf4640cc76bdc8ea9a7d353df7eee3469`.
Test freeze is `bd93aab0d9e74aff755586566bfc2f05892de5b8`.
The branch includes exact concurrent main
`e893a052eec36218161ec383b3dab1b49983acb6` (PR946/package protocol81).
Our character slice adds no wire/replay fields. This qualification does not
claim unrelated later main changes or an external review/CI result.

Windows desktop and x64 dedicated Release pass with one existing CS0649 warning
and zero errors each. Test tools build with zero warnings/errors after correcting
one test nullability annotation. Exact SHA256 values:

| Binary | SHA256 |
| --- | --- |
| Desktop core | `835bea7d744ff82b6bebd691b698d3cb9f1fb81ac1df44cde111b9a483c10d3c` |
| Dedicated core | `b6144b2cb65a09089842637019055d883c5b70ff812d8ccca2124b3fc273a572` |
| External-import tool | `a055a8663a048cd141c63b8bc3b04719d013b323a91c0400648e723b873bd81f` |
| Character-preview tool | `c257c1a7919aec6bfa6b6d6870a027885d84768867553e01b0109ec8474b1f60` |

The actual owned-source CPU probe passes **184,312 assertions**. It creates
actual old v3 and v4 generations using independently retained frozen exporters,
cancels both migrations at BeforeCommit, then upgrades with the current default
importer. All 40 v3 and 44 v4 component files compare byte-for-byte and by hash.
Each new body captures its correct Weavel profile, three complete GX programs,
seven textures and full source hierarchy. All 113 strict clips sample at start,
off-grid time and end with complete finite 36-node poses. Repeat reuse,
pre-cancel, removal, retained generations and final cleanup pass.

The actual Windows/OpenGL owned-source probe passes **1,176 assertions**, with
28 refusal cases and 80 frames in two fresh contexts. It switches all four
Troopers plus normal/Overcharged Echoes through one real launcher sideScene in
each context, without changing the global model-replacement preference. Complete
source triangle/material/texture ownership, literal submitted post-slot1 matrix
rows, Power-only C1, animation palette/pixel changes and exact zero/off-grid/zero
returns pass. All profiles retain stable resources through time changes; context
recreation retains exact pose content. Invalid clips retire partial owners;
the same rejected owner cannot resurrect them, while a new owner reopens.
Injected shader-link failure deletes every newly created program/shader before
publication. Scene/context retirement deletes every observed source program,
texture and display list. No match actor or full native-UI session is claimed
by this sideScene fixture.

Synthetic GPU verification passes **446 pixel checks**, including 79 source-GX
assertions through production generated GLSL and the actual Scene transaction.
Literal oracles cover yellow C1, texture modulation, PREV/C2 propagation,
zero/yellow/zero isolation and post slot1. CPU suites pass 169 GX, 501 raw
archive, 421 source GLB, 966 Trooper GLB, 50 generated-cannon and 171 recipe
checks, alongside the full existing importer/bundle/OBJ suites. Existing body941,
physical-root24, source-rig910, replay3050, offline policy971 and network
architecture checks pass. The actual offline cannon regression passes all
77,124 assertions on the same final desktop/tool binaries, including native
gameplay parity and fractional late-aim presentation.

Owned Prime, Echoes and native AMHE1 input inventories remain unchanged; both
owned probes delete all temporary derived files. Twenty private source captures
were reviewed or matched to an identical already-reviewed capture, then deleted
individually with hash receipts. Ready poses have coherent full source geometry
and distinct Power/Ice/Wave/Plasma colors. A low-contrast Wave head detail was
checked against all original geometric triangles and the exact head UV region;
no source horn geometry is missing. This is qualitative preview inspection,
not an exact rendered-edge or original hardware/room-lighting comparison.
Action clips may exceed the static camera fit; existing rotate/zoom controls
remain available. Full animation framing is not qualified.

Local metadata/logs are ignored under `artifacts/sh-integration/`:
`trooper-gx-final-owned-cpu.json`, `trooper-gx-owned-ready-gl.log`,
`trooper-gx-protocol81-retirement-synthetic-gl.log`,
`trooper-gx-final-external-synthetic.log`, `trooper-gx-final-retro-synthetic.log`,
`trooper-gx-cannon-regression.log`, both capture-retirement receipts and
`trooper-horn-source-comparison/comparison.json`. None retains game captures or
extracted payloads. Renderer: NVIDIA RTX5080, OpenGL4.6 NVIDIA617.42.
No Android build/device/provider or macOS execution was performed for this slice;
GLSL300 source tests do not establish GLES execution.

## Reproduction and next work

Use the owned Prime Rev2 RVZ with `external-import-check --probe-prime-rev2`.
`PRIME_REV2_BASELINE_CORE` points at the separately retained old v3 desktop
core SHA256 `0599a74b5d56d8fe2aa04194107dcc38224e5bfe3d5db4590f0851dd812adfe7`;
`PRIME_REV2_MATERIAL_BASELINE_CORE` points at the old v4 core
`dd30361bbbd4a57159086c69cbf8ba4d4f1601a03dbf6b54b662e95875b53388`.
These exact old binaries are needed only for actual cross-version comparisons.
Synthetic suites need no old binary or source media.

For the actual GPU probe, use
`external-import-check --probe-character-trooper-generated <owned-rev2.rvz>`
with `PRIME_BODY_TEST_ASSETS` identifying the existing owned AMHE1 directory.
Optional `PRIME_TROOPER_TEST_ECHOES_SOURCE` adds both Echoes profiles to the
same-Scene switching test. Optional `PRIME_BODY_TEST_CAPTURE_DIR` must be inside
this worktree's ignored artifacts directory; delete captures after review.

Next work is a profile-specific Trooper pose adapter over these captured clips:
root rotation/translation pinning, retained pelvis sway without horizontal
travel, model-space torso layering and native-clamped aim. Hunters selection,
exact local offline Weavel actor/life ownership and unsupported-state native
fallback then need actual-match verification. This viewer does not implement
those transitions, nor imported Trooper abilities or first-person presentation.
