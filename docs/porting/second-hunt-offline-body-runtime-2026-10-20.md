# Offline imported Dark Samus body runtime

This slice adds opt-in normal and Overcharged Echoes body cosmetics to an
actual local offline Samus actor. It is a partial SH-05 delivery. The full
Second Hunt plan, imported Trooper actors, source cannons/balls, multiplayer
identity/ability authority and complete arenas remain unfinished.

## Product behavior

In **Hunters**, select Samus, choose **Native**, **Dark Samus** or
**Overcharged**, then **Equip Loadout**. The existing loadout draft owns
selection, discard, concurrent-edit rejection and persistence. The default is
Native. The launcher preview still shows the native loadout; its hint says so.
The chosen imported body requires the user's complete local Echoes import.
A missing or invalid bundle aborts the offline load with an error before
publishing an actor; choosing Native restores the ordinary load path.

The choice is captured in the immutable offline launch plan, separately from
gameplay capabilities. It applies only to Samus in a native offline match.
Campaign, online, host, demo, replay recording, replica, M7 and Trooper practice
routes cannot activate it. A saved choice does not change online presentation.
The local appearance does not enter Hunter IDs, network packets or replay
metadata. Current source protocol is **79**, unchanged by this slice.

Walking, turning, aiming, firing and charging use the full source rig and
source body material programs. The native gun, muzzle, projectiles, ball,
collision, health, movement and scoring retain their existing owners. Native
presentation handles jump, morph, death, crouch/tuck, status effects, bright
skins, teams, overlays, fractional alpha, LOD and camera sequences. **Dynamic
shadows must be Off** for imported body drawing: other qualities select the
complete native body because the source packets do not own shadow casters.
The existing chase camera can also hide/fade the local body near geometry.
This slice does not add an imported first-person cannon.

Windows x64 now has a physical source-root identity profile. It reads volume
and 128-bit file IDs plus final paths from opened directory handles and
compares every ancestor at proof boundaries. It retains stable aliases and
rejects retargeting/replacement. Existing macOS arm64 proof remains. Linux,
Windows arm64, macOS x64, Android and dedicated processes cannot activate the
new body choice. This delivery is qualified on this Windows machine only;
there was no new Mac or Android test.

## Ownership and rendering

`MatchStart` captures a validated immutable source selection on a worker at
the explicit load barrier, then binds it after native Scene initialization.
Each initialized player owns independent source animation state, full
39-joint pose and material clock. Segments gather at most 32 joints for the
existing shader limit; the source hierarchy is never truncated or mapped onto
the native palette. Normal and Final resolve their own generation, strict
clips and source materials. Final does not acquire normal's cannon authority.

The pose adapter reuses the existing source decoder. Ready/four glide clips,
Spine_1 firing/charge masks, a six-tick transition and a held aiming arm follow
the pinned donor behavior. The adaptation retains MIT attribution to
`SecondHunt` `41452f2d053fcfa679e33c00a73d120a53f59e61`,
`src/MphRecomp.Core/Campaign/DarkSamusBody.cs`, Copyright 2020 NoneGiven.
Static ready-pose skinned bounds fit native feet/height; root translation is
removed only from presentation. No native simulation position is assigned.

Pose/material time advances once after each completed, unpaused 60 Hz native
step. Pauses and repeated draws cannot advance it. Bindings retain exact
Scene generation, actor, slot, native model, source pack, appearance and
instance. Reinitialization, destruction, context retirement and Scene cleanup
retire ownership. Draw cannot reload an invalid lease or resurrect a retired
binding. Import/proof/clip IO stays outside Step and Draw.

The existing Scene owns a bounded collection of source body packets. They
use the real world camera, draw opaque color once, restore depth when the
native translucent pipeline clears it, and draw source alpha/additive color
once. They never enter native retained batching or its six-pass color replay.
Native GL state is restored transactionally. Unsupported states choose the
whole native body before any imported segment is submitted.

## Verification scope

