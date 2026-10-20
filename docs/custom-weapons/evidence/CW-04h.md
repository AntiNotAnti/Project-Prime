# CW-04h evidence — engine-derived remote admission

Base/latest main: `f274cc8ee58b99ca60624423fd44f67e980f33ca` (CW-04g).
Tested production source: `d7b0183cf`, including typed grant lifecycle extraction
`a04b1e9c1`. Following commits add tools/documentation only.
Environment: macOS arm64, .NET SDK 10.0.401.
Scope: [engine admission contract](../04h-engine-admission.md).

The source certificate now comes from actual retained engine observations and
canonical grant evidence in the alternative developer entry. Readiness/bootstrap is
an explicitly trusted fixture dependency. There is no custom packet handler or
public activation, and no claim that this completes CW-04 networking.

| Final check | Actual result | Local log under `artifacts/custom-weapons/CW-04h/` |
| --- | --- | --- |
| Main Release build, processor count 2, shared compilation disabled | PASS, zero warnings/errors, 73.37 s | `build-readiness-retirement.log` |
| Fresh custom tool build, project references disabled | PASS, zero warnings/errors, 1.57 s | `tool-final-build.log` |
| Pure contracts | PASS, 3,754 checks | `contracts-final.log` |
| Engine admission scene | PASS, 249 checks | `engine-admission-final.log` |
| Engine source / authority scenes | PASS, 318 / 985 checks | `engine-source-final.log`, `authority-final.log` |
| Offline / rewind scenes | PASS, 46 / 156 checks | `scene-final.log`, `rewind-boundary-final.log` |
| Native table parity against unchanged CW-00 | PASS | `native-tables.log` |
| Fresh nettest build and fourteen native suites | PASS each | `native-results.json`, named logs |
| Native admitted shot / Imperialist scenes | PASS, 343 / 949 assertions | `native-accepted-scene-final.log`, `native-imperialist-scene-final.log` |
| Native timed-shot real-contact scene | PASS, 6,464 assertions | `timed-shot-native-scene.log` |
| Native modifier/payment scene | PASS, 5,493 assertions | `timed-shot-modifiers-scene.log` |
| Fidelity oracle all, unchanged baselines, content allowed, 60 Hz | 46 PASS, one unchanged baseline divergence; exit1 | `native-fidelity-final.log` |
| Whitespace | PASS | `git diff --check` |

The fourteen native suites are architecture, protocol58-replay, shot-source-journal,
weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage,
instagib-ion-fracture, combat-fast-damage, combat-fast-decisions,
combat-fast-receipts, claim-receipts, timed-grants and timed-shot-certificates.
Commands use `/Users/jarrett/.dotnet/dotnet`, Release and
`-p:UseSharedCompilation=false`; tool builds add
`-p:BuildProjectReferences=false`. Full command arrays are retained in result JSON.

The positive admission scene supplies no source certificates. Native lifecycle
carrier intake and real placement/controls create G observations; the production
engine oracle accepts journal actions and emits the real private-pool M7 through
canonical collision/damage. Base removal is 65. A genuinely initialized native
Double item is placed for actual complete native player-step contact; the real
source grant chronology then authorizes 130 custom removal. Owner Double/Prime hints
alone leave removal 65. Native history/shot/payment stores remain isolated from the
custom action even with matching numeric sequence IDs.

Negative cases cover exact captured ACK/subframe, an ACK future at observation but
no longer future at delayed inspection, aim/muzzle disagreement, missing previous
release, held fire, denied/expired source, missing history and readiness boundaries.
An invalid replacement encountered by CanCapture permanently poisons a pinned
context even if the trusted fixture restores the old value before Inspect. Initial
unavailable/future readiness never publishes a source. Tests preserve exact denial
or pending behavior; no numerical tolerance or damage expectation was relaxed.

Pure modifier checks compare native and typed lifecycle readers for all three grant
types, contact NotGranted/C+1 Granted, original funding surviving renewal/expiry,
missing/unknown/other-life/world/room evidence, deliberate funding corruption and
missing records, immutable history modifiers, uint wrap and read purity. Native
ShotKey remains only on native grant setup/records; custom observation lookup uses
the nominal lifecycle key. Warmed 100k typed read/funded capture/missing capture
probes allocate zero bytes on the measuring thread. These are not scene CPU or
physical platform measurements.

Native fidelity retains only missile splash at tick 177, `victim.position4096.z`,
expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baselines/tolerances changed. Native tables, protocol 60 and packet/replay layouts
are unchanged by this slice.

NOT_RUN / remaining: authenticated custom readiness/action/outcome transport,
server-owned production readiness bridge, local/bot source-phase parity, reliable
missing/coalesced input recovery, loss/reorder/host fairness, snapshot/reconnect/
late-join/spectator state, custom replay and physical/rendered platforms. The strict
preceding-frame proof can refuse a real fire whose release input was omitted by
native coalescing; this is unavailable evidence, not permission to invent it.
Historical geometry is the existing sampled-world contract, not a retained exact
pre-projectile door state. CI/review are neither awaited nor reported PASS.
Rollback keeps the developer alternative disabled; ordinary matches allocate no
custom admission/history/payment adapter.
