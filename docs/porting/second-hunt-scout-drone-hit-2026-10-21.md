# Imported drone post-flash hit tail

Alpha/Gamma solo Scout Drone practice uses original source hit18 after the
native red damage flash. Presentation reads the existing native damage age;
it never changes damage, invulnerability, movement, attacks, collision, RNG,
camera, HUD, wire or replay state. The twelve-clip profile reuses both immutable
v1 imports, the complete33-joint rig, original materials and existing fixed fit.

## Closed phase and priority

The exact added primitive is ordinal18, `R_hitf_drone`, ANIM `944ca609`, SHA256
`eb5947bc1a81463b51b0ea2f0bd8039eec594ed0f2afaaf8e2ad7d00ef7f0ec1`.
It has37 keys and original duration1.2000000476837158seconds. Existing private
RootPin retains authored pelvis height and removes root/pelvis travel only in
each actor's own clip copy. Source/import/shared rig bytes remain unchanged.

Synthetic presentation state11 supports native ages4–71 and samples `age/60d`,
without an extra entry tick, loop or restart after a status fallback. Native
ages0–3 retain complete native red-flash pictures. Age72 is deliberately outside
the donor float endpoint, despite the raw duration being slightly above1.2 in
double precision. Laser takes priority over hit; hit takes priority over burst
and free movement. The native attack continues behind the presentation choice.

Hit entry Snaps through the existing nonfree branch. Free movement selection and
phase clocks freeze while hit is active. Return to free animation holds the
actual current local hit pose and uses the existing0.12second/eight-step blend.
Unsupported completed steps clear held poses, so recovery samples current native
damage age directly. Repeated pictures and pause never advance source clocks.
Life resets, closed ownership, native status guards and complete native fallback
through camera progress0–39 retain their previous behavior.

Clip selection adapts pinned MIT SecondHunt DroneBody at
`41452f2d053fcfa679e33c00a73d120a53f59e61` (Copyright2020 NoneGiven; LICENSE
retained). Source animation events produce no damage, sounds or projectiles.

## Qualification

Production source freeze: `b2e2924f535cb153bddff949a8cc4f9f10150586`.
Desktop core SHA256:
`8555a2307f5c4946ae1a2d0c20ebc63d83cd57d925b46c389371352057c7b074`.
Focused desktop/dedicated builds pass on this Windows machine, with the existing
CS0649 readiness-fixture warning. Desktop2,708/server2,690 source-body checks,
24 filesystem/910 synthetic-rig checks,3,107 replay-format checks,971 offline
character checks,694 historical replay compatibility checks and architecture
checks pass. Commands/results and binary/log hashes are retained locally in
`source-drone-hit-regression-receipt.json`.
Receipt SHA256:
`c08c00d1f8c50f7d562047099517e2173dfee973d64042d86d7cbaa34d7c7017`.
Dedicated core SHA256:
`fdd2a27b7fdfb286a291789405d4d00c4849ebdc9f3ce4574fa02045a93b465f`.

Tests first reject the previous eleven-clip movement core before owned-media,
native scene or GPU IO. Independent frozen-parser gold covers225 raw hit poses
per appearance,68 absolute-age poses and eight held-current Ready return poses.
Together with Ready gold this evaluates1,748 full-rig poses/2,906,924 source
positions, with zero raw sampler error and the unchanged0.001 rigid bound.
Oracle receipt SHA256:
`8d4289afcf07852380c899f2427642ab6e0707e5ed9a5c7dc5b7e65945647b5f`;
gold SHA256:
`076aa380d73da956e4ad6b069d5ffb10e06465aec16f0c6da8d92377501e725f`.
These are independent source/policy checks, separate from actual damage/GPU
qualification.

The first actual damage run exposed a fixture error: native flash uses each
RenderItem's `PaletteOverride=Metadata.RedPalette`, retaining its original
texture bindings. The test now checks the actual override on every native drone
packet and its absence on ordinary fallback, rather than requiring a different
texture ID. The next run exposed an inherited test's hardcoded eleven-clip count;
it now recognizes the exact closed twelve-clip set. Both failed logs and their
original tool/core identities are retained. Neither failure required a
production change or relaxed pose tolerance.