The first production freeze is `d8e5a593f`, rebased on main `f4ea73c35` (PR937).
The overlapping CRLF shader startup repair uses main's implementation; this
slice also exercises it in actual GPU testing. Later fixture/documentation
commits do not change production bytes. Builds use a private, official
SHA512-verified .NET SDK 10.0.401, isolated from other chats' SDK directories.
Local receipts live under ignored `artifacts/sh-integration`; protected source
or derived assets are not committed. No CI or external-review pass is claimed.

| Local check (also repeated at the combined freeze below) | Result |
| --- | --- |
| Desktop and Windows x64 dedicated Release | Pass; one existing CS0649 warning, zero errors each |
| Source body CPU policy/draft/retained barrier | 927 assertions |
| Real source-root identity / full source-rig fixture | 24 / 910 assertions |
| Synthetic actual Windows GL | 417 pixel checks, including 50 source GX assertions |
| Actual owned native/normal/Final match | 16,480 assertions; 1,011 identical gameplay snapshots per run |
| Actual launcher Begin/LoadScene/EndScene and import lifecycle | 54 assertions |
| Native Hunters presenter contract | All seven Hunters, 192 publications, actual native intent286 selection/discard |
| Native RmlUi bridge | Windows x64 GL2 rebuild, fingerprint verification and runtime ABI suite pass |
| Offline character launch/replay policy | 971 assertions |
| Network architecture / historical replay / frame timing | Pass / 3,050 checks / all cases pass |
| Actual desktop shader newline fixture | Seven checks |
| Asset-free external-import suite | Pass; actual Windows junction ancestry negatives; file-symlink negative case unavailable under privilege1314 |

First-freeze desktop core SHA256:
`29b47eb7ca447fb62d94f3443b3d2f8b3eaefdd67c3837df3267e05710336901`.
Dedicated SHA256:
`676d548f6c3d252190ebf4608c1f4f89f326547818c555b6b3986fbf90095801`.
The actual owned probes used tool SHA256
`69d63a5f8cc1873450adc97d29b646f8ddc2160712867900fb8c9c3f253fbe8e`,
with the same desktop core. Subsequent asset-free fixture-only changes are
distinguished from that owned-probe tool identity in local receipts.

The actual match fixture uses the user's local AMHE1 directory and USA Echoes
RVZ in place, isolated temporary import/user roots, the real desktop GL
context, native keyboard movement/fire/jump/morph and mouse turning. Death is
explicit fixture damage followed by native input-driven respawn. It compares
a seeded native run with each appearance, including RNG, position, aim,
muzzle, ammo, projectile and score state, and repeats each draw without
simulation. It requires complete imported firing and charging frames as well
as complete native fallback. Pixel hashes establish actual framebuffer
differences, not art fidelity. A second pose owner over the same actor proves
buffer independence; it does not qualify two simultaneous actors.

Each appearance produced 454 complete source-body observations, including all
16 firing, 100 charging and 20 release steps before the camera turn. Normal
uses seven material segments, five distinct compiled programs and ten resident
textures; Final uses nine segments, six programs and twelve textures. All use
39 source joints. The exact native gameplay trace, including RNG, hashes to
`0edd185b7a51e1c1e26f860091b6d7c075fbd1baecacded6161fe850b0c68300`
for native, normal and Final. Draw repetitions preserve that state and raw
source palettes/clocks. Low shadows select complete native geometry and Off
resumes imported geometry without a simulation step. Paused camera visibility
may reduce source packets to zero, while pose/material time remains frozen.

The separate real launcher test captures all three launch plans before later
preference changes, then uses `MatchStart.Begin` and its actual binding barrier.
It proves missing/corrupt/removed selected imports refuse before Scene
allocation, native recovery, Update to a fresh generation, retained admitted
ownership after Remove, Final's separate identity, list deletion at EndScene,
reinstall/relaunch and no resurrection of ended owners. Source/native file
inventories match before/after both actual probes; temporary exports are
deleted. Four optional local idle/fire captures were inspected for both
appearances, then individually deleted; their retirement receipt contains
only names and hashes. They are not publication or broad visual acceptance.

