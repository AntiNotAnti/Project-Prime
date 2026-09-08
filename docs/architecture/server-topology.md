# Current server topology

This is the source-based topology snapshot for the current worktree. It records
the accepted A1–A8 evidence, the accepted fresh A9–A22 five-minute gate, and the
focused Worker evidence available so far, including the bounded A23 host
measurement and bounded A24 local/source/loopback/child-process evidence; it
does not turn these results into live-client, deployment, a portable capacity
ceiling, or release proof. The checkout contains
concurrent uncommitted changes, so the paths below describe the observed
ownership boundaries rather than a release contract.

## Stage status

| Stage | Current state |
|---|---|
| A0 | Historical baseline captured in `docs/SERVER_ARCH_REFACTOR_BASELINE.md`; the clean-checkpoint requirement remains unsatisfied because the original dirty paths were preserved. |
| A1 | Verified: `src/Server.Shared/` and its current focused suite pass 43/43. Node and Worker use the shared contract project at their source boundary. |
| A2 | Verified: the post-baseline Release suite passes 976/976, including player/roster ownership checks and the content-path fallback rerun. |
| A3/A4 | Integrated and verified in the current content-backed 1049-test Release suite; the content-free CI path passes the other 1000 tests and excludes only tests marked `RequiresGameContent=true`. |
| A5 | Accepted: the focused multi-instance isolation set passes 8/8 and the acceptance record is complete. |
| A6/A7 | Focused evidence passed: the core set has 5 checks and the adjacent set has 11 checks. Worker runtime extraction and Node/Worker package builds are active source boundaries. |
| A8 | Focused evidence passed: 7 checks. |
| A9–A22 | **Accepted (fresh five-minute gate)** for the covered Worker/Node paths. The command exits 0; the accepted run records 300 seconds of workload plus 41.288 seconds of drain, 64 created, 56 completed, 8 expected interruptions from 2 injected crash/replacement events, 0 match/actor failures, 144 reconnects, 56 reports ingested/persisted, 8 HTTP 503 responses, 0 hash mismatches, 112 semantic artifacts, logical occupancy 1, and zero active/outbox/reserved/quarantined state at the end. The earlier failed runs remain recorded below and in `docs/SERVER_ARCH_REFACTOR_BASELINE.md`. Same-lobby rematch/map-change and Node WSS transport are separate gaps. |
| A23 | **Accepted bounded host-specific measurement**: 21 independent rows, all child `exitStatus: 0`; `8/1/4` is the highest tested topology meeting the active-window scheduler-drop criterion, not a capacity ceiling. See [`capacity-model.md`](capacity-model.md). |
| A24 | **Accepted bounded local/source/loopback/child-process evidence**: real UDP cross-match admission, report-adversarial (9), and child-process IPC (7). Evidence outside that boundary remains unclaimed. |
| A25 | **User-confirmed administrative completion**. No local eight-hour soak artifact was independently verified in this run; the confirmation does not establish soak, memory, capacity, or cross-host evidence. |
| A26 | **Accepted native `osx-arm64` fresh-extracted package smoke**. WSS authentication, public lobby create/configure/start, real Worker handoff, UDP admission, match end, replay/artifact writes, graceful drain and no-orphan checks passed; the empty-content negative exited 1. This remains local package/process evidence and does not claim live Windows, Android, deployed, WAN, or rendered-client proof. |

The historical A0 suite remains 972/972 and its backend baseline remains 194
passed with four PostgreSQL tests skipped because
`PRIME_TEST_POSTGRES_FILE` was absent. Current worktree verification is tracked
separately: the backend suite is 198 passed with those four explicit skips,
and `Server.Shared` is 43/43. The real Worker process run is recorded in
`/tmp/real-worker-tests.log` (21 passed, 0 skipped).

## Accepted A23 bounded host measurement

The 2026-09-08 capacity matrix is documented in
[`capacity-model.md`](capacity-model.md). It has 21 unique rows across seven
fixed topologies and three roster variants. All 21 child records have
`exitStatus: 0`; the parent matrix session was reaped before the final poll, so
the record does not claim a captured parent exit. The aggregate reconciliation
is 622/622 matches/reports/replays/telemetry, with 0 failures, 0 interrupted
matches, and 0 payload-hash mismatches. `8/1/4` is the highest tested topology
meeting the active-window scheduler-drop criterion, not a capacity ceiling.
The A25 candidate is four matches per Worker, two lanes, and two matches per
lane; the runtime fallback remains one because no local eight-hour soak artifact
was independently verified in this run. Administrative completion does not
freeze a capacity setting or provide cross-host validation.

Two maximum-player supplemental direct runs extend the bounded result to
`8/0/0`: one Worker at `4/1/2` completed/persisted `16/16` matches/reports,
and two Workers at `4/2/2` completed/persisted `32/32`. Both exited 0, drained
their outboxes, and recorded zero queue drops with no failure, interruption, or
payload-hash mismatch. Their lane and health details are in
[`capacity-model.md`](capacity-model.md); they do not change the bounded A23
criterion or freeze the A25 configuration.

## Accepted A24 bounded security evidence

The accepted A24 boundary is local/source/loopback/child-process evidence: the
real UDP cross-match admission check passes, the report-adversarial set has 9
passing checks, and the child-process IPC set has 7 passing checks. Evidence
outside that boundary remains unclaimed. A25 administrative completion and A26
source/package acceptance do not expand this evidence boundary.

The local A25 soak artifact was not independently verified in this run. The
existing retention note remains a limitation: `CriticalEventLog` is capped at
8,192 records and 4 MiB, which cannot hold the proposed eight-hour reconnect
evidence without scenario-derived bounded capacity or complete archival chunks.
The A25 administrative status therefore does not freeze the runtime fallback.

