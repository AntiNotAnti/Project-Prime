# Dark Samus source ball trails

Normal and Overcharged source balls now tint the existing native morph-ball
trail blue, RGB90/200/255, in their admitted local offline draws. The native
18-slot trail history, fade, texture, ribbon geometry and update timing remain
owned by `PlayerEntity`. There is no second emitter or presentation clock.

The color follows the successful source-ball submission in the current draw.
Cosmetics off, unsupported status, native morph transitions and retired source
contexts retain the native yellow trail. A captured source appearance alone
cannot authorize the tint. Defaults remain native; online/replay admission and
protocol83 are unchanged. Disabling the existing cosmetic restores native
presentation. This is a limited cosmetic slice, not complete character support.

## Source and implementation

Reference: pinned MIT donor `41452f2d053fcfa679e33c00a73d120a53f59e61`,
`src/MphRecomp.Core/Campaign/DarkSamusBody.cs`, shared Trail presentation for
both Echoes forms. Existing repository MIT attribution is retained.

Production freeze `dc82544c4b1c0d9763d037448b4f4fb4ead7e2cf` changes only
`PlayerDraw.cs`: retain `ModTryDrawSourceBall`'s result and pass an optional
color into the native `DrawMorphBallTrail`. Import recipes, generated files,
raw GX programs, source ball placement, collision, bombs and native effects
are unchanged. Base main is PR961 merge
`967a6f7a2d71413d9fe488fef1486d0f5fa3f29b`; no open PRs were returned during
the pre-delivery overlap check.

## Windows evidence

SDK10.0.401; Windows x64; NVIDIA RTX5080 / OpenGL4.6 NVIDIA617.42.
Tests first in `4c2f5a2677f9c43c763499ddd56f37b525b89e38` failed after47,227
checks against the old `da82cebd…ca12` core: a real normal ball-roll TrailStack
packet was `[1,.87096775,.3548387]`, expected `[.3529412,.78431374,1]`.
The complete native baseline passed before that failure. Raw failure and
diagnostic logs remain in ignored `artifacts/sh-integration/source-ball-trail-*`.

Qualified binaries (SHA256):

| Binary | SHA256 |
|---|---|
| Desktop core | `481e4cd0ec6370e1b3db6195f0d12c60dc9b80b3845e2b8f6614d361f37da12e` |
| Dedicated core | `5ddb7e731688b37c1c45c2099dd2298cea1b595ce5ef791e6e1c053d9b6b0dca` |
| Initial runtime/launch tool | `c547ad0b8d7e94f27248923fdbe359c43d3cd15d0868243d5366202c9499a3dc` |
| Nettest | `812fb633d5ba32cba7f5d5acd45734ca2aa4af443e4ae48ead0aa62b7fb43dfe` |

Desktop/dedicated Release builds pass, with the existing unused readiness
fixture field warning. An initial desktop `--no-restore` build reused server
restore assets and failed with missing Avalonia references; the retained
failure is superseded by the normal restored desktop build. No stale binary
was counted as a pass.

The new actual Windows Scene/GX trail probe passes589,288 checks. Launcher
selection/import/update/remove/relaunch passes66 checks on those same binaries.
Every native/normal/Final run contains the same1,415 completed-step gameplay
trace `f427384c4531686dfaef0d11cb0f35fce029e55dc4823bea0d85e11f136d6ca6`.
The separate trail trace is identical across all three:
`f7dae29fdb934da32955c6f0e04352d3a9f974d39c2b3b6a160ce5d7b813e945`.
It hashes all18 native alphas and ring cursor, matrices for active slots and
global RNG per completed script step. Inactive matrix storage is deliberately
excluded across independent Scenes; all18 matrices are checked within each
draw and repeated draw.

Native observes103 actual yellow trail pictures; each source selection
observes591 blue and3 native fallback trail pictures. These totals include
separately counted pause/guard/fractional/retirement checks beyond the original
script;103 pictures per selection are from input-driven rolling. No emitter
history is fabricated. Both appearances retain native bomb, injected death
and native respawn parity. Death is an explicit damage fixture, not an
opponent combat claim. Pause, repeated draws, shadow/cosmetic guards,
restoration, morph transitions, life reset and context retirement pass.
The90/120/240/540 schedules are simulated draw schedules, not achieved refresh.

A first-draw native charge effect advances global RNG even when no trail
packet exists. The initial overbroad fixture caught this; retained diagnostic
logs distinguish it from trail behavior. Draw-level no-RNG-advance assertions
apply when a trail packet exists, and complete native/source RNG progression
still matches in the1,415-step trace. Canonical trail history is checked on
every draw. Existing repeated-draw gameplay invariance remains checked.

Additional checks pass: desktop2,708 and dedicated2,690 CPU source-body
assertions;694 Quake III historical replay compatibility;971 offline character
policy; architecture/owner-position/full-snapshot byte fixture. No new ABI,
native bridge, asset recipe or replay format is introduced.

Capture-only follow-ups wait for actual native trail packets before saving
roll frames, finally requiring12 segments and suppressing unrelated cannon
captures in this mode. The final fixture commit is
`cb9e68538f06d1dbb70a3b9ed15306e01d9f6e51`; final tool SHA256 is
`bd905d71de43586757624d6d66343caa01aee6fe4574aabfed068caeb733b261`.
All14 screenshots across three isolated capture runs were visually inspected,
hashed and individually deleted. They show the source balls, arena and earlier
cannon frames; the axial trail remains obscured/not clearly distinguishable.
They therefore do not prove visible trail color. Actual TrailStack packet
values, geometry/history parity and successful native GPU rendering are the
qualified trail evidence. No screenshot or source payload is committed.

Owned Echoes RVZ SHA256 remains
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
Owned AMHE1 inventory remains
`c1c59fc4c406145310a324cd92a8a65cdfa01bd9bd783d1aa4f72629d1ba33f0`.
Probes use isolated user data and delete generated imports. Initial runtime
log SHA256 is `c463de4845069048b2a653e3f5f3daf647a6932dd54460e2724527b28a73a542`;
launch log is `850bca1a0daa20a8ec982eef8d53c0337e5cd4ecc86cfa0c0e8041887bc3266d`.

Commands use the named SDK, rebuilt desktop core and reference-disabled tool
builds, with `PRIME_BODY_TEST_ASSETS` pointing to the existing AMHE1 tree and
`ALSOFT_DRIVERS=null`:

```text
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-source-ball-trail-runtime <owned-Echoes-RVZ>
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-body-launch <owned-Echoes-RVZ>
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -sourcebodycheck
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --quake3-replay-compatibility
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --offline-character-gameplay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
```

## Remaining work

Normal's procedural core, boost/furl particle tint, animated guns, imported
drones/abilities, mixed-seat authority and online/replay integration remain
engineering. CPU sampling of the actual opaque normal shell found no visible
core hits across16,384 center rays and26 orthographic/perspective views for
either the donor source-relative radius or a native-radius-compatible.86R
core. This guides further work; it is not GPU proof or an exhaustive visibility
proof. Shell alpha/depth must not be changed merely to expose an added core.
The private metadata note/receipt is retained under
`artifacts/sh-integration/normal-ball-core-visibility`; no core is activated.

Other platforms and physical controls were not requalified. The user requests
this Windows machine going forward and defers Android testing.