The third run exposed a second fixture timing assumption: clearing the injected
cloak did not immediately restore native opacity. At age25 alpha is0.95161295,
at age26 it is0.983871; both require complete native fallback. At age27 alpha is1
and the first supported source hit directly matches the independent absolute-age
pose. The fixture now observes the actual first supported step, bounded by35,
rather than requiring source presentation at the unsupported age25. The original
failed log is retained (SHA256
`5aac40e68652873570d692aeddde9789b386b9d8dcb5fbf6a3a456ac82ffe226`).

The corrected no-capture Windows runtime run passes795,509 checks on the frozen
desktop core above and tool
`86c6e9fe604ed2dc71a2545bc0e0615425a356864c74e94e2807d10a9fc9e8f8`.
Raw log SHA256:
`91971ae574f7acf1b6bc4117e7d0df95f118568bef913135d7782bf5fc444887`.
Each appearance uses seven accepted ordinary public
`TakeDamage(1,DamageFlags.None,null,null)` calls and two immediately rejected
invulnerability calls, without writing HP, damage age or native palette fields.
These counts describe the matched trace; each source case uses one additional
accepted call for the separate context-retirement fixture.
The second repeat is accepted only after native invulnerability drains, at
least30 completed ticks later. Damage during genuine burst leaves native firing
active and returns to its current frame at age72; native laser retains priority.
An active age12 source hit also retires cleanly with the Scene GPU context.

The separate1,666-record HitTrace SHA256 is
`1c5d57cef86dbf6dc8806d86e4cfd8544b1c1f1f30166f4a46b7ef71ee6460fd`
for Native, Alpha and Gamma. It includes native state, HP, damage age,
invulnerability, opacity, camera progress and the observed native red-palette
boolean. Texture allocation IDs are excluded from cross-run canonical parity.
Its grain is1,657 completed steps plus seven accepted/two rejected API events.
All four prior1,418/420/3,083/2,793-step trace hashes remain unchanged. Per source
appearance299 hit-tail and68 laser-priority pictures,5,651 full33-joint CPU skins
and36 selected actual GL phase keys pass. Native age0–3 flash packets are checked
explicitly, with28 observations per appearance. Pause/repeated draw, status
recovery, genuine merge/resplit expiry, life reset and context retirement are
covered. No opponent or authoritative combat acceptance is inferred.

The final capture run passes795,909 checks on the same frozen core/tool; all five
traces match the no-capture run. Raw log SHA256:
`1df9a7ed8070f88645dd9accbcff7309dedcc8c79c6b11942dfc6701305cc4b9`.
All eight native-camera pictures were viewed, hash-checked and individually
deleted after verifying their exact containing directory; zero remain. Per
appearance these show age0 native flash, age4 source and same-step native
fallback, and age71 source. The native flash is visibly solid red/orange; Alpha
is silver/gold and Gamma purple after the flash. Selected source silhouettes
stay inside the frame and differ between first and final tail samples. The
native planted turret remains in the foreground. Small source size and HUD text
overlap limit detailed art judgement; this is scoped camera/fallback evidence.

Every capture records camera40/Third2/normal FOV78, no zoom/HUD zoom/Scene form
blend, exact actual view/projection/inverse-eye/HUD state, and zero Roam calls.
Each age4 source/native pair retains identical camera, native state and source
clock. No camera or mesh fit adjustment was needed for these selected pictures.
Capture manifest SHA256:
`5b66fb1344882ad9ed57a35fd3ccf81ff58a4b7820087eeba85cd82eba3a4b26`;
inspection SHA256:
`f05b8b7da6b8043a0542e14642789ce288ce661f9a2445ccb28b5904aabadad8`.
Owned source/native inventories stay unchanged and temporary derived imports
are deleted. Raw local receipts remain outside Git; no owned images or payloads
are included in this change.
Final compact runtime receipt SHA256:
`a7f87693390c7f41df78cfe390f09ea500cbe938690973547116e01f893f0306`.
Test source commit: `20426afb8`; production tree remains the freeze above.

## Limits and rollback

This is a local solo presentation slice. Full imported characters, source weapon
presentation, Arsenal admission, opponents/combat authority, remote per-seat
appearance, multiplayer/replay support and broader visual acceptance remain
unfinished. No Android, Mac, other-platform or deployment claim is made. Existing
native appearance selection and unsupported-state fallback retain the native
drone. Owned source files and captures are never committed or uploaded.
