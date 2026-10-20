# CW-10b evidence — independent affine pose handoff

Base: latest main `0a4cd55a14996302bdca584dd3aac29419d9d620` (PR906).
Head: completed-slice commit associated with this evidence/PR; no CI claim/wait.
Scope: two new non-shipping leaf math classes, standalone checks, actual native
CPU fixture and separate Android test app. No shipping game/server source or
project/dependency changed. [Design](../10b-affine-pose-handoff.md).

SDK10.0.401/macOS arm64/runtime10.0.12; DOTNET_PROCESSOR_COUNT=2, single MSBuild
worker, shared compilation off. Native fixture reads owned AMHE1 assets with an
isolated PROJECT_PRIME_USER_DATA and readonly paths fixture. Raw commands,
failures, receipts and result hashes: `artifacts/custom-weapons/CW-10b/`.

Core DLL SHA256:
`d6101c88a488eeff6d58c864fca8b3f2f154570ecf33173b0a8fcb18100b01f5`.
Native tool SHA256:
`6dfe51b66a9df7f2db9fdfb8ebaa6df4beac1750c131f244dfd0a4f48e0bfcab`.
Existing engine DLL used by the native CPU fixture is the unchanged CW-10a build
`0f03fa9fc780355d29797425294ff129d3a22aed4f2203d1f6df44765f8ac0c2`.
Later merged main launcher/scoreboard changes are outside this math fixture;
this slice does not claim a new full game/server build or their UI acceptance.

| Check | Actual result |
|---|---|
| Leaf Release / native tool | PASS0 warnings/errors |
| Independent synthetic affine/map contracts | PASS108 affine +45 map=153,0.237s; immutable copy, atomic invalid refusal,128-node bounds, quaternion normalization/sign, scale/shear, zero-scale, rigid covariance, concurrent readers and100k warmed0B loops |
| Full standalone pilot | PASS1,505 contracts,45 CPU workloads,128 world/1,024 corpse lifecycle stress;0 warmed step/sample bytes and final arena0B |
| Actual native CPU pose bridge | PASS10,964 checks/1,092 pose cases/300 scaled cases,0.454s;14 native model/LOD profiles ×26 animation banks ×first/mid/last frames |
| Native affine numeric bounds | Maximum matrix element error3.5762787e-7 at original pose;1.0728836e-6 after common rigid motion |
| Existing full custom CPU regression | PASS35,726 checks,14.817s; no new live transport or graphics claim |
| Android Release arm64 Mono AOT | PASS19.194s build,0 warnings/errors;24 ARM64 AOT modules, executable-mapped core/BEPU images |
| Actual Android bridge checks | PASS108+45=153, firstFailure=null on owned API36 arm64 emulator5582 |
| Actual Android existing proxy matrix | PASS45 workloads/128 worlds/1,024 corpses;47.009s Activity wall includes bridge checks plus benchmark; runner81.413s includes boot/install |
| Android package | PASS permission-free, notices included, separate test package and explicit test-only debuggability |

The native CPU test uses fifteen independently oriented frame carriers placed
near current actual joints. They are not collider dimensions/joints or a physical
skinning qualification. It captures every LOD independently, preserves full native
affine matrices, verifies isolated head/helper motion, and checks exact invariance
of source nodes/palette/skin bindings and gameplay RNG. Later animations and caller
matrix/map mutations cannot overwrite an earlier owned binding. All profiles
truthfully retain PhysicalQualified=false. No native/modern/HD GPU draw, game
corpse lifetime, accepted combat authority or physical replay is claimed here.

A first mapping rejected Trace/Noxus because their head and shoulder paths share
a Collar helper. The RED is retained (`affine-pure/run-map-red.log`). Shared
helpers now remain torso-owned while exclusive Neck/collar branches follow their
own body. Both explicit topology regression and actual14-profile tests pass.

Android signed APK SHA256:
`d80ddc196aff9732b91551fe4b5f7c269c30eb61868807e6ae103c441f7882a2`.
Full target/build/source provenance and per-check counts live in
`android/final-results.json`, `apk-inventory.json` and
`release-run/bridge-check-results.json`. No Hunter asset is packaged in this APK.

All45 Android warmed step/sample rows report0B. The reused4KiB-block lifecycle
arena plateaus and releases to0B. Timings remain variable: this run's8-corpse,
60-scheduled-Hz physics step p50/p95/p99 is1.905/7.441/13.347ms, maximum44.338ms.
Retain that result separately from CW-09's faster run; no cause or code regression
is inferred without controlled same-scene repeated baselines. It is emulator CPU
work, not a physical-phone/full-game frame delta or release budget approval.

The emulator initially failed its disk guard. Only completed generated build
intermediates and redundant private probe binaries were removed from this owned
worktree; sources, APKs and acceptance receipts remained. Once space was available,
owned5582 ran successfully, then only that test package/emulator was stopped.
Other target5580 was untouched. Failed launch logs and cleanup manifests remain.

CW-10 remains ongoing. Actual collider fit/static geometry export, accepted-life
render owner, multi-actor GPU palettes, moving surfaces,100+ integrated respawn/
scene cycles, physical devices and target frame budgets remain subsequent gates.
The game still uses the bounded CW-10a/CW-08 fallback; no physics option is enabled.
