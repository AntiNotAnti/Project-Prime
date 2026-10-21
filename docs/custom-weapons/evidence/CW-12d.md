# CW-12d — local package hosting qualification

Final source `0e02b82e7` integrates main `65d30788c` and product commit
`a8f43d2d4`. See the [local hosting contract](../12d-local-package-hosting.md).
Final logs: `artifacts/custom-weapons/CW-12d/integrated/`.

Desktop Release built in96.19s, SHA-256
`4d4620d8eeb96236a23e83a6785538e10570b3d3e3483cc4fe8244e14c7c2a69`.
Actual dedicated Release built in68.09s, SHA-256
`71d19965d59c9856eabcd4fdee8f3218ae5afab4df81ba0863abf74e83ec6481`.
Copied tool, loaded dedicated and profile DLL hashes match their intended
assemblies. The unchanged apphost renamed `ProjectPrimeServer.exe` has SHA-256
`7adc3e93b42e61db426ded061283d57a00c633462f041d1d8a8d49816c0470cb`.
It is a private installed candidate from the build, not evidence of a released
package or downloader installation.

| Final integrated local check | Observed result | Log under integrated/ |
| --- | --- | --- |
| Actual local child/start/join lifecycle | PASS50; exit0;5.4238s; client stderr empty | `host/stdout.log`, `host/process.json`, `host/stderr.log` |
| Ordinary package GL client and dedicated child | PASS43; exit0;11.0378s; client stderr empty | `gl/stdout.log`, `gl/process.json`, `gl/stderr.log` |
| Hostlocal CLI | PASS8: six refusals without store/rotation mutation; native/package positive exit0 | `cli/receipts.json` |
| Desktop / actual dedicated package engine | PASS499 /495 | `engine.log`, `server-probe/engine.log` |
| Desktop / actual dedicated manifest binding | PASS678 each | `manifest.log`, `server-probe/manifest.log` |
| Desktop / actual dedicated preparation UDP | PASS146 each | `preparation.log`, `server-probe/preparation.log` |
| Pure custom contracts | PASS37,062 | `cpu.log` |
| Desktop / actual dedicated native replay format | PASS3,107 each | `production-replay.log`, `server-production-replay.log` |
| Native ownership / architecture / replay compatibility | PASS42 /PASS /1,189 | `native-ownership.log`, `native-architecture.log`, `native-protocol58.log` |
| Dedicated startup cleanup and inventory | PASS; no Avalonia, BEPU or CorpsePhysics loaded | `server-probe/startup.log` and probe inventories |

The selected local store/archive reaches both the dedicated child and ordinary
joining client, connection-bound without global environment mutation. Parent
launch and child/session/scene owners retain independent archive leases.
Preflight refusal precedes server rotation/port/process work; uninstall during
marker handoff fails closed. The GL fixture exercises ordinary world pickup,
four optics, firing and reload. The root agent inspected the final current
images: optic4 shows6X, the creator rifle is magenta and reload shows6/22.
This measured desktop path does not establish WAN, physical audio, high-refresh
or performance-budget acceptance.

Earlier `qualified/` observations belong to base `7998a089d`: desktop110.71s,
SHA-256 `da84e1f3744547a37fb1ed843def0218584a8b59513eb6c7890ee7ab3bbecee6`;
dedicated67.61s, SHA-256
`28f462bd800e3dc4fcbdf0961e61e18b610db97cfd33cb234631be692c6ac2f3`.
They passed lifecycle50, GL43, CLI8 and engine505 on both assemblies; they are
not relabelled as final integrated results.

The initial `final/` builds112.53s/70.84s were superseded before functional
qualification after review found selection missing from the joining-client
path. That connection-bound forwarding and an initial nullable fixture warning
were corrected before the qualified builds. The first qualified `host/` run
then failed reconnect because the authenticated owner sent Bye as the last peer:
the owner-token lobby correctly shut down and the later connection got no
answer. Failure logs remain intact. The corrected fixture retains a real UDP
witness without Ready, load ACKs or grants, then tests ownership transfer after
actual client preparation and uses a new witness for remaining lease proof.
`host-repeat/` passed50 on the same product binary. This fixture correction did
not change the product's shutdown behavior.

No remote allocator wire or launcher UI delivery is claimed. Full CW-12 remains
open for remote allocator selection, bots and custom replay/seek; CW-13 through
CW-16 remain open. Native replay still refuses package/custom gameplay. Android
tests are deferred by user scope, CI is unobserved, and no full
create/publish/download/play/replay or performance-budget acceptance is claimed.
