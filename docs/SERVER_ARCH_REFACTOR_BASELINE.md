# Server architecture refactor A0 baseline

Captured 2026-09-08 from the local checkout at:

```text
/Users/jarrett/Documents/Development/Project-Prime
```

The architecture scope and gate order are taken from
`/Users/jarrett/Downloads/PROJECT_PRIME_MULTI_INSTANCE_SERVER_ARCHITECTURE_MASTER_PLAN.md`,
section 44. This document records local source, test, build, and packaged-artifact
evidence. It does not turn source or static test evidence into live-client,
rendered-GUI, WAN, deployed-database, load/soak, or release proof.

The A0 evidence below is historical. The current worktree has since integrated
the A1–A5 source changes while preserving the original dirty paths and their
initial inventory. Current verification is reported separately from the A0
checkpoint and does not create a clean Git checkpoint. A fresh A9–A22
five-minute gate is accepted below; the earlier failed attempts remain
historical evidence rather than being rewritten.

## Checkpoint status

| Stage | Status at this capture | Evidence and boundary |
|---|---|---|
| A0 baseline | Evidence captured; clean checkpoint **not satisfied** | The A0 plan calls for a clean G1–G5 commit, separate Parallax/LICENSE preservation, a full test baseline, and protocol/build/content hashes. The worktree is already dirty and contains concurrent work. No cleanup, commit, or unrelated change absorption was authorized. |
| A1 `Server.Shared` | **Verified** | `src/Server.Shared/` and `tests/Server.Shared.Tests/` are present and the current focused Release suite passes 43/43. The contracts are referenced by the Node/Worker boundaries; this remains source/build evidence. |
| A2 player-state isolation | **Verified** | The post-baseline Release suite passes 976/976, including four ownership tests. The final content-path fallback rerun also passes 4/4 without `GAME_DATA_DIRECTORY`. |
| A3/A4 RNG, singleton, and content-runtime isolation | **Integrated; verification recorded in current suite** | The current content-backed Release suite passes 1049/1049. The content-free CI path runs the other 1000 tests and excludes only tests marked `RequiresGameContent=true`; neither result establishes live deployment, load, or release proof. |
| A5 multi-instance isolation gate | **Accepted** | The focused A5 checks pass 8/8 and the acceptance record is complete. This remains focused in-process evidence, not live deployment or load proof. |
| A6/A7 Worker extraction and adjacent checks | **Focused evidence passed** | The core set has 5 passing checks and the adjacent set has 11 passing checks. `MatchInstance` and the gameplay runtime compile from `src/Server.Worker/`; the Node-managed Worker is the supported host boundary. |
| A8 | **Focused evidence passed** | The A8 set has 7 passing checks. |
| A9–A22 | **Accepted (fresh five-minute gate)** | The fresh gate exits 0 and records 300 seconds of workload plus 41.288 seconds of drain; its exact lifecycle, crash/replacement, reconnect, reporting, artifact, and end-state metrics are recorded below. The original failed five-minute run and the subsequent failed-drain rerun remain in the evidence table. Same-lobby rematch/map-change and Node WSS transport are separate gaps and are not claimed by this gate. |
| A23 | **Accepted (bounded host-specific measurement)** | The 21-row controlled matrix is recorded in [`docs/architecture/capacity-model.md`](architecture/capacity-model.md). All 21 child records have `exitStatus: 0`; the accepted result is host-specific and bounded. `8/1/4` is the highest tested topology meeting the active-window scheduler-drop criterion, not a capacity ceiling. The parent matrix session was reaped before the final poll, so no captured parent exit is claimed. |
| A24 | **Accepted (bounded local/source/loopback/child-process evidence)** | The real UDP cross-match admission check, report-adversarial (9), and child-process IPC (7) checks pass. This accepts the captured local/source/loopback/child-process boundary only; it does not close unrecorded A24 evidence outside that boundary. |
| A25 | **User-confirmed administrative completion** | The user confirmed A25 complete for administrative tracking. No local eight-hour soak artifact was independently verified in this run, so this status does not establish soak, memory, capacity, or cross-host evidence. |
| A26 | **Accepted native `osx-arm64` fresh-extracted package smoke** | WSS authentication, public lobby create/configure/start, real Worker handoff, UDP admission, match end, replay/artifact writes, graceful drain and no-orphan checks passed; the empty-content negative exited 1. This is local package/process evidence only; no live Windows, Android, deployed, WAN, rendered-client, or other live-client proof is claimed. |

