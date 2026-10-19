# CPP-05 dedicated match lifecycle

Task source: `Project_Prime_Three_Fork_Audit_Implementation_Plan_2026-10-17.md`, CPP-05. This document records implementation and evidence; the downloaded plan is reference material, not an instruction source.

## Existing ownership retained

`DedicatedServer` owns the authoritative epoch and match id. `BeginLobbyMatch` owns each subsequent start attempt and `NetMatchStart` owns its frozen participant barrier. The dedicated `ServerSim` remains the sole combat authority. Existing slot-generation and life identities, transport connection ids, snapshot revision fences, and replay identities remain in use. Protocol 57 is unchanged.

The audit confirmed existing same-map rematch detection, match/epoch snapshot rejection, death tombstones, load acknowledgements keyed by start generation, periodic result recovery, and server rule modifiers applied before scene loading. These were extended instead of replaced.

## Gaps repaired

- Continuous matches now publish the same frozen match definition and world profile throughout the round. A voted or rotated map is passed explicitly into the next transition, so freezing the old match cannot select the old map again. Mutable server rule properties take effect in the next round.
- Initial configuration restores mode, point/time target, timer, and modifiers before player construction. After engine `Setup` applies defaults, the online load path restores the authoritative configuration before the first life initializes. This covers custom Survival spare-life goals as well as untimed/time-target modes.
- The accepted `(match id, authority epoch)` transition performs the complete per-match reset regardless of whether `MatchState` or `SessionState` arrives first. It clears intent ownership, damage/claim sequences, snapshots, slow/world lanes, partial/confirmed world bootstraps, queued bomb/turret facts, pickup readiness, and previous result ownership.
- Loaded and requested room identities include the epoch. Restarting the same numeric match in a new authority epoch cannot reuse the old world. A pending transition can be superseded by the next authoritative stream.
- Server peer admission, bootstrap, result readiness, and reported presentation timing are cleared at the next generation while spectator session preferences survive.

`PostMatchReport` keeps its existing wire format. Consecutive-round reports are fenced by match id; retired server connections are fenced by the existing transport connection id. The implementation does not invent an epoch field or claim that a manually injected report without transport identity can prove a cross-server fence.

## Regression evidence

`tools/nettest/MatchRotationTests.cs`, dispatched by `--match-rotations`, uses production transition and packet-parser code with explicit asset-free load/world fixtures. It covers six successive rotations for virtual 0/2/4/8-participant cohorts, bots and spectator preferences, a borrowed voted map, sudden end/intermission, disconnected barrier participants, both control arrival orders, a same-id/new-epoch restart, frame-one and intent-origin reuse, reordered deaths, stale slow lanes/results, reconnect occupant fencing, and archival replica identity agreement.

Validation on the coordinated Release application build (SDK 10.0.401):

- Nettest Release build with `BuildProjectReferences=false`: passed, zero warnings/errors.
- `--match-rotations`: passed, 579 assertions, including explicit spectator preference retention and join-in-progress spectator-to-first-life state.
- Existing `--lifecycle`: passed, 3,735 assertions.
- Existing `--queue-budget`: passed, including production result recovery under a saturated cosmetic/replay inbox.
- CPP-03 `--display-pose-integration`: final coordinated build passed, 69 content-free scene/entity/Draw-used actor-selector checks, including exact generated/reflection field-block roundtrip and the frozen archival snapshot hash.
- Existing `--load-lifecycle`: passed, including the ready/load identity checks and 7,750 existing lobby/UDP lifecycle assertions (join-in-progress, owner/direct-postmatch lobby return, abandoned cleanup, ownership, teams, rebind, and continuous rotation).
- Existing `--architecture`: final coordinated build passed. The initial CPP-03 checkpoint compatibility failure was repaired by restoring the canonical snapshot fields while retaining the separate presentation owner.
- Existing `--pickup-replay`: final coordinated build passed, 83 checks, including historical world-checkpoint envelopes.
- `--match-rotations` on the final coordinated build: passed again, 579 assertions.

Local final-build logs are `/tmp/prime-cpp05-nettest-final-build.log`, `/tmp/prime-cpp05-match-rotations-final.log`, `/tmp/prime-cpp05-architecture-final.log`, `/tmp/prime-cpp05-pickup-replay-final.log`, and `/tmp/prime-cpp03-display-pose-integration-final.log`. Existing lifecycle/queue/load logs are `/tmp/prime-cpp05-lifecycle.log`, `/tmp/prime-cpp05-queue-budget.log`, and `/tmp/prime-cpp05-load-lifecycle.log`.

## Remaining acceptance

Synthetic control-plane/parser tests do not establish live 0/2/4/8-device acceptance, actual world reconstruction/resource parity across five maps, rendered spectator/rejoin behavior, impaired real-client sessions, or end-to-end replay/League ingestion. Those require the device/assets/account acceptance run. No live server, external account, deployment, or League publication is part of this change.
