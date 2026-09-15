# Free-running multi-client E2E (P0-C)

The semantic control transport is development-only and disabled unless an
operator explicitly enables it. Unix uses a normalized absolute Unix-domain
socket; Windows uses a current-user named pipe. A restricted per-run token
file authenticates the first line and is loaded once without logging the token.

Requests and responses are bounded strict JSON lines. Every request carries a
command ID plus `sessionId`, `matchId`, and `phaseId`. The closed command set
contains shell/lobby operations, gameplay input (`SubmitMovement` and
`SubmitFire`), frame/diagnostic capture, and rematch votes. Unknown fields,
duplicate properties, non-finite values, arbitrary paths, and generic setter or
reflection-shaped commands are rejected.

The server queues a bounded number of requests, gives each request a bounded
deadline, and cancels/expires queued or in-flight dispatches at that deadline.
Owner callbacks receive the cancellation token and must honor it. Shell
operations dispatch to the UI owner and gameplay operations to the shipping
input owner; mutating requests are phase-fenced before dispatch, with
`stale-phase` returned at the transport guard if queue delay made a rematch or
match transition make the request obsolete; the attached shell owner uses
`stale-identity` when its live identity no longer matches during UI dispatch.
A successful owner transition such as `StartMatch` may
advance the phase while it is being applied and remains accepted; the owner
callback and cancellation token are authoritative for that transition. The
read-only `GetShellState` query is the sole bootstrap exception to phase
equality, while its session, match, and phase values still require strict
identity syntax and transport authentication. Shutdown cancels pending work.
The transport itself does not mutate Scene/player health/lobby state directly.

The persistent desktop launcher now attaches one development-only semantic
control owner to its `HomeWindow` lifetime. Enable it explicitly with
`PRIME_E2E_ENABLE=1` (or `PRIME_E2E_CONTROL_ENABLE=1`); the coordinator supplies
per-client `PRIME_E2E_CONTROL_ENDPOINT` and
`PRIME_E2E_CONTROL_TOKEN_FILE` values, and also sets
`PRIME_E2E_CLIENT_SLOT` to `a` or `b`. The adapter dispatches shell commands
through `Dispatcher.UIThread` and the existing Gateway/Play owners. It is
disposed with the persistent launcher window and is not recreated for a
rematch.

`SubmitMovement` and `SubmitFire` now enter a 64-command bounded,
match-scoped owner that the shipping SDL host drains immediately before both
compatibility input and the fixed-step frame loop. Inputs expire after a
bounded lease and are cleared on session, match, phase, connection, life,
pause, replay, frame-advance, scene-ownership, detach, or shutdown changes.
The semantic focus exception bypasses only native-window focus eligibility;
all match and scene ownership gates remain in force. `SubmitFire` activates
the configured primary-fire binding only. Deterministic semantic look/aim is
not available, so firing alone is not accepted as proof of authoritative
damage.

`CaptureFrame` uses the same bounded SDL GPU readback path as production
captures. At most eight requests are pending, labels are filename-safe, PNG
encoding runs off the SDL host thread, and output is confined to
`<PRIME_E2E_RUN_DIR>/client-{a,b}/screenshots`. A response is accepted only
after the PNG is written. `GetShellState` and `GetDiagnostics` remain
read-only queries, and all mutating commands require the current
session/match/phase identity immediately before invoking an existing owner.
Diagnostics explicitly report deterministic aim and client-visible Worker
identity as unavailable rather than inventing evidence for those assertions.

## Coordinator

`tools/e2e/run-e2e.sh` (a thin wrapper around `run-e2e.py`) is a guarded coordinator. It starts the configured
Backend/Node/Worker stack and two configured client commands as direct child
processes, keeps both clients free-running, and records a strict evidence
bundle under an ignored artifact directory. It never pauses one client while
driving the other. The normal lifecycle milestones are:

```text
launcher-ready, lobby-created, lobby-joined, both-ready,
match-loading, match-start, damage-confirmed, death-confirmed,
respawn-confirmed, match-end, results, rematch, lobby-return
```

The coordinator requires explicit `PRIME_E2E_CLIENT_A_COMMAND`,
`PRIME_E2E_CLIENT_B_COMMAND`, and `PRIME_E2E_DRIVER_COMMAND` values. The server
command is optional: when omitted, the existing `tools/start-dev.sh
--with-backend` path starts Backend, Node, and the managed Worker; operators can
provide `PRIME_E2E_SERVER_COMMAND` for a packaged/shared stack. Commands are
parsed as bounded argument strings (use an explicit `bash -lc` command only if
the operator intentionally needs a shell wrapper). The coordinator refuses to
call a missing prerequisite a pass, monitors the server and both free-running
clients until the driver completes, and fails if any of those processes exits
early. It scopes and records its own child PIDs, and sends termination only to
those direct children on shutdown.

Generated Backend/Node credentials, private keys, and runtime state are held in
a run-owned mode-700 temporary directory outside the evidence bundle and are
removed during cleanup. The bundle contains only client/driver logs and a
sanitized `server-evidence.json` process summary; Backend/Node private state is
not copied. The managed Worker drains stdout/stderr to `Null`, so authoritative
Worker evidence is explicitly recorded as unavailable rather than represented
by a fabricated `worker.log`. The two per-run control token files are removed
in `finally`; other evidence artifacts are preserved.

The fixed semantic assertion manifest requires separate
`client-a-session-continuity` and `client-b-session-continuity` assertions plus
`distinct-client-identities`; a single `same-session` assertion would be
ambiguous for two clients.

`summary.json` and `assertions.json` distinguish `not-run`, `failed`, and
`passed`; no mock transport run is labeled live E2E. Screenshots/logs support
semantic assertions but cannot replace match/session/phase evidence from the
shipping owners.