### A6–A26 execution tracker

This tracker records implementation progress only. A stage stays pending until
its required source, focused-test, infrastructure, and runtime evidence is
recorded.

| Stages | Current state |
|---|---|
| A6/A7 | Focused evidence passed: core 5 and adjacent 11; Worker-owned `MatchInstance` source migration and legacy wrapper builds are verified locally. |
| A8 | Focused evidence passed: 7 checks. |
| A9–A22 | Accepted for the fresh five-minute gate: the command exits 0 and the result is recorded below. The earlier report-identity failure and failed-drain rerun remain retained as historical evidence. Same-lobby rematch/map-change and Node WSS transport remain separate gaps. |
| A23 | Accepted bounded host-specific measurement: 21 independent rows, all child `exitStatus: 0`; see [`docs/architecture/capacity-model.md`](architecture/capacity-model.md). The matrix parent session was reaped before the final poll, and `8/1/4` is a measured bound rather than a capacity ceiling. |
| A24 | Accepted bounded local/source/loopback/child-process security evidence: real UDP cross-match, report-adversarial (9), and child-process IPC (7). Evidence outside that boundary remains unclaimed. |
| A25 | User-confirmed administrative completion; no local eight-hour soak artifact was independently verified in this run. The status does not freeze a capacity setting. |
| A26 | Accepted native `osx-arm64` fresh-extracted package smoke: WSS authentication, public lobby create/configure/start, real Worker handoff, UDP admission, match end, replay/artifact writes, graceful drain and no-orphan checks passed; the empty-content negative exited 1. Live Windows, Android, deployed, WAN and client proof remain unclaimed. |

### Dirty-worktree boundary

`/tmp/codex-re/project-prime-architecture-baseline/preexisting-files.json` is the
initial A0 inventory. It contains 43 dirty tracked paths: 21 entries with an
initial SHA-256 and 22 entries represented as `null` because the path was
already deleted. The inventory includes the pre-existing Parallax, LICENSE,
protocol, client, conversion-tool, and guard-tool changes. The baseline TRX
files and `tooling.log` were captured beside that inventory.

The current checkout also shows concurrent A1/A2 paths that were not part of
that initial manifest, including `Game.sln`, `src/Server.Shared/`,
`tests/Server.Shared.Tests/`, and gameplay/scene files. These changes are
preserved as found. The captured baseline artifacts are not regenerated or
rewritten, and no commit has been made.

The A0 clean checkpoint therefore remains unsatisfied. The initial 43-path
inventory and its recorded hashes remain the historical reference; the current
implementation work is kept alongside those paths without rewriting that
inventory. The existing local evidence does not authorize a claim of live client
proof.

## Repository and toolchain identity

| Item | Observed value |
|---|---|
| Git `HEAD` | `9ef8cb5d572837c7d1efcc821ea4e3ae1bf7c615` |
| Resolved .NET SDK | `10.0.400` (`dotnet --version`) |
| `global.json` request | SDK `10.0.100`, `rollForward: latestFeature` |
| Target framework | `net10.0` (`Directory.Build.props`) |
| Language version | C# `14.0` |
| Deterministic build | Enabled |
| Content binary used for corrected run | `AMHE1/_bin/arm9.bin`, 909,912 bytes, SHA-256 `1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b` |

Selected source/config hashes at capture (SHA-256; these are focused files,
not a hash of the whole repository or content tree):

| File | SHA-256 |
|---|---|
| `global.json` | `f202c0c9129e553bcf9bef4cbc101c4bc4573eae070cb3aa963b393aad42c336` |
| `Directory.Build.props` | `1a884f4714e01b74a251741d17388868e95ada8ab72a7ae35328ca7ea701d721` |
| `Game.sln` (worktree at capture) | `fd7d6607c08a077f8cc68d643c4a640040a02050194a94619e1842112cb2f624` |
| `Game.sln` (HEAD) | `b769277a7930c5c52ebf9e5d8624fc76a4a4ba35f2276e62bc0cc9c0f3399884` |
| `src/Game/Protocol/NetHeader.cs` | `5fe035f02765d356de4fd3c55f4d1609de8f3d9246ee2245fdc520051be13e8d` |
| `src/Game/Protocol/NetProtocol.cs` | `183e3d35f81616566989006c6d64fe0ea8d96fd7f29a5c5d2a00b335c35f4e87` |
| `docs/PROTOCOL_8_DESIGN.md` | `f24aa2c1f5e38babcf63dbdeb9f00aaac37f88a4680916803461cb575bbcb216` |

