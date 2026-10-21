# CW-12b — exact package preparation before loading

Integrated base main `67fc37054` (includes CW-12a, Parkour v4 and normal-source
cannon work). Windows x64, SDK10.0.401/runtime10.0.12,
two .NET processors, one MSBuild worker, shared compilation disabled.
Protocol intentionally advances80→81 for package availability bit20 and separate
bounded offer/Ready/confirmation IDs99–101. Prior packet identities, native
payload bodies, CWM1 and historical movement attribution remain unchanged.

[Preparation contract](../12b-package-preparation.md): an operator-selected exact
installed revision is retained before the server runs. The ordinary client binds
the exact installed archive on a worker and retains leases before acknowledging
the complete recipient key. Match/epoch/configuration/slot generation/connection
and CWPK digest must match. Spectators prepare independently. Native loading,
world-admission acknowledgement and gameplay cannot bypass preparation. No
download, installation, grant or equip occurs. Package Start still refuses with
an explicit execution-unavailable reason after all peers prepare: actual online
definition/resource binding remains the next slice, preventing built-in fallback.

| Final local check | Observed result | Raw log |
| --- | --- | --- |
| Forced desktop Release | PASS98.18s; existing CS0649 only | `desktop-build.log` |
| Forced dedicated Release | PASS82.26s; same warning only | `server-build.log` |
| Desktop / actual dedicated preparation UDP | PASS126 each; exit0 | `final-custom/preparation-udp.log`, `server-probe/preparation-udp.log` |
| Desktop / actual dedicated CWPK binding | PASS657 each; exit0 | `final-custom/manifest.log`, `server-probe/manifest.log` |
| Dedicated startup rejection cleanup | PASS lease/prune/socket; exit0 | `server-probe/startup-failure.log` |
| Store / package / custom CPU | PASS99 /1,000 /37,015 | `final-custom/store.log`, `package.log`, `default.log` |
| Native architecture / replay compatibility | PASS /1,147 | `final-native/architecture.log`, `protocol58-replay.log` |
| Momentum / Quake3 / Traversal / Parkour / mantle contracts | PASS222 /255 /278 /66 /65 | `final-native/`, final Traversal: `traversal-rules-corrected.log` |
| Full ordinary lobby UDP suite | PASS7,745;99.66s; exit0 | `final-native/lobby.log` |
| Desktop / dedicated production native replay format | PASS3,050 each; exit0 | `final-native/production-replayformatcheck.log`, `server-replayformatcheck.log` |
| Actual-DLL registry/browser/status contracts | PASS142; exit0 | `final-native/browser-party.log` |
| Actual offline package GL integration regression | PASS49; native process exit0; empty stderr | `gl.stdout.log`, `gl-process.json`, `gl/` |

Final raw evidence is under `artifacts/custom-weapons/CW-12b/integrated-81/`.
Desktop/tool SHA-256:
`961a7092b479ed0f033269a15640bfc67ffe164908f45e6029d3ecc5b7b9656f`.
Dedicated source/copied/loaded SHA-256:
`ad853d659ccb569ea8aa12da432bc7cfb092de9fc90e76df0dcf543f8238c1f4`.
Hashes match before every final run. The dedicated inventory loads no Avalonia,
BEPU or CorpsePhysics. Product source remained frozen during final builds/tests.

UDP coverage includes genuinely missing local archives, cross-slot and altered
epoch/configuration/generation/connection/digest ACKs, a copied key from an
unadmitted endpoint, duplicate confirmation recovery, explicit unsupported Start,
late lobby spectator and replacement occupants, uninstall/prune retention, and
native-only no-op. The ordinary production NetSession client completes real
worker→Ready→confirmation, reports missing content, cancels/reconnects and releases
its handles. A held real store gate proves contradictory authenticated offers
cannot replace the worker. A declared pending retired-worker test seam proves
new connections cannot overlap an unfinished parser. This seam does not claim
physical parser timing. Server startup with unsupported Survival fails before
the loop and releases both retained bytes and the bound UDP socket. The GL
regression reuses the declared CW-11c new-ID magenta6/23 variant:20 ordinary
W-input steps acquire the world pickup, package reload leaves6/22, and actual
native collision pays37 body damage. NVIDIA RTX5080/OpenGL4.6/617.42 images
retain the raised magenta module; draw-only checks preserve simulation. This
does not substitute for online package gameplay or physical audio qualification.

Performance limits: one package-validation worker;247-byte offer and61-byte
Ready/confirmation payloads;0.5s offer/ACK retries. These are bounded contract
properties, not frame-time or WAN benchmarks. No native session creates a
package binding or worker. Resource decode/prewarm and actual package gameplay
performance remain later qualification.

Development attempts remain labelled: the initial CPU run found two old tests
classifying newly assigned bit20 as unknown; negatives now start at bit21, with
new explicit package/orphan-bit and all-protocol archive refusal coverage. The
first broad-lobby wrapper was interrupted while correcting a later unsupported
test-tool flag; the final lobby run completed naturally. That unsupported flag
was never executed or counted. A browser test command initially used its project
name instead of its declared `nettest` assembly; the corrected command passed.
Before publication, fetching main found that Parkour v4 had independently
occupied protocol80. Its changes were merged intact and package preparation
moved to81; historical80 retains Parkour v4 attribution. Earlier protocol80
candidate logs in the parent directory are pre-integration evidence, not final
head qualification. Integrated Traversal initially exposed main's stale profile
array expecting Parkour3; the fixture now expects the published4 and passes278.
No movement implementation was changed to satisfy it. A references-only tool
rebuild with `--no-restore` initially inherited server-only NuGet assets and
could not locate OpenAL; normal tool restore rebuilt it successfully without
recompiling/changing the qualified main DLL. Both attempts remain labelled.

Not full CW-12 acceptance: this slice does not start package gameplay, prewarm
presentation resources, allocate package hosts, enter a running package world,
enable custom bots or implement custom replay/seek. Existing custom/package
archive refusal remains explicit, including malformed bit20 without bit18.
Android builds/emulators/devices remain deferred; CI is neither awaited nor
claimed passing. Rollback: omit operator package configuration; no store schema
migration or native gameplay toggle changes exist. Protocol81 is an explicit
live compatibility boundary and cannot silently be relabelled80.
