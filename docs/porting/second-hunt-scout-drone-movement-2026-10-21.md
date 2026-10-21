# Imported drone directional flight

Alpha/Gamma solo Scout Drone practice selects the original six source flight
loops from completed native movement. Source poses never move the actor, apply
source root travel, redirect aim or change native attacks/collision/RNG. The
same closed solo owner, complete33-joint rig/1,390 triangles/two material groups,
fixed Ready0 pivot and scale0.3 remain. Native laser and burst retain priority;
unsupported status/opacity/morph/shadow and native camera handoff pictures retain complete native fallback.
No bundle/recipe/source bytes, wire/replay/native ABI or UI intent changes.

## Closed clips and selector

These original primitives were already in both immutable v1 imports. The worker
and actor now validate/privately derive eleven exact clips, including the five
previously qualified Ready/burst/laser clips. Hit18 remains excluded.

| Direction | Ordinal / ANIM | Raw SHA256 |
|---|---|---|
| Forward |4 / `1f21510a`|`857458dfcc824d8d57a19762eb5f95e5f9eb26fcd2725a43c19e5013510012c7`|
| Back |3 / `aff82288`|`fa217ba84b3b7d0d871fbdd9b3c2798951ed3d7e01ef430e08ca855571a046e1`|
| Source left |2 / `fa0107c9`|`898e9a5304cf9f38fbca896bc8a22b5acebadb8e3c8ec4ccc2a8baf783483fa7`|
| Source right |5 / `e87fa8e3`|`891ed408224bee08beeae347dc527c68028bfe694add7a861b5fcf6ae33680e3`|
| Up |6 / `ab7bdfeb`|`b1c6873384fe362de16cbd8c8df1e90b7c4612c8f613852729d713d536c8bbc8`|
| Down |1 / `28e34205`|`bfa409602fa9b4858f2aded40ca7b81257b209d117b12a99305e7c10f9203c86`|

Forward/back have40 keys and duration1.2999999523162842seconds. The other four
have27 keys and duration0.8666660189628601seconds. Existing RootPin retains raw
pelvis height and removes authored root/pelvis travel only in each actor's own
copy. The raw bank and shared source rig remain immutable.

Selection adapts pinned MIT SecondHunt DroneBody at
`41452f2d053fcfa679e33c00a73d120a53f59e61` (Copyright2020 NoneGiven, LICENSE
retained). Velocity is `(currentCompletedNativeCenter-lastCompletedCenter)*60f`,
never interpolated draw position or native half-step `Speed`. The owner observes
center history even through priority attacks and full fallback, so re-entry
cannot turn several elapsed steps into one step's velocity. Constructor/life
reset seed history; the first life step uses zero velocity.

The selector flattens canonical native facing to XZ using direct float division
by `MathF.Sqrt(X*X+Z*Z)`. Degenerate facing uses the native flat facing fields,
then UnitZ when squared length is at most1e-8. Its lateral dot corresponds to
`Cross(UnitY,flatFacing)`; at facing+Z, velocity+X selects **source left**. A
maximum absolute forward/lateral/vertical component below `.9f` selects Ready;
exactly `.9f` moves. Vertical must strictly exceed both other components;
forward wins horizontal ties. This is a component threshold, not vector speed.

A changed candidate must persist for eight completed simulation steps, except
leaving committed Ready selects immediately. The pending counter saturates at8.
Pending movement choice freezes behind native laser/burst and resets to Ready
on full fallback. Selection/pose/clock history never updates from draw or pause.

Free presentation states0,5–10 correspond to Ready/forward/back/left/right/up/down
and admit frame0 only. A changed free clip starts at1/60second and loops its own
original duration. `FreeCompletedSteps` owns the selected free phase, including
Ready; `ReadyCompletedSteps` remains specific to Ready and preserves its prior
reset/resume semantics. Free clip changes and attack returns blend from the
held **current** local pose over the existing0.12second/eight-step Mix. Native
burst/laser entries Snap. Holding the current mixed pose avoids a discontinuity
when a direction changes during an existing fade; donor internal bookkeeping is
not claimed bitwise identical.

Unsupported completed simulation steps clear held fade and pending selector while retaining the pose's
last clip/elapsed phase, consistent with prior Ready behavior. Re-entry into the
same free clip resumes its frozen clock; a different clip restarts at1/60second.
Both enter directly without a held unsupported pose. Picture-only guards such as
cosmetics visibility do not mutate simulation-owned history. Life resets clear all clocks.

## Qualification