## Accepted A9–A22 five-minute gate

The fresh run at `/tmp/codex-re/worker-soak-a9-a22-20260908/` exited 0 with
`kind: complete`. `summary.json` records a 300-second workload and 41.288
seconds of drain. The final gate values are 64 created = 56 completed + 8
expected interrupted from 2 injected Worker crashes/replacements; 0 match and
actor failures; 144 reconnects; 56 reports ingested and persisted; 8 HTTP 503
responses; 0 payload-hash mismatches; and logical occupancy 1. The JSONL
evidence records 112 semantic artifacts. At shutdown, active matches, outbox
pending/queued/reserved reports, and quarantined reports are all 0.

This acceptance covers the exercised Worker/Node crash replacement, reconnect,
report persistence, artifact, and drain paths. The lobby-return records open
rematch state but do not close same-lobby rematch or map-change behavior. Node
WSS transport is outside this gate. The previous report-identity failure and
failed-drain rerun remain historical evidence; they are retained in the
baseline document rather than being treated as if they passed.

## Runtime that exists today

The supported server boundary is a persistent Node with a managed Worker pool:

```text
Backend directory and account service
└── persistent Server Node (WSS control authority)
    ├── sessions, public lobby list, lobby state and match placement
    └── authenticated Worker pool
        ├── Worker MatchInstance A (authoritative 60 Hz simulation + UDP)
        ├── Worker MatchInstance B
        └── Worker MatchInstance N
```

The client authenticates with the Backend, selects a compatible Node, and opens
the Node's WSS control connection. Host creates a public lobby; Join lists and
joins public lobbies. The Node freezes the match specification, places it on a
Worker, and sends a signed UDP handoff. Each Worker owns its `MatchInstance`,
simulation lane, scene, entities, transport routing, replay, telemetry and
report lifecycle. Control traffic remains on the Node connection and gameplay
traffic goes directly to the selected Worker.

`src/Server.Node/` owns the persistent control, session, lobby, directory and
Worker-lifecycle boundary. `src/Server.Worker/` owns the gameplay runtime and
shared UDP transport. The Worker process accepts Node IPC identity/configuration
and bounded runtime options; it has no user-facing `--standalone` mode. Private
or unlisted local hosting is retired from the client cutover.

The current Node lifecycle suite passes 75/75 with extracted content and 70/70
for the content-free CI subset. The accepted five-minute gate above adds 56
persisted reports, 112 semantic artifacts, 144 reconnects, and clean final
active/outbox state after two crash replacements and eight HTTP 503 responses.
The original bounded five-minute integration run and subsequent failed-drain
rerun remain historical evidence; same-lobby rematch/map-change remains a
separate gap.

The package workflow publishes the Node apphost at the bundle root as
`FruityPrimeServer` and the Worker apphost below `worker/`. The native
`osx-arm64` fresh-extracted package smoke passed WSS authentication, public
lobby create/configure/start, real Worker handoff, UDP admission, match end,
replay/artifact writes, graceful drain and no-orphan checks; the empty-content
negative exited 1. No legacy standalone Server rollback or deploy path and no
Worker standalone apphost contract is retained.

## Current ownership boundaries

| Area | Current owner | Evidence or compatibility note |
|---|---|---|
| Match simulation, scene, entities, fixed ticks | `src/Server.Worker/` | Worker `MatchInstance` is the runtime boundary; one Worker owns each match and its fixed-step loop. |
| Per-match players and derived state | Worker/Game match-owned objects | `MatchPlayers`, feature state, random state, model/runtime wrappers, and scene-owned state are covered by the accepted A2–A5 focused evidence. |
| Content and model runtime | Worker-owned `WorkerContent` plus Game content readers | The one-version Worker content context is acquired before matches; lifecycle and reload behavior remains part of the later Worker/Node evidence. |
| UDP transport and match routing | Worker/shared transport | `src/Server.Worker/` owns the Worker datagram hub and per-match routing; the Node never carries gameplay packets. |
| Node control, reporting, directory, and lobby work | `src/Server.Node/` | Node lifecycle/control sources are present and covered by the current 75/75 content-backed and 70/70 content-free suites. The accepted five-minute gate persists 56 reports and 112 semantic artifacts after crash replacement, reconnect, HTTP 503 recovery, and drain. The earlier report-identity failure and failed-drain rerun remain historical evidence; same-lobby rematch/map-change remains a separate gap. |
| Client hosting/join cutover | Client + Backend + Node | The launcher selects compatible Nodes and creates or joins public lobbies. Private/unlisted local hosting and direct Worker launch are retired. |

The accepted isolation evidence establishes the current match-owned state
boundary for the covered paths. The focused security checks and short
real-domain/backend recovery run add evidence for their specific paths; they do
not claim that the broader Node, performance, security, or long-soak
requirements are complete.

## Evidence limits and next gates

The current source and focused runs support the Worker extraction, the accepted
in-process isolation gates, the latest Node vertical lifecycle path, the
accepted fresh five-minute crash/replacement gate, the bounded A23 host
measurement, and the bounded A24 local/source/loopback/child-process checks
listed above. A25 is administratively complete by user confirmation, but no
local eight-hour soak artifact was independently verified in this run. A26 is
accepted on the native `osx-arm64` fresh-extracted package smoke: WSS
authentication, public lobby create/configure/start, real Worker handoff, UDP
admission, match end, replay/artifact writes, graceful drain and no-orphan
checks passed; the empty-content negative exited 1. None of this establishes a
portable capacity ceiling, live Windows or Android behavior, deployed service,
WAN path, rendered client, or other live-client proof. The original
report-identity failure and subsequent
failed-drain rerun remain historical evidence. Same-lobby rematch/map-change
remains a separate behavior gap.
