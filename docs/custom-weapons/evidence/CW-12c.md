# CW-12c — exact package combat and ordinary client execution

Final source `48e60cb86` integrates main `73f5ebf62` and #947–950. Protocol82
and its grapple/native layouts are inherited contracts; package execution adds
no further native width changes. Native IDs, CWM1 and historical profiles remain
unchanged. See the [execution contract](../12c-package-execution.md). Full CW-12
remains open.

Final logs: `artifacts/custom-weapons/CW-12c/release-82/`. Windows x64,
SDK10.0.401/runtime10.0.12. Desktop Release built in99.26s, SHA-256
`903c11b21842281937118c643a42f57c00d3d3e1c7c5d574914a8025ac5c598d`.
Actual dedicated Release built in68.78s, SHA-256
`cd25e1ea8f424187be0434880561af187c66665d2d249d199afadbf90da3f23d`.
Source/copied/loaded tool DLL hashes were verified against their intended
assembly. Earlier artifact directories retain observations from earlier hashes.

| Final local check | Observed result | Raw log under release-82 |
| --- | --- | --- |
| Desktop engine execution, three runs | PASS507 /563 /567 | `engine-1.log` through `-3.log` |
| Actual dedicated engine execution, three runs | PASS507 /505 /499; probe exit0 each | `server-probe/engine-1.log` through `-3.log` |
| Ordinary GL client and separate production dedicated process | PASS41; exit0;10.0819s; client stderr empty | `gl.stdout.log`, `gl-process.json`, `gl.stderr.log` |
| Desktop / dedicated CWPK binding | PASS678 each | `manifest.log`, `server-probe/manifest.log` |
| Desktop preparation UDP | PASS146 | `preparation.log` |
| Dedicated preparation UDP | First run FAIL timeout; three same-binary reruns PASS146 each | `server-probe/preparation.log`, `preparation-repeat.log`, `preparation-repeat-2.log`, `preparation-repeat-3.log` |
| Store / package / pure custom contracts | PASS99 /1,000 /37,062 | `store.log`, `package.log`, `cpu.log` |
| Dedicated startup rejection cleanup | PASS lease/prune/socket release; no Avalonia, BEPU or CorpsePhysics loaded | `server-probe/startup.log` and probe inventories |
| Desktop / dedicated production native replay format | PASS3,107 each | `production-replay.log`, `server-production-replay.log` |
| Native architecture / reviewed replay compatibility | PASS /1,189 | `native-architecture.log`, `native-protocol58.log` |
| Quake3 compatibility / grapple invariants / grapple wire | PASS689 /17,102 /956 | `native-compatibility.log`, `grapple-rules.log`, `grapple-wire.log` |

The first final dedicated preparation run timed out at “duplicate answered”
inside the shared rig's `ReadyAll` path, probe result1. The unchanged binary
then passed146 checks on three repetitions. No cause is established; later
passes do not erase the timeout or prove it cannot recur.

The creator package has UUID `12c012c0-1122-3344-5566-778899aabbcc`, archive
`697e3ba9567f7d90c3e0c92ea1a5f3a2d870e8e43d33861552c6cc94a27374d4`,
six-round magazine, initial reserve23, damage37 body/83 head, four optics and a
magenta model. Every final engine repetition records genuine `Headshot=true`
at `(2.291504, 1.5084245, -15.357178)`, HP163→80, unchanged native damage
history and zero engine step failures. The body/head checks use real
collision/health/payment paths.

The ordinary NetLaunch/NetSession GL client joins a separate production
server through paired `-weaponstore`/`-weaponarchive` options. Its worker
prepares the exact model and four PCM cues. Uninstall in the lobby preserves
retained ownership across Start. No synthetic snapshots, ownership grant or
client combat authority are used. The fixture declares its approach placement;
actual W movement over the extracted native floor reaches the world pickup.
Authority funding yields6/23. Ordinary selection, scope and optic cycling reach
optic4; fire spends one round and reload leaves6/22. Session Stop preserves
independent scene ownership; scene teardown releases the final lease.

Final captures use NVIDIA RTX5080/OpenGL4.6.0 NVIDIA617.42. The root agent
visually inspected optic4 showing6X and the magenta rifle with reloaded6/22.
Images are `release-82/gl/online-before-pickup-*`, `online-optic4-*` and
`online-package-reloaded-*`. This single-human GL fixture does not claim a
second rendered opponent hit; engine fixtures measure the damage separately.

Earlier regression observations remain separate: integrated-82 public
availability passed930 configuration/client,8,193 action and4,216 control
checks; that full suite was not rerun on the final binary. Final-82 CLI
negatives passed store-only, archive-only, uppercase digest and duplicate
option, each exit2 with no store/rotation creation
(`final-82/cli-negative/receipts.json`). They were not rerun for release-82;
flag source did not change. The initial wrapper missed diagnostics because
Windows sends stderr to its native PID log; the corrected wrapper reads that
log. An unsupported `--parkour-grapple` attempt is not counted as a pass.

Development failures remain recorded. The initial compile had seven fixture
constructor errors; wiring was corrected. Early engine runs lacked game data;
a deliberately held client-store gate kept the configured server waiting.
The genuine client exposed a Starting countdown frame0 baseline gap and then
inactive `KillCam.Reset` swallowing the pending Shoot edge. Those fixes
precede the final ordinary accepted-fire evidence.

Two early headshot fixtures failed, followed by diagnostic passes559/501/499
without damage changes. A later final-82 dedicated failure conclusively
recorded `Headshot=false`, HP163→126, two contacts/payments, unchanged native
history and zero step failures: correct37 body damage, not a wrong83 head
rule. Only the package fixture lane changed3→8 units and head-aim inset0.15→0.05
to remove the ambiguous front-capsule ray; baseline fixture defaults and damage
rules remained unchanged. Qualified-82 passed561/564/497; the six final runs
above confirm explicit head contacts on final source. Replay-format validation
initially failed a stale expected FireEvent width after main's grapple change.
Correcting that assertion without changing the adapter yielded final-82 desktop
3,102 and the final desktop/dedicated3,107 passes above.

Native recording, killcams and archived reconstruction still refuse custom
packages. Custom replay/bootstrap/seek, bots, host allocator package selection
and the wider Studio/Community flow remain open. Android testing is deferred
by user scope; CI is unobserved. No full create/publish/download/play/replay,
WAN, physical audio, high-refresh or performance-budget acceptance is claimed.