Retained initial failures led to fixes: Windows directory-junction fixture
quoting; the two old macOS-only pack proof guards; Windows shader literals
under CRLF; a desktop fixture's Android-only shader reference; unsupported
dynamic shadow casting. Later actual-match fixture corrections use mouse-only
turning (native keyboard aim suppresses mouse), place firing before the native
chase-camera fade, admit complete-or-zero paused packets, and seed the common
gameplay boundary after load/first draw. RNG remains in every parity snapshot;
it is never reset per-step or omitted to make the test pass. All failed logs
remain local alongside the final passes. No Android test was run.

The broad asset-free importer run additionally exposed two older fixtures
that assumed symbolic-link privilege on Windows. They now use real directory
junctions for ancestry checks. The file-link negative case is explicitly
skipped only on Windows error1314; all other errors/assertions still fail.
The rerun passes with tool SHA256
`7d12006fa3687890363b107e73f621e57a98f52fa3219aded08345d1e8b98c1f`;
production core stays identical to the two owned-probe runs. This does not
claim file-symlink coverage on this unprivileged host.

Unqualified: all source/native status combinations on actual GPU, a second
simultaneous imported actor, opponent-earned death, broad arena/GPU fidelity,
multi-seat, physical devices, achieved refresh/performance, source first-person
cannon and ball, imported Trooper presentation, multiplayer/replay character
identity and authority. These remain engineering or acceptance work, rather
than implied completion from the body slice.

## Combined main qualification

Before merge, main advanced through custom-weapon package play PR938/939 to
`8b8c9b121`. The clean rebase freezes combined production/test source at
`043e12fd72ec2b20bafa256f5308b89799b4ad98`. It preserves both CLI dispatches;
the source-body policy still refuses custom-weapon availability. This final
combined build repeats every table entry above, including both owned probes,
full asset-free importer suite and actual native Hunters contract. It also
passes all 36,965 source-free custom-weapon contracts. Dedicated/desktop builds
retain the same one existing warning and zero errors each. Android is deferred.

| Combined output | SHA256 |
| --- | --- |
| Desktop core (also copied into each fixture output) | `1c7c9606b92ab9fd3d5ada151869e0ac0c701992c8b7a5899b97ebcdbebed989` |
| Owned importer/runtime/launcher tool | `b8af59423f6028c3c58d6cd39acce49b38e9e071dbe44402f5daacc3acff53dc` |
| Windows x64 dedicated core | `4564b4a9f33551cf4fa16ff45e0b13257802871b440c971bcb60929bdc2998a3` |

The exact gameplay trace remains identical across all three appearances.
Local `body-combined-final-receipt.json` pins source, binaries and each owned
probe log, including unchanged owned inputs and deleted temporary derivatives.
Earlier binaries/receipts remain separately retained under `body-first-freeze`.
Subsequent documentation-only changes do not alter qualified production or
fixture bytes. No green-CI, external-review or later-main qualification is
implied by these exact identities.

## Merge confirmation

[PR940](https://github.com/AntiNotAnti/Project-Prime/pull/940) merged as
`ff522877ec16e065ec7e0782928f5f2448ff5b70`. Its tree exactly matches PR head
`fdcf35ace67a8c4cbdd7befa5ff4b6e31f1527de`; the diff from the combined freeze
to that head contains only this evidence document and manifest status prose.
Qualified production/test source therefore survives unchanged. This is a tree
identity check, not a fresh build of an arbitrary later main revision.

## Rollback

Choose **Native** in Hunters and equip it. No import removal is required.
Existing Check/Update/Remove remains the import owner. Match bindings capture
one immutable generation; refreshing the import does not hot-swap an active
actor. A new launch rechecks the chosen generation and provenance. Removing
or corrupting an import prevents a subsequent imported-body launch; it cannot
turn another local pack into authorized content.
