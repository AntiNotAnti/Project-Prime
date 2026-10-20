# CW-04g evidence — engine source observation phase

Base: `47d446e22c8a95e694cee241405e449780220da6` (CW-04f).
Latest incorporated main: `3c9a53d53`; merged production source: `99c6597c7`.
Environment: macOS arm64, .NET SDK 10.0.401.
Scope: [engine observation contract](../04g-engine-observations.md).

The explicit developer adapter records immutable remote engine observations before
its deferred native accepted-projectile drains. It still receives a trusted external
source oracle. This slice does not establish custom action admission or networking.

Production review identified two cases before acceptance: an input exception must
release the phase without publishing partial observations; and frozen rejected
placement must still cancel custom reload without fabricating accepted geometry.
The final implementation handles phase cleanup in finally and retains immutable
negative permission cells separately from accepted poses.

Validation commands use `/Users/jarrett/.dotnet/dotnet`, Release,
`-p:UseSharedCompilation=false`; tool builds also use
`-p:BuildProjectReferences=false`. Main compiler source is frozen during each build.
Full local logs reside under `artifacts/custom-weapons/CW-04g/`.

The source history tests explicitly supply detached engine facts, and do not call
those facts authenticated input proof. Real scene tests use native lifecycle carrier
intake and actual placement/control handling; native inventory/proof seeds are
explicitly declared where required to exercise accepted projectile drains. Their
endpoint authentication and network loss are not exercised by an in-process carrier.

NOT_RUN: historical modifier/ACK/muzzle admission, readiness/bootstrap transport,
local/bot source-phase parity, missing/coalesced source recovery, custom UDP/outcome
reliability, reconnect/late join/spectator replication, custom replay reconstruction,
physical devices and GL/audio. Neither native tests nor observation metadata satisfy
those gates. CW-04 remains in progress. CI/review are not awaited or reported PASS.

| Final check | Actual result | Log in local CW-04g directory |
| --- | --- | --- |
| Main build against merged main | PASS, zero warnings/errors, 68.42 s | `build-current-main.log` |
| Fresh custom tool build | PASS, zero warnings/errors, 0.59 s | `tool-final-build.log` |
| Pure contracts | PASS, 3,684 checks | `contracts-final.log` |
| Engine source scene | PASS, 318 checks | `engine-source-final.log` |
| Authority combat scene | PASS, 985 checks | `authority-final.log` |
| Offline rifle scene | PASS, 46 checks | `scene-final.log` |
| Rewind isolation scene | PASS, 156 checks | `rewind-boundary-final.log` |
| Native tables against CW-00 | PASS | `native-tables.log` |
| Fresh nettest build and twelve network/combat suites | PASS each | `native-results.json` and named logs |
| Native accepted shot / Imperialist impact scenes | PASS, 343 / 949 assertions | `native-accepted-scene-final.log`, `native-imperialist-scene-final.log` |
| Fidelity oracle all, unchanged baselines, content, 60 Hz | 46 PASS; same single known divergence; exit 1 | `native-fidelity-final.log` |
| Whitespace check | PASS | `git diff --check` |

The twelve native suites are architecture, protocol58-replay, shot-source-journal,
weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage,
instagib-ion-fracture, combat-fast-damage, combat-fast-decisions,
combat-fast-receipts and claim-receipts. Their scope remains their native contracts.

The asset-backed source suite verifies the automatic ServerSim/Renderer hook without
an extra step failure. A real native lethal Imperialist carrier remains Pending
through both actors' actual remote input processing, then becomes Fired only after
all source observations freeze. Subsequent actual death cannot rewrite the victim's
pre-projectile observation. Declared native resource/proof seeds are explicit.
An aborted phase (`capture:false`) publishes no observation while releasing the
native drain; actual scene cleanup mid-phase discards its actor references. This
exercises the exception-cleanup mechanism; no throwing Renderer callback is injected.

Actual frozen placement records a negative permission without an accepted pose.
Reload begun before the freeze is canceled with no ammo transfer, stays canceled
through thaw/reconsumption and past its original deadline, and remains canceled when
a later trusted fixture action commits. The negative cell cannot be promoted later.
The helper's restriction and geometry checks preserve native resources.

Pure checks cover exact identity, first facts/time immutability, conflicting raw
input tombstones, absent/negative/expired/evicted frames, fixed-anchor clock limits,
shared occupant clock across lives and uint wrap. Three warmed 100,000-iteration
batches—exact/missing lookup pairs, duplicate positive observations and duplicate
negative observations—each allocate zero bytes on the measuring thread. These do
not establish whole-scene CPU or platform performance.

Native fidelity retains only missile splash at tick 177, `victim.position4096.z`,
expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baselines or tolerances changed. Main's incorporated Quake III changes use
protocol **60**; this slice adds no packet, protocol or replay shape change.
