# Normal Dark Samus source ball in local matches

The normal Echoes appearance selected through Hunters now uses its own source
ball in local offline Samus matches. It reuses the existing transactional import,
full source-rig loader, per-player appearance owner, Scene GX graph and native
alternate-form draw pose. Native rolling, position, collision, bombs, effects,
camera and simulation remain authoritative. Defaults remain native; this is
presentation only. Overcharged and Troopers retain their native alternate forms.
The complete character integration and Second Hunt plan remain unfinished.

## Source and generation policy

The original normal ball uses ANCS `654a3f97`, CMDL `2f2c1e6f`, CSKR `4403b1b9`
and CINF `c7dbc44a`: all2220 triangles,1553 positions, one opaque material and
the complete two-joint source rig. The source GLB SHA256 is
`eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9`.
The fixed ready pose is ordinal1 `B_ready_Dark_Samus_MorphBall`, original ANIM
`3bf596d0`, time0, SHA256
`792bb9d335da503cf1fe0eb59c65a01f7c47ee2aebb48385e5d44c77736e470e`.
Its two keys are constant. The source roll clip carries travel and rotation;
playing it above native rolling would duplicate motion and is expressly refused.

The existing raw material archive format1 gains append-only profile7,
`darksamus-echoes-ball`, with the ball's own four-TEV/four-texgen program and
four original CMPR textures. Independent source decoding proves all6660 corners
have identical T0/T1/T2 coordinates before using the existing GX aliases. Ball
materials cannot borrow a body/cannon lease. Whole GLB, provenance, ANIM bank,
graph, raw ANCS/CINF and material hashes bind admission to this exact generation.

Normal bundle7/body5/ball4 adds one material archive and an AlternateForm manifest
entry. It has25 committed files and23 required components; the manifest role is3
and the ball has11 required roles. All22 prior normal components excluding the
manifest and versioned parts index remain byte-identical. The frozen bundle6
upgrade proves cancellation before publication retains the old discovery pointer
and all24 files. Final's recipe and all24 components remain unchanged; Trooper
recipes are unchanged. Check/Update/repair/reuse/Remove retain the existing
transaction and immutable-generation policy.

Normal ball provenance SHA256:
`7afe18290250fe745579a931a968204f3dd136da123c7ae3f89def8f6c2a924a`.
Material archive SHA256:
`fadaf0787fa1c9df6b0cf5765db78547953908dc1b24c9b1cba76217c16746b5`.
The material ID uses a dash before `ball`; its existing filename stem uses a
dot. Both are explicitly mapped at capture and revalidation boundaries.

## Runtime ownership

At local launch the complete ready pose is evaluated, centered by its posed
bounds and uniformly fitted to the initialized native collision radius. The
source ball follows the prepared native roll/position plus native sphere-center
offset. Its two-joint palette stays independent of the body and cannon. Source
pose sampling never changes native collision or gameplay state.

Ball readiness and material time use completed unpaused60Hz steps, reset per
life, and remain independent of unsupported biped poses. Repeated pictures do
not advance clocks, load files, sample RNG or mutate gameplay. Scene/player/
slot/life/pack/context fences and exact native-model identity apply to every
packet. Context or appearance retirement releases ball, cannon and body resources;
subsequent native morphing cannot resurrect them. Source files are captured at
load boundaries and can stay exclusively locked throughout gameplay.

Morph/unmorph transitions, death/status overlays, dynamic shadows, cloak and
other unsupported states use complete native presentation. No new launcher
intent, native bridge ABI, Hunter ID, network packet or replay format is added.
The source viewer remains Body/Cannon; `HasBall` indicates captured availability,
while `RuntimeBall` is granted only to local-match selection.

This slice renders the original raw normal mesh/material. The donor's separate
procedural blue core/trail is not implemented. Final's donor vein/shell/heart
construction requires an independent recipe and cannot alias normal's ball.
Original-game lighting, full art fidelity, other weapons, online/replay cosmetic
identity and imported drone/ability presentation remain engineering.

## Windows qualification

Production commit `e00e0c4b6` is based on main through PR953. Preintegration
desktop core SHA256:
`10c7426451dcbb7b2d793b36ab297e62079d6fd6d185ef37d262793cfbf3ee70`.
Dedicated core:
`e6bf840215402df042986b92aa428170fd536bb1ff6c4e16a54230d69fe9b01a`.
Both Release builds pass with one existing readiness warning and zero errors.

- Independent original-source CPU proof:6808 checks/33 refusals, all source
  triangles, UV aliases, full rig/ready pose, raw material/TXTR bytes, fixed-time
  refusal, corruption/alias/generation guards, file locks and retirement.
