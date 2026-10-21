# CW-12b — exact package preparation before loading

Base main `a2a177a29bbae6ef0fe112c859820fc7301871ba` (includes CW-12a and
concurrent imported-body ledger work). Windows x64, SDK10.0.401/runtime10.0.12,
two .NET processors, one MSBuild worker, shared compilation disabled.
Protocol intentionally advances79→80 for package availability bit20 and separate
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
| Forced desktop Release | PASS122.58s; existing CS0649 only | `desktop-build-final.log` |
| Forced dedicated Release | PASS68.09s; same warning only | `server-build-final.log` |
| Desktop / actual dedicated preparation UDP | PASS126 each; exit0 | `final-custom/preparation-udp.log`, `server-probe/preparation-udp.log` |
| Desktop / actual dedicated CWPK binding | PASS657 each; exit0 | `final-custom/manifest.log`, `server-probe/manifest.log` |
| Dedicated startup rejection cleanup | PASS lease/prune/socket; exit0 | `server-probe/startup-failure.log` |
| Store / package / custom CPU | PASS99 /1,000 /37,014 | `final-custom/store.log`, `package.log`, `default.log` |
| Native architecture / replay compatibility | PASS /1,104 | `final-native/architecture.log`, `protocol58-replay.log` |
| Momentum / Quake3 / Traversal / Parkour contracts | PASS222 /255 /278 /47 | `final-native/` |
| Full ordinary lobby UDP suite | PASS7,744;102.16s; exit0 | `final-native/lobby.log` |
| Production native replay format | PASS3,050; exit0 | `final-native/production-replayformatcheck.log` |
| Actual-DLL registry/browser/status contracts | PASS141; exit0 | `party-final-corrected.log` |

Raw evidence is under `artifacts/custom-weapons/CW-12b/`. Desktop/tool SHA-256:
`128dcf16f650fb19fb15eefd5293fc4eb0b095b46162e880950d91114378f012`.
Dedicated source/copied/loaded SHA-256:
`b23848beec4766423a40758817ef556b5952e5684bd29337049a0d54ce64f778`.
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
the loop and releases both retained bytes and the bound UDP socket.

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

Not full CW-12 acceptance: this slice does not start package gameplay, prewarm
presentation resources, allocate package hosts, enter a running package world,
enable custom bots or implement custom replay/seek. Existing custom/package
archive refusal remains explicit, including malformed bit20 without bit18.
Android builds/emulators/devices remain deferred; CI is neither awaited nor
claimed passing. Rollback: omit operator package configuration; no store schema
migration or native gameplay toggle changes exist. Protocol80 is an explicit
live compatibility boundary and cannot silently be relabelled79.
