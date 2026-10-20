# CW-04f evidence — developer authority contacts

Base: `487dc8e3fe13215f3f22579319324516d009b9e5`; latest main incorporated:
`835a5ba5a`. Production source tested: `7adf5d9b5`; later commits add tool coverage
and documentation only. Environment: macOS arm64, .NET SDK 10.0.401.

Scope: [authority contact contract](../04f-authority-contact.md). This is a developer
scene integration with an explicitly injected trusted source oracle. It is not
authenticated custom networking and does not complete CW-04.

| Check | Actual result | Log under `artifacts/custom-weapons/CW-04f/` |
| --- | --- | --- |
| Main Release build, `DOTNET_PROCESSOR_COUNT=2`, `UseSharedCompilation=false` | PASS, zero warnings/errors, 123.31 s | `build-current-main.log` |
| Fresh custom tool build, references disabled | PASS, zero warnings/errors, 1.84 s | `tool-retirement-final-build.log` |
| Pure custom contract checks | PASS, 3,308, including 1,203 payment checks | `contracts-retirement-final.log` |
| Actual developer authority scene | PASS, 985 checks | `authority-retirement-final.log` |
| Offline rifle scene | PASS, 46 checks | `offline-retirement-final.log` |
| Custom rewind/isolation scene | PASS, 156 checks | `rewind-retirement-final.log` |
| Native table snapshot against unchanged CW-00 | PASS | `native-tables.log` |
| Fresh nettest build; architecture, protocol58-replay, shot-source-journal, weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage, instagib-ion-fracture | PASS each | Named logs, `native-results.json` |
| Additional merged-main combat-fast-damage, combat-fast-decisions, combat-fast-receipts, claim-receipts | PASS: 64 / 52 / 481 / 698 checks | Named logs, `merged-native-results.json` |
| Actual native admitted shot scene | PASS, 343 assertions | `native-accepted-scene-final.log` |
| Actual native Imperialist impact scene | PASS, 949 assertions | `native-imperialist-scene-final.log` |
| Fidelity oracle all, unchanged baselines, content allowed, 60 Hz | 46 PASS; same single baseline divergence, exit 1 | `native-fidelity-final.log` |

Native fidelity retains the existing missile splash divergence at tick 177,
`victim.position4096.z`, expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baseline was regenerated or tolerance changed. Native packet/replay layouts,
protocol version and native weapon tables remain unchanged.

The authority fixture drives real custom Spawn/Process/collision/TakeDamage for 65
body and 150 scoped head damage, canonical death/score once, protected-zero contact,
one-use damage entry, direct/forged/spent refusal with native claim/proof policy
both enabled and disabled, missing rewind history and definite no-spawn refund,
private-pool saturation and exact terminal payment identity. A controlled native
ResolveCollision invocation establishes halfturret channel debit without claiming
free-flight turret geometry coverage. The eight-beam pool pressure is synthetic.

Native factory projectiles invoke actual lethal TakeDamage with declared launch
worlds to test equal-world trades and later source rejection. Native respawn keeps
old-life admitted flights payable and attributes generic assists to their firing
life while the new inventory remains untouched. Occupant replacement destroys the
unpaid flight and retires receipts. A later earlier native kill invalidates a flying
custom shot: its actual subsequent physical contact produces one TradeDenied zero
outcome, no damage event and no refund; another victim/channel cannot revive it.

Counter boundary fixtures seed sequence/history arrays to test exact ushort alias
and half-range pruning through actual custom contacts. Native history serialization
retains neutral custom attribution, canonical snapshots close custom holes, and the
next native event replays once. Merely retrieving a custom outcome does not advance
the native replay frontier.

An initial fixture failed because its hardcoded shot segment intersected room
geometry. The fixture now measures a clear body and head ray and explicitly sets the
stock unity match damage modifier. Damage expectations remain 65/150. No production
collision tolerance was relaxed. Earlier pre-merge authority runs (333 and 417 checks)
are provisional; final counts below refer to rebuilt tools against the merged game.

NOT_RUN: authenticated custom ingress/readiness, production source history/pose and
modifier admission, reliable outcome transport, reconnect/late join/spectator
replication, custom replay, loss/reorder and host/remote source fairness, graphics,
Android and physical device gates. Native tests do not certify those custom paths.
CI/review are neither awaited nor reported PASS. Rollback removes the explicit
developer activation; ordinary scenes do not construct the adapter or its storage.

Final lifecycle checks change all six excluded rules/mode, apply actual match/epoch
controls, and call actual scene cleanup with retained runtime/beam references. Each
refuses damage/admission immediately and permanently disposes the old runtime on
advance. A capacity fixture uses 32 real native respawn lives with actual terminal
body payments. The 33rd life cannot bind or evict the first receipt; retention is
inclusive through tick 256, and expiry frees capacity for a subsequent binding.
Native Spawn adds standing height to its supplied point; the fixture restores its
original measured clear standing positions after each real respawn, preserving the
65-damage body requirement. Collided presentation beams are destroyed between lives
to isolate retained-store capacity from synthetic projectile pressure.

Two separate warmed 100,000-call fixed-frame `Advance` batches, retaining an actual
unpaid flight and an actual paid outcome respectively, each allocate **zero bytes**
on the measuring thread and preserve exact resources/projectile/payment state.
These are bounded hot-path allocation checks, not full-match CPU or platform proof.
The default 128-entry store now measures 34,800 allocated bytes with dependencies
prebuilt. Whole-scene peak memory/percentile CPU remain release measurements.
