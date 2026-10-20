# CW-04i evidence — endpoint-owned opt-in readiness

Base/latest main: `8e98bb0b1` (including historical replay fixture repairs and
native profiler tail retention). The readiness implementation is `87b17e610`;
`1e669ad16` adds only tools/documentation after it. Environment: macOS arm64,
.NET SDK 10.0.401. Scope: [readiness contract](../04i-live-readiness.md).

This slice supplies the authenticated endpoint/bootstrap capability exchange for
the built-in M7. It neither enables combat nor supplies a source-frame boundary.
The default-off server/client configuration has no public launcher or CLI switch.

| Check | Actual result | Local log under `artifacts/custom-weapons/CW-04i/` |
| --- | --- | --- |
| Main Release build before latest-main integration | PASS, zero warnings/errors, 77.84 s | `build-consistent-clock.log` |
| Fresh custom tool Release build, project references disabled | PASS, zero warnings/errors, 1.83 s | `tool-final-build.log` |
| Pure contracts | PASS, 3,957 checks | `pure-final.log` |
| Engine admission / source / authority scenes | PASS, 249 / 318 / 985 checks | named `*-final.log` |
| Offline / rewind scenes | PASS, 46 / 156 checks | `offline-final.log`, `rewind-final.log` |
| Fresh nettest Release build, project references disabled | PASS, zero warnings/errors, 2.43 s | `nettest-final-build.log` |
| Real UDP readiness, manual clients and actual NetSession client | PASS, 134 checks | `custom-readiness-udp-final.log` |
| Native table parity against unchanged CW-00 | PASS | `native-tables.log` |
| Fourteen native contract suites | PASS each | `native-results.json`, named logs |
| Native admitted shot / Imperialist impact scenes | PASS, 343 / 949 assertions | `native-accepted-scene-final.log`, `native-imperialist-scene-final.log` |
| Fidelity oracle, unchanged baselines/content allowed/60 Hz | 46 PASS, one unchanged baseline divergence; exit1 | `native-fidelity-final.log` |
| Packet registry check | PASS, old 78 names unchanged, protocol61 and IDs83–85 registered | `python3 tools/group-reservation-check/check_registry.py` |

The fourteen native suites are architecture, protocol58-replay, shot-source-journal,
weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage,
instagib-ion-fracture, combat-fast-damage, combat-fast-decisions,
combat-fast-receipts, claim-receipts, timed-grants and timed-shot-certificates.
Full command arrays/timings are in the local result JSON. Commands use the local
.NET SDK, Release, `-p:UseSharedCompilation=false` and processor count 2; tool builds
add `-p:BuildProjectReferences=false` against the freshly built game DLL.

The pure suite independently compares the M7 row/digest/118-byte manifest and
72-byte acknowledgement to fixed golden bytes. It rejects unknown, extra and
modified packages; checks bootstrap, recipient, connection, conflicting messages,
retries, confirmation ordering, inclusive 30-second deadlines, immutable opening
world and 16-per-second work budget. Warmed 100k duplicate/lookup/mismatch/flood
probes allocate zero bytes on the measuring thread.

The UDP suite verifies default-off behavior, two independent peer handshakes,
cross-slot and copied-connection rejection, dropped confirmation recovery,
duplicate idempotence, disconnect/replacement, stale bootstrap and contradictory
acknowledgements. Its actual NetSession client completes native confirmation,
receives and validates the manifest, acknowledges it and observes the authority's
confirmation. Another admitted endpoint sends a corrupt current-bootstrap page;
the client host filter ignores it and retains the real host's capability. Stop
clears capability. An empty synthetic native baseline substitutes for constructing
a remote scene, so this is a control-plane test, not networked combat or respawn.
A native lifecycle packet proves that the capability does not grant a new loadout;
it does not simulate a new engine life.

A first-retry clock mismatch discovered during validation was corrected by sharing
the sampled wall timestamp between cache construction and message processing.
The final UDP run copied the exact final game DLL (SHA256
`9ef03a21a8ad3a689135b3d8b1d7654e2d137c27a4a4aa624294c5a86dd18699`).

Fidelity retains only `weapon.missile.splash`, tick177,
`victim.position4096.z`, expected52814/actual52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baselines/tolerances changed. Native payload widths/checkpoint versions remain
unchanged; the live datagram version intentionally becomes61. Protocol60 discovery
and historical replay bodies are explicitly retained; live old-version Hello is
refused with the existing mismatch reason.

NOT_RUN / remaining: live source-boundary bridge and automatic life binding,
custom action/outcome/snapshot transport, missing/coalesced input recovery,
local/bot source parity, latency/loss/reorder combat, reconnect/late-join/spectator
custom state, custom replay and physical/rendered platforms. CW-04 remains open.
CI/review are neither awaited nor reported PASS.

Latest-main integration: clean rebase onto `8e98bb0b1`, main Release build PASS,
zero warnings/errors in 89.07s (`build-latest-main.log`). Fresh custom tool build
PASS in2.13s (`tool-integrated-build.log`); all six modes pass unchanged against
that integrated binary, recorded in `custom-tool-integrated-results.json` and
`*-integrated.log`. Additional transport/native checks against the preceding
same-readiness production build pass: reliable, queue-budget, lobby7,752,
waitlist309, Quake III rules175 and replay compatibility284
(`additional-native-results.json`, `additional-*.log`). The latest-main production
change is native profiler tail retention; the other integrated changes repair
historical replay fixtures and record performance evidence.
Fresh integrated nettest build also passes, zero warnings/errors in2.67s
(`nettest-integrated-build.log`). Integrated UDP134, architecture,
protocol58 replay285, Quake III rules175 and replay284 all pass;
`integrated-native-results.json` records exact commands and `integrated-*.log`
contains full output. Both tool copies match the integrated game's SHA256
`8ef96eb0534f08d98bbaf3b792e343e5fffcb3a1920f6e2ac908ea4ddaf55df9`.