The initial movement freeze was `07846e4f71efdb33b29d2f60044efbdfa76c8d68`; desktop core
`3e27b004f3162c8cf1d5c456a624a36bb54c2d0c849db2e72a574e43a38b5dc9`.
The initial actual GPU/Scene smoke passes384,877 checks on runtime tool
`3550b2a52ad6716acb3cb0ee26b5ae4d8e09f14ef7dfe9bea869d62b7919bc11`.
All eleven clips bind and the independently modeled completed-step velocity,
selector/hold, priority/fallback and phase clocks match the actor. Native, Alpha
and Gamma retain the exact original1,418-step trace
`aa44ee6f8e9a3a78fe7ccf1b58b7e79db1d5cdf3fa39731a5218e43bd25acefc`,
420-step burst trace `8a738ab661fdb308a8e140142feca62602e83a4e995bb9fb666671d89bea299f`
and3,083-step laser trace
`01f2395ccd4525d390e56f1a8285dbbb29b90b2702a174611f2fedc51f12e8e4`.
The independent owner phase model checks5,060 completed source samples per
appearance; free-motion transitions legitimately add held fades beyond the
earlier stopped Ready-only return script. Source/native hashes remain unchanged
and temporary imports are deleted.

The extended no-capture run passes649,103 checks on tool
`05a4cc98f2501ff4b5da7354504503afd8bc4c102d29e3fe4cacf1e9e1fed182`.
All six committed directions appear through native public Spawn followed by
genuine W/S/A/D/Space/release input; Spawn setup is declared and identical across
appearances. The new2,793-step trace SHA256 is
`a4b48705aa9ba59ce7a2c3d6b81d6169b9e19bced85ecb8c789a7e589c94870e`
for Native, Alpha and Gamma. The preceding three canonical traces remain exact.
The extension includes86 actual moving-attack steps, status reset/resume,
public-Spawn life resets, pause and actual GL readback at34 selected phase keys,
including every new directional state. Independent1,174 literal float cases,
47 hysteresis cases,3,686 raw full33-joint key/off-grid/seam/loop samples and47
held-current transition rows pass per appearance. Native/source hashes remain
unchanged and disposable imports are deleted. Final capture inspection is pending.

The pre-guard capture/stronger Ready-clock run passes656828 on tool
`87a0f4064c0db3f648b945e1dabe45f4d38316aa54a0d1a18d9935e648267ef2`.
The14 initial directional captures were all viewed, hashed and individually
deleted. Twelve oblique pictures show the six poses and distinct Alpha/Gamma
materials; ordinal5 has partial wall occlusion. The two initial gameplay-camera
pictures are heavily cropped with an orange HUD overlay. They were captured at
the first committed Down during setup, rather than the later explicit descent.
They do not qualify settled camera fit. Orange coloration alone did not prove
zoom; the later failing runtime observation explicitly has `Zoomed=false`.
Initial inspection SHA256:
`dc6b41a9f07803f8765661f50aa01850bd7835fe00fecaf185ce23abf451519e`.

This investigation found a real admission gap: native Scout Drone clears its
Morphing flag immediately while its independent camera timer progresses0–40.
Tests first on the frozen3e27 core show source2/native0 at camera timer0/end40,
CameraType2/FOV78/Zoomedfalse, failing the required full native handoff fallback.
The retained failure log SHA256 is
`d7291ef9e5b329840f2c29673989ad7b7658faf19283631e6987c895ddf15487`.
The final production freeze `4fb92baed6d88bcc7653ace6d6db09359dfd47bd` adds only
`_camSwitchTimer >= Values.CamSwitchTime*2` to source admission. It keeps native
presentation through progress0–39 and changes no native camera/HUD/fit/gameplay.
Final desktop core SHA256:
`6f4c8f01299179101adb4271688b0dca0a77a9b1584445afffbe3a545a1f60e3`.
The final actual runtime run passes640,884 checks on tool
`3c73e9d39579e43bf25eca234b8d1d7a6b74c978821bc609482ef76b80263041`.
All four canonical gameplay traces remain exact across Native/Alpha/Gamma.
Each source records960 native fallback pictures spanning every native camera
progress0–39,9,494 source pictures/1,051 native pictures,4,748 full33-joint CPU
skin checks,6,759 completed phase checks and326 held fade steps. GL final-batch
uniform readback covers33 selected phase keys, including all six directions;
this is not a claim of33 simultaneously resident shader slots. Source/native
inventories remain unchanged and temporary imports are deleted.
Final raw log SHA256:
`f3c9907e4232d82041582bfdd18bb7f256a1cfaa4eb24dd37dbb53000b5e37e7`;
compact receipt `c1e5cc58807b6d2a4a08623a816cd561240c389fef8ffee9a187220a2b0dc874`.

Four final settled Down source/native-fallback images also pass exact same-camera
comparison: native timer40/Third2/FOV78(normal78), Zoom/HudZoom/Scan/Menu/Dialog
false, HUD layer bindings[-1,-1,-1], Scene handoff complete, and identical actual
view/projection/inverse-eye. Both source poses stay inside the frame; HUD text
substantially obscures detail in source and native pictures. The orange overlay
is absent. These controlled pairs qualify this sampled descent/camera case,
with no mesh fit, native camera or HUD change beyond the source admission gate.
All four were inspected, hash-verified and individually deleted. All18 movement
captures are now deleted. Final inspection SHA256:
`fdbefc8a15b667134597efca7a37ef7a92cf5ba0d933e113948d9e046554f1ad`;
four-file manifest `9bde6b034f171ee679d1bdc808fdf1586a90dcc1283b4dce7086e8ae3de8ce0e`.
Earlier oblique views retain their bounded source-pose evidence on the identical
pose/motion implementation; their two gameplay views remain unqualified.

