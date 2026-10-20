# CW-05i evidence — Android offline Rules save owner

Base `6e2287e860fcb31b2231093a0c3807261ef8d820`, latest fetched main at
qualification, including upstream native canvas PR840. Its changes are not
claimed as authored here. [Contract](../05i-android-offline-rules.md).

macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`. Integrated desktop
Release build PASS66.099s, zero warnings/errors. Main SHA256:
`eaff175de6ea7d69fdf5d15a424dc0c42f7f9170de7a173972ca5d0af90151ed`.
Integrated Android Debug builds PASS: experiment=true64.076s (26 warnings),
experiment=false66.091s (25 warnings), zero errors. Both include actual Android
head/shared sources and produce APK output; no emulator/device execution or
packaged native RmlUi renderer acceptance is implied. The warnings are retained
in raw logs. Sources were frozen throughout builds; tools use
`BuildProjectReferences=false`. Before/after runtime hashes remain exact.

| Integrated acceptance | Result | Metadata |
| --- | --- | --- |
| Actual async menu controller/bindings/cache | PASS67 | ui-focused-results.json |
| Actual Android dispatch/cache/commit-helper source; fake UI queue/writer/admission | PASS36 | same |
| Existing Adventure async controller + dispatch | PASS; dispatch7 | same |
| Actual Avalonia StartScreen + real settings persistence | PASS14 | ui-integrated-results.json, rules-save-ui/rules-save-ui-evidence.json |
| Ordinary RmlUi runtime/offline rules/live-menu | All3 build/test pairs PASS; upstream managed canvas197 | ui-integrated-results.json |
| Custom pure contracts | PASS14807 | 05i-integrated-custom-results.json |
| Actual offline availability/window launch | PASS18/17 | same |
| Scope/local/offline/public-client | PASS37/28/46/24 | same |
| Selection/projection/replica | PASS180/145/173 | same |
| Readiness/admission/source/authority/rewind | PASS127/249/318/1014/156 | same |
| Focused synthetic controller | PASS75 | same |
| Actual NetSession/native isolation/native shot/Imperialist | PASS75/680/343/949 | 05i-integrated-nettest-results.json |
| Other architecture/protocol/replay/movement/pickup/target/retirement/chat/reliable | All14 commands PASS | same |
| Registry/native tables | PASS78; old IDs/names/tuning unchanged | final-ancillary-ancillary-results.json |
| Native fidelity | Known46/47; raw exit1, exact established divergence | same |

Complete commands/raw exits/hashes/timing are under
`artifacts/custom-weapons/CW-05i/`. All17 custom and19 nettest commands pass.
The focused async executable is deliberately run with `--offline-rules-async`:
it uses actual linked controller/Host/Android worker sources and copied immutable
rules, with declared fake UI queue/admission/writer. It verifies one pending
request, immutable expected/next values, disabled fields/actions, cancellation,
duplicate callback exactly once, stale/lobby/disposed epochs, exact result and
current/cache readback, sanitized exceptions, post-effect cancellation retaining
the committed cache, guarded recapture after a write-side exception, explicit
unknown cache, and dropped-post bounded deadline. MainActivity/Activity predicates
are source-reviewed and compiled, not executed by this fake queue.

The actual Home test runs on Avalonia's UI dispatcher and persists isolated
settings.json through GameState.CommitSettings. It confirms M7/Momentum and
current unrelated Language/Sfx/Room values on disk; stale expected values, invalid
rules and cancellation preserve exact bytes. A directory at settings.json causes
the real writer to fail and the whole rule subset to roll back. The real import/
reset fence refuses writing; only that isolated process's captured flag is
restored on teardown. Startup completion is explicitly staged, not qualified.

Corrections are not hidden. The first production build failed on a switch-local
`change` name collision; the renamed local and all subsequent main/Android builds
pass. The Home test initially lacked a Menu type alias. The older async tool
needed its existing friend assembly name and copied identity.rml content after
linking newer menu fixtures. Its broad linked-theatre run then failed because a
reflection fixture looked for ReplayAnnotations in the source-linked tool
assembly. That unrelated broad run remains FAIL; ordinary live-menu acceptance
passes against the real engine. The explicit focused route qualifies this slice
and existing Adventure async behavior without claiming a broad linked-tool PASS.
Pre-integration4a9 candidate checks/builds remain distinct from final eaff results.

The Android workload/SDK were already present. A compatible Microsoft OpenJDK21
archive was obtained from the [official download page](https://learn.microsoft.com/en-us/java/openjdk/download)
and verified against its SHA256:
`6704f1372a02fabcc9e1efec84f63fcf4d9c1c904e2b8e29b04bed2fb62f7e6e`.
It is confined to ignored tooling artifacts; no global Java installation changed.
The initial Python extraction API was unavailable in the installed Python; the
verified archive was extracted with system tar. Provenance and build arguments
are recorded, and no tool archive/APK/game assets enter Git.

Native fidelity still differs only at `weapon.missile.splash`, tick177,
`victim.position4096.z` expected52814/actual52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Physical Android/controller, portrait fallback usability, WAN and custom replay
remain unqualified. The emulator inventory contains two saved AVDs but no
running/attached device; neither was modified or started for this slice.
CW-05 continues with accessible in-game custom/native selection. Full CW-16 is
not complete. CI/review are neither awaited nor reported passing.