The source declares `NetHeader.Version = 8` and derives
`NetConfig.ProtocolVersion` from it. `docs/PROTOCOL_8_DESIGN.md` describes
protocol 8 as unreleased/evolving, so this is protocol/source evidence only.

## Baseline tests and focused checks

All results below are from the saved artifacts in
`/tmp/codex-re/project-prime-architecture-baseline/`.

| Scope | Result | Evidence |
|---|---:|---|
| Main game/test assembly, corrected content path | **972 passed, 0 failed** | `architecture-baseline-content.trx`; 972/972 executed. SHA-256 `96d44d7f51daa7c8755258b3872ae76f208a5268e80866c37944436b43a8e957`. The existing binaries were used with `--no-build --no-restore` and `GAME_DATA_DIRECTORY` set to the checkout's `AMHE1`. |
| Main game/test assembly, initial default-path run | 963 passed, 9 failed | `architecture-baseline.trx`; all nine failures were the same content lookup error for `tests/Tests/bin/Release/net10.0/AMHE1/_bin/arm9.bin`. SHA-256 `14f1f96ecdc54e442fb7ffd7ff5f5c808cb47e716de0d3c33153109b0ec152e9`. The extracted content exists at `AMHE1/_bin/arm9.bin`; the corrected run resolves this path/setup issue. |
| Backend | **194 passed, 4 skipped, 0 failed** | `backend-baseline.trx`; 198 total, 194 executed. The four skipped PostgreSQL tests each report `Set PRIME_TEST_POSTGRES_FILE to an isolated PostgreSQL connection-string file.` SHA-256 `8a76a90e86a4c09009484e6fb9486c2d49d28dbd26904adcc5721c5fd450c2da`. |
| Imaging | **18 passed, 0 failed** | `imaging-baseline.trx`; 18/18 executed. SHA-256 `05d71a158181a78fcbd293be9e1f03424ad828b2928c64683528c76162f4da71`. |
| Python tooling tests | **58 passed, 0 failed** | `tooling.log` records `Ran 58 tests` and `OK`. SHA-256 `d3f6949ac93a9c3c0525cab595c7cc14515fc6fcc3481cbaaf22c90ffb544210`. |
| Multiplayer audit self-test | **PASS** | `audit-self-test.log`: empty retail/FH, truncation, versions, offsets, unknown types, and counts. SHA-256 `95a83812302a2af94d05b9fe8a1e0925b9958922da2a93b4d6c57b3ce740a658`. |
| Reliable backpressure/freshness self-test | **PASS** | `backpressure-self-test.log`: mixed freshness health gate and bounded refusal of the saturated peer while healthy batches remain admitted in order. SHA-256 `839d63d472863e28f78b398074811668c771a6e2e1062f031078dd298cdc2f30`. |
| Synthetic authoritative server smoke | **PASS** | `server-smoke.log`: exact discovery/status checks, protocol 8 advertisement, two healthy localhost clients for 10 seconds, 30 snapshots/s, 595 processed inputs per client, no queue drops or rejects. It explicitly says no rendered gameplay is running. SHA-256 `1b92e52172f25b818ff6f4d4fbf775cf7c9115f9404fa8be233f8ee21657bea3`. |
| Post-baseline A2 Release suite | **976 passed, 0 failed** | `/tmp/a2-final-full.log`; four new player/roster ownership tests are included. This does not replace the pre-change A0 baseline of 972/972. |
| Prior A3/A4/A5 Release suite | **1009 passed, 0 failed** | `/tmp/codex-re/project-prime-a5-full.log`; retained as the earlier integrated source checkpoint. |
| Focused A5 isolation checks | **8 passed, 0 failed; accepted** | Current focused A5 check set; retained as a separate gate signal from the 1009-test suite. |
| Current main Release suite with extracted content | **1049 passed, 0 failed** | `/tmp/codex-re/project-prime-pre-soak-main.log`; `GAME_DATA_DIRECTORY` points to extracted `AMHE1`. |
| Content-free main CI suite | **1000 passed, 0 failed** | `dotnet test tests/Tests/Tests.csproj -c Release --filter "RequiresGameContent!=true"`; only explicitly marked content-required tests are excluded. |
| Current `Server.Shared` focused suite | **43 passed, 0 failed** | `tests/Server.Shared.Tests/Server.Shared.Tests.csproj`; current contract evidence. |
| Current Node suite with extracted content | **75 passed, 0 failed** | `/tmp/codex-re/project-prime-pre-soak-node.log`; includes real Worker process and vertical lifecycle tests. |
| Content-free Node CI suite | **70 passed, 0 failed** | `dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter "RequiresGameContent!=true"`; real Worker/content tests are explicitly marked. |
| Worker density checks | **Passed at 2/4/8/16 matches** | Focused Worker density checks; this is not a production capacity result. |
| Worker two-process/four-match run | **21 passed, 0 skipped** | `/tmp/real-worker-tests.log`; includes deterministic RNG evidence. |
| Short real-domain/backend recovery run | **16 rounds, 0 failures** | `/tmp/codex-re/worker-soak-domain-first/summary.json`; Kestrel TLS plus SQLite and report drain/recovery evidence, not an eight-hour soak. |
| Five-minute crash/artifact integration run | **Not green; recovery and drain completed with one report-identity failure** | `/tmp/codex-re/worker-soak-five-minute/summary.json`; 300-second workload at density 4 across 2 Workers and 2 lanes, with crashes at 120/240 seconds and HTTP 503 injection. 64 matches created, 55 completed, 8 interrupted, 1 `MatchFailed` (`Completion persistence failed: Report does not match frozen launch identity`), 55 reports persisted, 110 artifacts validated, payload-hash mismatches 0, and no pending/quarantined outbox entries at shutdown. |
| Five-minute subsequent rerun | **Not green; final drain aborted on a soak-client connection failure** | `/tmp/codex-re/worker-soak-five-minute-fixed/run.log` and `metrics-00.jsonl`; reached the full 300-second workload at density 4 across 2 Workers and 2 lanes, replaced both intentionally crashed Workers, and recorded 0 `MatchFailed` events through workload completion. Final drain then stopped with `System.IO.IOException: A soak client reported admission or connection failure` before `summary.json` was written; 52 receipts existed at abort. |
| Short drain diagnostic | **Pass; separate from five-minute acceptance** | `/tmp/codex-re/worker-soak-drain-check/summary.json`; 10-second workload plus 36.137-second drain, 8 completed/persisted matches, 16 validated artifacts, zero failures/interruption/reconnects/payload-hash mismatches, empty outbox, and zero active matches. |
| Fresh A9–A22 five-minute gate | **Accepted; command exit 0** | `/tmp/codex-re/worker-soak-a9-a22-20260908/summary.json` and `metrics-00.jsonl`; exact result is recorded below. |
| Current backend suite | **202 total: 198 passed, 4 skipped, 0 failed** | Four PostgreSQL cases explicitly skip when `PRIME_TEST_POSTGRES_FILE` is absent. |
| Worker apphost publish | **Succeeded** | `dotnet publish src/Server.Worker/Server.Worker.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true`; produced the Node-managed Worker apphost in `/tmp/codex-re/project-prime-worker-publish-check-20260908`. The apphost is not a user-facing standalone hosting mode. |

