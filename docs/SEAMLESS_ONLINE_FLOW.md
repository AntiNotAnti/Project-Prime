# Seamless online lifecycle

Status: the client, Node round coordinator, Worker match path, Results ballot,
and reusable SDL host are implemented on this branch. This document records
their ownership and operating contract. It does not claim deployment or live
GUI/GPU acceptance.

The canonical loop is:

```text
Gateway / account or explicit guest identity
        -> persistent client shell and Node control session
        -> Node-owned lobby
        -> Node handoff to a compatible Worker match
        -> direct Worker gameplay UDP
        -> Results overlay and Node-owned ballot
        -> automatic continuation or lobby
```

## Ownership and lifetime

| Boundary | Owner | Lifetime and responsibility |
|---|---|---|
| Application and shell | `GuiLauncher`, `HomeWindow`, `PrimeShellView`, `ClientSessionCoordinator` | One application loop. The shell stays available across lobby, match, Results, and the next lobby. The coordinator records `Gateway`, `OnlineHome`, `Lobby`, `Launching`, `InMatch`, `Results`, `ReturningToLobby`, and `Closing` transitions. |
| Account identity | `AccountSession` / `AccountSessions` | Supplies the authenticated account or the explicitly selected guest identity used to obtain Node admission. Gameplay never invents an identity when this is absent. |
| Node control session | Client `NodeSessions` / `NodeControlClient`; server `NodeSessionManager` | Reliable WSS control owns session, lobby, round, handoff, and completion events. Returning between rounds does not close this session. The server retains a disconnected session for its bounded resume grace period. |
| Lobby and round policy | `LobbyManager` and `NodeMatchCoordinator` | Own membership, owner/readiness, map and mode configuration, ballot state, tournament state, Worker placement, per-session notifications, and fresh handoffs. They do not own movement, health, entities, score, or the simulation clock. |
| Worker pool | `WorkerScheduler`, `WorkerRuntime` | The Node selects a healthy Worker with matching content/build/protocol and capacity. A Worker process may host several matches, but every `MatchInstance` is isolated to one round and one immutable `MatchSpec`. |
| Round authority | Worker `MatchInstance` and its `Scene` | Owns the authoritative fixed 60 Hz simulation, direct gameplay UDP, frozen roster/rules, and terminal completion. Report, replay, and telemetry work is bounded and host-owned around the simulation lane. |
| Round presentation | Client `MatchStart`, `AuthoritativePlay`, `Scene`, and `ScenePresentation` | Creates and consumes one client scene per round. `MatchResultsSnapshot` is copied from the immutable replicated authority result; a missing terminal result remains unavailable. |
| Native host | `SdlGameHost` and `SceneHostLifetime` | One native window, GPU device, caches, event pump, and host thread span sequential scenes. Per-scene presentation, GPU command/readback state, input state, and world resources are cleaned before the next scene. |

Control traffic stays on the Node connection. Gameplay traffic goes directly to
the Worker named in the signed handoff. The client does not start or stop a
local gameplay server when a player returns to a lobby.

## Entry and admission

`PlayController` exposes the three online entry actions:

- **Quick Play** ensures a Node, pages its lobby directory, and selects the
  first `Open` lobby with a free human-or-bot slot. A stale revision, capacity,
  full, phase, or not-found join is retried up to three times; if the race
  persists, the UI falls back to Browse.
- **Browse Lobbies** reads bounded pages (16 entries, at most 64 pages),
  deduplicates moving listings, and leaves a continuation cursor when more than
  1024 entries exist.
- **Host Lobby** creates a public lobby on the connected Node. Lobby map and
  mode configuration is accepted only after the Node catalog and local content
  checks agree.

Directory data is fresh for less than 25 seconds. A normal entry operation is
also bounded by a 25-second cancellation deadline. Automatic Node selection
prefers the requested region, then a Node with lobbies and lower population;
full Nodes are excluded. If a previous control session exists, resume is
attempted before fetching a new directory. A failed resume can therefore
recover the same lobby before the player chooses another Node.

## Round launch and completion

When the lobby owner starts, the Node re-reads the current revision, validates
the configured map/mode, readiness, teams, and capacity, then freezes an
immutable `MatchSpec`. The spec contains the roster (players, bots, and
observers), rules, content identity, trust policy, round identities where
applicable, and independent RNG seeds. `NodeMatchCoordinator` places that spec
on a compatible healthy Worker and issues each member a fresh signed handoff
with a fresh nonce.

The client joins the Worker over UDP using that handoff. A generation-scoped
handoff gate permits one launch for one authoritative nonce; cancellation,
Node replacement, or a stale event closes the gameplay transport and prevents
the old operation from launching a second scene. On successful admission,
`MatchStart` creates the round `Scene` and `ScenePresentation`, and the Worker
steps its `MatchInstance` on its single writer.

