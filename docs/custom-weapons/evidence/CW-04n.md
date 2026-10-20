# CW-04n evidence — read-only authority replica capture

Base main `0edd15e7e` (CW-04m). Scope: [capture contract](../04n-authority-replica-capture.md).
Environment: macOS arm64, .NET10.0.401, DOTNET_PROCESSOR_COUNT=2.
Final game and both tool-copy DLL SHA256:
`a2d0755690b73b64706e1eb3185ccd599e76b74561c6024b326842a3a6947ee5`.
Protocol remains65; no registry, archive or existing packet shape changes.

The new API atomically copies eight explicit slot rows and up to64 actual active
private flights. It retains original source life alongside current replacement
life state, represents absent and occupied Life0 spectator slots, and borrows no
mutable engine objects. Capture never grants inventory, consumes source evidence,
advances payment stores, expires leases or mutates canonical health. It is not yet
a public replication transport or spectator combat activation.

| Check | Actual result | Evidence under artifacts/custom-weapons/CW-04n |
| --- | --- | --- |
| Final main Release build | PASS79.49s; zero warnings/errors | build-capture-final.log |
| Fresh nettest / custom tool builds | PASS3.572s /2.31s; zero warnings/errors | n-registry-nettest-build.json, n-final-custom-results.json |
| New real authority replica suite | PASS173,0.900s | n-final-custom-results.json |
| Existing pure contracts | PASS9025,2.004s | same |
| J/H/G/F authority scenes | PASS127/249/318/985 | same |
| Offline / rewind scenes | PASS46/156 | same |
| Twelve registry/native modes | All PASS | n-registry-native-results.json |
| Native tables and fourteen combat modes | All PASS | n-native-results.json |
| Native accepted / Imperialist scenes | PASS343/949 | n-content-results.json |
| Fidelity | 46 PASS; known single divergence | native-fidelity-final.log |

Replica tests run in the actual asset-backed authority scene with the existing
explicit trusted source fixture. They compare the captured values against real
private projectiles emitted through the native factory, including hip-spread
direction distinct from certified aim. Age/lifespan are actual seconds; velocity
is displacement per simulation step. A first fixture incorrectly expected an
active projectile after a six-unit-clear-path probe: M7 travels200 world units per
step and hit the room wall. The fixture now observes the genuine emission before
stepping, at actual age0/lifespan0.1. Native wall-collision aftermath is tested
separately and must be excluded. Production speed/physics were not changed.

Additional cases cover insufficient full-bound spans and caller-tail canaries,
invalid late-flight staging without partial output, scratch/reentry cleanup,
source-phase/disposed refusal, positive-lifespan terminal player and world impact
effects, future payment-deadline queries without state advancement, actual native
respawn retaining an old-life flight beside new-life magazine4, spectator
retirement, and real BeginShot historical reconciliation followed by ResetSlot.
One hundred thousand warmed captures allocate0B on the measuring thread and
preserve ammo, source clock, payment frame/quarantine/retirement/high-water,
canonical HP/event sequences and physical fields. Fixture setup deliberately uses
trusted source certificates; no new UDP, rendering or physical-device proof is
claimed by these scene checks.

Review found two defects in the initial capture. A wall-hit visual retained
positive lifespan and a Flying payment, and ResetSlot could clear the collision
cache while the native rewind scope remained open. Focused tests reproduced both
RED against preflight DLL
`dbd8cff0afcd1da223482b2e9f2f82997b44d19be2f974251695b5d2bdde5925`;
commands/hashes/logs are in n-replica-red-results.json. Final guards reject
Collided effects and the complete native shot/rewind/cache scope. Both cases pass
against the final DLL. The new native property is read-only and has no consumer
outside the new capture API.

Registry fixtures preserve all78 old names, IDs80–93 and protocol65. Upstream
baseline JSON and its packet/event/shot digests remain equal to CW-04m. Fidelity
remains only missile splash tick177 victim.position4096.z,
expected52814/actual52815. No tolerance/baseline changed. Imperialist headshot F2
remains `6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Parent command/hash records are n-parent-validation.json. The content runs were
repeated after confirming the fresh nettest copy; an earlier unattributed pass is
preserved separately and excluded from final acceptance.

Remaining CW-04 gates: live recipient-owned fanout and atomic baseline/ACK,
late join/reconnect/spectator delivery, local/bot source parity and final host/
remote fairness. Public input/HUD, custom replay, rendered/physical Android,
WAN and p50/p95/p99 are NOT_RUN. CI/review are neither awaited nor reported PASS.
Ordinary rooms do not instantiate this runtime; rollback is both developer flags
off. CW-05 remains gated on the rest of CW-04.