### Fresh A9–A22 five-minute gate (accepted)

The run in `/tmp/codex-re/worker-soak-a9-a22-20260908/` exited 0 and reported
`kind: complete`. The workload ran for 300 seconds and drained for 41.288
seconds, for 341.288 seconds total. Its accepted result is:

| Check | Result |
|---|---|
| Match lifecycle | 64 created = 56 completed + 8 expected interrupted from 2 injected Worker crashes/replacements |
| Failures | 0 match failures; 0 actor failures |
| Reconnects | 144 |
| Reporting | 56 reports ingested and 56 persisted |
| Backend outage recovery | 8 HTTP 503 responses |
| Report integrity | 0 payload-hash mismatches |
| Semantic artifacts | 112 |
| Final occupancy | Logical occupancy 1; 0 active matches at end |
| Final outbox state | 0 pending, 0 queued, 0 reserved, and 0 quarantined |

The summary source is `summary.json`; the 112 semantic artifact records are
counted in `metrics-00.jsonl`. This gate covers the exercised Worker/Node
crash-replacement, reconnect, report persistence, artifact, and drain paths.
The run's lobby-return records open rematch state, but do not close
same-lobby rematch or map-change behavior. Node WSS transport is also outside
this gate; both are separate follow-up gaps. The earlier failed five-minute
run and failed-drain rerun above remain part of the record and are not erased
by this accepted run. A25 is administratively complete by user confirmation,
but no local eight-hour soak artifact was independently verified in this run.
A26 is accepted on native `osx-arm64` fresh-extracted package smoke: WSS
authentication, public lobby create/configure/start, real Worker handoff, UDP
admission, match end, replay/artifact writes, graceful drain and no-orphan
checks passed; the empty-content negative exited 1. The bounded A23
measurement is recorded below. This remains local package/process evidence and
does not claim live Windows, Android, deployed, WAN, rendered-client, or other
live-client proof.