Diagnostic Roam drawing also resets Scene's separate18-tick handoff history;
restoring five camera fields does not restore that prior presentation history.
The old native/simulation/source clock and pose checks remain valid, but their
camera-restoration assertion has that explicit limit. Final source/native
fallback pairs therefore run with zero Roam pictures, native camera complete,
Scene handoff settled, and exact actual view/projection/eye/HUD comparison.
Read-only native camera review receipt SHA256:
`dd4e4ce2e939cc0c4852e678c1db227bfc9765b896a286e8c3bbbdc7d83340e5`;
Scene render handoff review
`9e3c4dc12d859226609f3daa27419534f285dc2eec3d14d951ff3cc03c77bd10`.

The final guard freeze repeats desktop/dedicated source-body2708/2690,
filesystem24/synthetic rig910, replay-format3107, offline-character971,
replay-compatibility694 and architecture checks successfully. Dedicated core is
`8383b07c4a096879c91d0df1fca1c6826316084babd88fc4541782a73745bcc0`;
Nettest `61f5c03e55211729b195ec5cc955de04b594a8c7e0766e6323a830d8bc9655f9`.
Final regression receipt SHA256:
`48755ff47e4f0764d33c0071d984dc3d98ceb38358eba3b941fbfe66476f5073`.
Final test commit is `94c4c63edd42827bb799d5b2433e7fcd12a9261d`;
source/native trees `0882e873ad02dd8ef2a9c8d0d86cc89b4a6eee0c` /
`491e699c7767dfb1bb9deaf651688315d068387a`.

Desktop/dedicated source-body2708/2690, filesystem24/synthetic rig910,
replay-format3107, offline-character971, replay-compatibility694 and architecture
checks pass on the frozen core. Dedicated SHA256 is
`c3e543a2f243d3acb3674018c78a889b3b97c6c1be05e8237ebf26eb0fcb1b22`;
Nettest `96059bc663d715bfc4f0f01d7522313056d18e9d53d64692548389fb917fed97`.
Regression receipt SHA256:
`38c036eb16bff7c37c511c65519e72cd4db87890cefb03351a6ba703c3ea0ba2`.
Both builds have zero errors and the existing CS0649 readiness-fixture warning.
UI/native ABI/recipe code is unchanged; prior PR969 native Hunters/bridge
qualification is historical evidence rather than a newly repeated UI claim.

Tests first on the frozen laser core
refuse missing `CharacterSourceDroneMotion` before owned/native assets, Scene,
GL or gameplay. Failure log SHA256:
`90a968e496b8d2eb6f61dd5cb1c0881425faf98911f189e74ebf4d9fe740164a`.

Independent frozen83fe sampling passes twice:8,670 full33-joint poses and
14,418,210 source-position evaluations across Ready/all six loops, all keys,
off-grid/seam samples and481 completed-loop phases per clip. Independent raw
sampler error is zero; existing rigidity tolerances remain unchanged. Metadata
SHA256 `40eaf8e3e6f8dc1b0f062520e0287112ee6110ed7b627a3b52fdafb06c1fb8cc`;
receipt `c628817e05e3b479a52a410b56603b66895a67aa0f892eab378e8db4475c8bc5`.
That oracle is source math evidence, not actual movement/render acceptance.

A separate independent float selector fixture passes twice with1,250 assertions
and1,174 cases (threshold neighbors, ties, rotated/nonunit/degenerate headings
and bounded hold). Reciprocal multiplication changes163 deliberately sensitive
choices versus the declared direct division. Receipt SHA256:
`4d78f224cee3e6fb0f7497eee109bde97e5e7af2efe02a209df406b2a6f56db7`.
Independent full33-joint held-current transition gold passes twice with94 rows
across both appearances, including a new choice during a three-step fade,
laser-to-moving return, clocks and same/different suspension resumes. Exact
float reproduction uses direct `Row3.Xyz` assignment; numeric double-vs-float
differences are diagnostics and never relax acceptance. Receipt SHA256:
`28015c8d387993d2bc21944c2f4911f928cf02826bae7ebd44dacd4ee7bb2894`;
47-row shared gold extract `8a111d6f4068ad1f57736800dfe1364256ad2902eb29ba7332c0874814daa525`.

Hit source animation, combat/arsenal admission, multiplayer seats/characters and
the rest of the integration engineering remain unfinished. Android work remains
deferred under the user's Windows direction. No full character, broad art or
performance acceptance or external CI/review approval is claimed.