At terminal state, the Worker emits a bounded `MatchCompletionSummary` after
the authoritative `MatchCompletion` is captured. The Node turns the terminal
Worker event into `NodeMatchEnded` and moves the lobby to `PostMatch`. The
client stops gameplay input, services queued terminal replication for at most
250 ms, and presents the final scene underneath the Results overlay. It never
manufactures a result when the terminal UDP replica did not arrive.

`MatchRunResult` is the client boundary for what happens next:

| Outcome | Client phase |
|---|---|
| `Completed` | `Results`, with an optional `MatchResultsSnapshot` and the Node ballot. |
| `LeftMatch`, `Disconnected`, `Kicked`, `FailedToStart`, or `ClientError` | `ReturningToLobby`, with an actionable error when one is available. |
| `QuitApplication` | `Closing`. |

## Results ballot and continuation

For an ordinary completed round, the Node automatically opens a 15-second
post-match ballot (`Node:PostMatchVoteSeconds` defaults to 15 seconds and is
bounded by Node policy). The electorate is the current non-observer human
membership. Bots and observers cannot vote, and a confirmed vote cannot be
changed. The Results view sends only an option ID plus the current lobby and
ballot revisions; it cannot submit an arbitrary map, path, or rule set.

The Node generates options from its `NodeContentCatalog` and the current mode.
The normal set is:

1. Rematch the current map and mode.
2. Use the next compatible map in the Node's configured rotation.
3. Return to the lobby.
4. Up to five additional compatible maps from that same server-generated
   rotation.

The one-map case intentionally produces a rematch-compatible next entry. The
client may display previews, but the Node validates the selected map/mode again
before creating a continuation. With no votes, the deterministic default is
the Next map entry. Otherwise the highest count wins and equal counts resolve
by the stable option order. A resolved non-lobby choice updates the lobby's
map/mode and creates a fresh `MatchSpec` without requiring a second ready
cycle; the continuation loop checks every 100 ms and allows at most one
placement in flight per lobby. A resolved Return to lobby reopens the lobby
without allocating another match.

`PostMatchView` is both the frozen result display and the ballot surface. It
supports pointer cards, arrows/WASD, Enter, number keys 1–8, and gamepad
direction/A/B input. `PostMatchCommandScope` serializes a pending vote and lets
Leave preempt it. `PostMatchWindow` keeps pumping Node and SDL events while the
completed scene remains visible, then closes for the next handoff or returns
to the shell when the lobby is open.

In-match voting is intentionally optional and deferred as P2 until Results
voting has demonstrated repeated live stability. Results voting is the
canonical implemented vote surface for this lifecycle.

## Tournament separation

Tournament lobbies carry explicit tournament and round identities. At round
end, the Node pauses the tournament and records the completed round instead of
opening the ordinary automatic ballot. Tournament authority selects the next
round identity, map, teams, and observer roles, and separately resumes or ends
the tournament. `PrepareContinuations` excludes tournament state, so casual
post-match voting cannot advance or rewrite a tournament bracket.

## Failure recovery

- Worker creation, placement, content mismatch, timeout, capacity, drain, or
  Worker loss is reported as an interrupted match. The Node cancels the failed
  placement, reopens the lobby, and notifies remaining members; it does not
  open a ballot for an interrupted round.
- A failed gameplay handoff leaves the Node lobby/session intact. The shell can
  retry the handoff or request `match.rejoin`, which obtains a fresh nonce and
  ticket for the frozen reservation. Stale generations cannot reattach an old
  UDP attempt.
- A lost WSS connection is resumable within the server's 45-second session
  grace window. The client tries resume before new directory discovery and the
  Play screen exposes Restore connection or Disconnect when recovery is needed.
- A Node event delivery overflow closes the affected control connection rather
  than silently dropping a terminal event. The session must resume or expire
  normally, preserving the ownership boundary.
- Native or presentation exceptions still run `SceneHostLifetime` cleanup.
  `SdlGpuBackend.EndScene` retires or cancels pending frame work, capture
  results are drained, the `ScenePresentation` and world are closed, input is
  reset, and the window is hidden before the host can accept another scene.

## Validation boundary

Automated source and build checks establish the state machines, bounded codecs,
directory/ballot rules, scene-lifetime cleanup, and Node-to-Worker vertical
contracts. The relevant checks are the solution build, focused client and
Server.Node tests, the content-gated client/Worker vertical test when
`GAME_DATA_DIRECTORY` is available, `python3 tools/check-project-boundaries.py`,
and `git diff --check`.

Those checks are source, build, focused-test, and in-process vertical evidence.
They do not establish native repeated-round GUI/GPU input, rendered Results
readability, or a deployed multiplayer service. Those remain live proof gates
on the target native graphics environment and deployed topology.