### A23 bounded host-specific capacity measurement (accepted)

The 2026-09-08 matrix in
[`docs/architecture/capacity-model.md`](architecture/capacity-model.md) has 21
unique rows: seven fixed `matches-per-Worker / Workers / lanes` topologies,
each exercised with three roster variants. All 21 child records in
`/tmp/codex-re/worker-capacity-20260908/matrix.jsonl` have `exitStatus: 0`.
The parent matrix session was reaped before the final poll, so the evidence
does not claim a captured parent exit. Across the matrix, matches/reports/
replays/telemetry are each 622/622, with 0 failures, 0 interrupted matches,
and 0 payload-hash mismatches. The highest tested topology meeting the
active-window scheduler-drop criterion is `8/1/4`; this is a host-specific
measured bound, not a capacity ceiling. The A25 candidate is four matches per
Worker, two lanes, two matches per lane; the runtime fallback remains one
because no local eight-hour soak artifact was independently verified in this
run, and cross-host validation remains outstanding.

Two maximum-player supplemental direct runs are recorded in the capacity model:
`8/0/0` at `4/1/2` with one Worker (`16/16` matches/reports) and at `4/2/2`
with two Workers (`32/32` matches/reports). Both exited 0, drained their
outboxes, had zero queue drops, and had no failure, interruption, or payload
hash mismatch. They extend bounded A23 evidence without changing its measured
bound or freezing the A25 configuration.

The main corrected run validates the existing test binary with the extracted
content directory. It does not validate a Windows client, rendered output,
WAN behavior, deployment, or a live PostgreSQL service. The backend's four
PostgreSQL cases remain unexecuted until an isolated
`PRIME_TEST_POSTGRES_FILE` is supplied.

As a post-baseline A1 check, `dotnet test
tests/Server.Shared.Tests/Server.Shared.Tests.csproj -c Release` passed 43/43
focused tests locally. That run is current-worktree evidence and is separate
from the saved A0 TRX set above.

The CI-safe content-free commands are:

```text
dotnet test tests/Tests/Tests.csproj -c Release --filter "RequiresGameContent!=true"
dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter "RequiresGameContent!=true"
```

The content-backed checks must be run explicitly with extracted content; a
missing path is an error rather than a skip:

```text
GAME_DATA_DIRECTORY=/absolute/path/to/AMHE1 \
  dotnet test tests/Tests/Tests.csproj -c Release --filter "RequiresGameContent=true"
GAME_DATA_DIRECTORY=/absolute/path/to/AMHE1 \
  dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter "RequiresGameContent=true"
```

The package workflow publishes `Server.Node` as the root `ProjectPrimeServer`
apphost and `Server.Worker` below `worker/` in one bundle. The native
`osx-arm64` fresh-extracted package smoke passed WSS authentication, public
lobby create/configure/start, real Worker handoff, UDP admission, match end,
replay/artifact writes, graceful drain and no-orphan checks; the empty-content
negative exited 1. This is local package/process evidence only. There is no
Worker `--standalone` mode and no legacy standalone Server rollback/deploy
package.