- Full pure suite passes, including43 independent fit checks and existing
  normal50/Trooper18/Final21 cannon tests. Owned migration records3339 passing
  labels, actual frozen bundle6 cancellation/upgrade,22 preserved components,
  unchanged Final output and unchanged123 frozen baseline dependency hashes.
- Actual Windows NVIDIA RTX5080/OpenGL4.6:322914 checks on external tool
  `6fe1f2218389a1b8e1f86006511535f58a3d8e73361f55ff7f2a6b4c0292bb14`.
  Native, normal and Final have identical1415-step gameplay trace SHA256
  `f427384c4531686dfaef0d11cb0f35fce029e55dc4823bea0d85e11f136d6ca6`.
  The trace includes input-driven rolling, a real owned native bomb, declared
  injected death, native respawn and a second-life morph/roll. It does not claim
  an opponent-caused death or physical-controller execution.
- Normal renders1372 source-ball pictures including the separately counted
  pause/guard/render-rate checks,110 after respawn. Maximum palette/center error
  is0.000000953675; independently evaluated fitted radius matches native exactly.
  Final/native render499 native-ball fallback pictures in the main trace.
  Pause/repeated draws freeze source pose/time. Declared cloak/shadow fixtures
  restore native presentation, and post-retirement native remorph passes.
  Simulated90/120/240/540 picture schedules preserve native rolling and60Hz
  source material time; these are not achieved physical refresh measurements.
- Actual launch lifecycle:65 checks, normal ball2joints/2220tri/current, Final
  no source ball, repair/Remove/reinstall and resource retirement/native recovery.
  Existing Body/Cannon viewer:1787 checks/94frames/two contexts.
- Desktop source-body2708, dedicated2690, root24, rig910, replay3107, offline
  policy971 and architecture pass on the same10c core. No historical network
  or replay reader is changed; inherited protocol remains82.

Main PR954 local weapon-package hosting was inspected and merged without
character-path conflicts at combined source `82d49cc499df9cec2a8c89b2fb86e926a984cd76`.
Combined desktop SHA256:
`b43f8dd4bc065007a89a015d67fc902e1bc61c4b34984fe9d2503609348b3b70`;
dedicated:
`8063dbdb021288f44c0cc40a3a91e2476f22731d4ac31b733338f78c02b676a8`.
Both Release builds pass with the same existing warning. Combined external tool
`86368d1a8b8f9226b66e3210ed7b6471b59770f2bc8b3fa9fab6611d6515ba04`
again passes322914 actual world checks with the identical1415-step trace above,
plus65 launch checks. Combined desktop2708/dedicated2690 source-body, replay3107,
offline policy971 and architecture pass. Nettest SHA256:
`fbc2e81c6b7cc1a1b268dc581aa4f913f1b004e3142b0220dfa4badab94a7793`.
Shared-render Trooper regression passes279841 actual checks on that same core/tool:
native and all four Troopers retain identical675-step gameplay trace SHA256
`8b0886273d4fb47f50e48d07db21e6da4490df38e48bdca8638ebb563f86dc20`.
Each Trooper retains397 source cannon/288 native fallback pictures; no source
alternate form is admitted. Prime Rev2 source and native inventory stay unchanged.
The earlier independent original-source/migration/viewer evidence belongs to
its explicitly frozen10c binary; it is not relabeled as a combined-core run.

Initial raw failed GPU attempts remain in ignored logs: the fixture had not
bound native AltAttack/RollUp controls, so it could not produce a bomb/movement.
Explicit test bindings resolved both failures without a production change.
A diagnostic compile failure and its accidentally invoked older binary are
retained separately and excluded from passing evidence.

Six actual normal ball-roll/bomb and normal/Final cannon idle/fire captures were
inspected locally, hashed and individually deleted. They show source geometry
inside the native arena/HUD, not an original-game art comparison. Logs/receipts
remain ignored under `artifacts/sh-integration/normal-ball-*`; no protected
payload or captures are committed or uploaded. Owned Echoes RVZ SHA256
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`
and native AMHE1 inventories remain unchanged; temporary imports are deleted.
The pinned donor is `41452f2d053fcfa679e33c00a73d120a53f59e61` (MIT,
repository LICENSE retained). No CI, external review, other-platform, online,
Android or complete-character acceptance is claimed. Android testing remains
deferred by the user; testing continues on this Windows machine.

Rollback is selecting Native or disabling custom cosmetics. Existing immutable
generations stay available until explicit Remove; unsupported states retain
native presentation and no source cosmetic affects authoritative gameplay.
