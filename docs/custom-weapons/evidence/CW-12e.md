# CW-12e — exact remote package allocation

Compiled source `bc4536344d189266e37c0a9d0bebfdd09517b131` combines product
commit `d41901544` with main `b8b0cd84a` (PR955). Final observations are under
`artifacts/custom-weapons/CW-12e/integrated/`; earlier observations stay under
`development/`. See the [allocator contract](../12e-remote-package-hosting.md).

Forced nonincremental desktop Release passed in87.41s, SHA-256
`13ddd6ca4153783fad29b95234d96d69f015967fa8003888a7c0801620597a01`.
Actual dedicated Release passed in64.74s, SHA-256
`f5fe03321fcbfe9fd799feda2e38512a4aa365c58377118fb4d887a1e5ce1b6d`.
Each build reports one existing unused fixture-field warning and zero errors.
Test-tool copies, dedicated probe and private child copies match their intended
assemblies. Builds used the private SDK10.0.401 with two processors, one MSBuild
worker and shared compilation disabled. Main source remained frozen during
the builds and observed runs. No binary was replaced while being executed.

| Final local check | Observed result | Log below integrated/ |
| --- | --- | --- |
| Directory / dedicated allocator lifecycle | PASS29 each; exit0;6.0594s /4.7983s; client stderr empty | `host-directory/`, `host-dedicated/` |
| Directory / dedicated allocated ordinary GL client | PASS43 each; exit0;11.1867s /11.1805s; client stderr empty | `gl-directory/`, `gl-dedicated/` |
| HostRequest wire and challenge fingerprint | PASS273 on desktop and actual dedicated assembly | `native-regressions/weapon-package-host-request.log`, `server-probe/request.log` |
| Worker/cancellation/ownership and no-I/O retain | PASS38 on desktop and actual dedicated assembly | `native-regressions/weapon-package-host-preparation.log`, `server-probe/preparation.log` |
| Exact manifest / actual preparation UDP | PASS678 /146 on both assemblies | `native-regressions/weapon-package-*.log`, `server-probe/` |
| Desktop / actual dedicated package engine | PASS503 /509 | `engine.log`, `server-probe/engine.log` |
| Pure custom contracts | PASS37,063 | `native-regressions/default.log` |
| Desktop / actual dedicated native replay format | PASS3,107 each | `production-replay.log`, `server-probe/production-replay.log` |
| Native archive / transport / architecture / ownership | PASS1,226 /PASS /PASS /42 | `native-regressions/` |
| Native grapple rules / wire | PASS17,102 /956 | `native-regressions/parkour-grapple-*.log` |
| Native lifecycle checkpoint / timed shot replay / certificates | PASS35 /4,676 /5,238 | `native-regressions/` |
| Native pickup replay / timed grant presentation | PASS105 /607 | `native-regressions/` |
| Dedicated startup failure and assembly inventory | PASS; no Avalonia, BEPU or CorpsePhysics loaded | `server-probe/startup.log` and all probe inventories |
| Existing map download / Community lobby selection / map readiness | PASS; all three exit0, including prepared-map Pump handoff | `native-regressions/custom-map-download.log`, `lobby-community-selection.log`, `custom-map.log` |
| Allocator CLI malformed approval options | PASS21; all exit2 with expected diagnostic, no bound port/store/rotation | `cli/audited-exit-codes.json` and case profile native logs |

Real allocator processes exercise no approval, old protocol82 UDP, missing
archive and deliberately corrupt archive refusal without allocating child
ports or disclosing a private store path. The successful request completes a
genuine challenge/reply/status barrier, launches an actual dedicated child and
joins with an ordinary owner client using a separate explicit client store.
Uninstall followed by prune skips the child's independently retained bytes;
after its last owner leaves, the child exits and prune succeeds while the
allocator and cached reply remain alive. A fresh native omission still hosts
and joins without package availability. Corruption lives in its own private
store and is never repaired to make a test pass.

Both final graphics routes independently acquire the actual world item, use
the creator definition/model/PCM and four optics, fire and reload6/23 to6/22,
then prove session and scene lease lifetimes. The root inspected final images:
optic4 displays6X and the reloaded magenta rifle displays6/22. These are local
NVIDIA/OpenGL4.6 observations, not WAN, physical audio, high-refresh or target
performance-budget qualification. The renamed unchanged build apphost staged
as `ProjectPrimeServer.exe` is a private installed candidate; release download
and installer qualification are not implied. Empty stderr above describes the
client harness; production children redirect native errors into profile logs.
CLI refusal still creates ordinary startup profile metadata/logs before the
ModEntry parser. No profile-write absence is claimed; no update activity was
observed in those cases.

Development failures remain preserved. Native protocol58 and transport17
fixtures initially used83 as an unknown future protocol after the deliberate
83 bump; they now reject84 while retaining literal historical82 fixtures.
An initial remote lifecycle fixture did not switch to its private profile
before reading paths.txt; it stopped before allocation. The dedicated probe's
first SDK invocation selected system dotnet without SDK401, and its first
engine run lacked the isolated profile. Corrected development runs and fresh
integrated qualification use explicit private dotnet and profiles. No failed
attempt is relabelled as a pass.

Native Intent/snapshot/CWM1/checkpoint14 shapes remain unchanged. This slice
versions only the live hosting request to83 and explicitly retains reviewed
historical replay attribution. Custom recording/seek and custom bots are still
refused pending their remaining CW-12 implementation. Community publication,
Studio and end-to-end UI remain CW-13 throughCW-15. Android builds, emulators
and device testing are deferred by user instruction. WAN, physical device,
performance and final release gates remain open. CI was not observed or waited
for; local acceptance is the evidence for immediate merge authorization.