## Build evidence

`nettest-build.log` reports a successful Release build with zero errors. It
contains four `NU1900` warnings because NuGet vulnerability metadata was not
reachable; those warnings are retained as evidence and are not silently
reclassified. SHA-256 of the log is
`5e770e256cbeffe93c29e95939cb0f1b0de64e498d71cde82f09b575de35604f`.

Selected staged outputs from `/tmp/codex-re/project-prime-architecture-baseline/nettest/`:

| Output | SHA-256 |
|---|---|
| `ProjectPrime.Game.dll` | `557e2225b2476c9f490a7be4e355083d178f563a55ec31616c56a851316bc6f8` |
| `ProjectPrimeServer.dll` | `86b58d2687b583b23369c59331a707490e940355413e8ec9993bbb981f84709f` |
| `nettest.dll` | `e9a401a190a1d47762f384e2cd3eaed8f6d6c1b00662792c607feb13c8bd4282` |

These are local build artifacts used by the saved checks; their hashes do not
establish release provenance or deployed-server identity.

## Protocol 8 and local Parallax artifact boundary

The focused protocol evidence is:

```text
src/Game/Protocol/NetHeader.cs: NetHeader.Version = 8
src/Game/Protocol/NetProtocol.cs: NetConfig.ProtocolVersion = NetHeader.Version
docs/PROTOCOL_8_DESIGN.md: live protocol-8 codec/design evidence, marked unreleased/evolving
```

The local shipped-style Parallax payload is recorded as an archive artifact,
separate from the dirty source tree:

| Artifact | SHA-256 | Archive-only content evidence |
|---|---|---|
| `maps/parallax/parallax.pk3` | `8903f07d80e8ed9e192fc631efb501675dc6c805c49c78dd0320b94d17225f27` | Zip archive with 13 entries, 308,840 uncompressed payload bytes: `maps/parallax.bsp`, two shader-list files, and ten `textures/parallax/*.tga` materials. |
| `maps/parallax.zip` | `4115e86f35c3fdc0b4969b5ee1537b5960a2ee87a736e2c52a60b86ac68c7b48` | Source/package archive with 70 entries, including macOS metadata; it is not treated as the shipped payload identity. |

The archive listing and focused hashes prove only the bytes and entries of the
local artifacts. They do not prove map loading in a live client, visual
correctness, collision behavior, or network deployment. No full-tree content
hash was taken.

## A0 exit conditions and next gate

A clean Git checkpoint was not created because the initial dirty paths remain
preserved in the manifest. The scoped implementation can proceed while keeping
those paths separate; this evidence does not authorize rewriting unrelated
files or claiming the clean checkpoint exists.

A1 contracts pass 43 current Release tests. The A2 player/roster migration
passes the 976-test Release suite and the final four-test content-path fallback
check. The current content-backed A3/A4/A5 suite passes 1049 tests and the
focused 8-test A5 check set is accepted. A6/A7 core and adjacent checks (5 and
11) and the A8 set (7) pass. A9–A22 have focused evidence for the covered
Worker/Node paths, and the fresh five-minute gate above exits 0 with 64
created, 56 completed, 8 expected crash/replacement interruptions, 0 match or
actor failures, 144 reconnects, 56 reports ingested/persisted, 8 HTTP 503
responses, 0 payload-hash mismatches, 112 semantic artifacts, logical
occupancy 1, and zero active/pending/reserved/quarantined state at the end.
The original report-identity failure and subsequent failed-drain rerun remain
historical evidence. Same-lobby rematch/map-change and Node WSS transport are
separate gaps. A23 is accepted only as the bounded host-specific measurement
recorded above. A25 is user-confirmed administratively complete without an
independently verified local eight-hour soak artifact. A26 is accepted on the
native `osx-arm64` fresh-extracted package smoke: WSS authentication, public
lobby create/configure/start, real Worker handoff, UDP admission, match end,
replay/artifact writes, graceful drain and no-orphan checks passed; the
empty-content negative exited 1. This remains local package/process evidence;
live Windows, Android, deployed, WAN, rendered-client and other live-client
proof remain unclaimed.
