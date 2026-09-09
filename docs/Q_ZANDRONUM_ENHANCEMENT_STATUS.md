# Q-Zandronum-Inspired Enhancement Status

This document records how the Project Prime enhancement plan was resolved. It
separates implemented behavior from measurements and external evidence gates.

## Status by workstream

| Workstream | Status | Evidence and decision |
| --- | --- | --- |
| QZ0 regression corpus | Complete | `MULTIPLAYER_REGRESSION_CATALOG.md` maps 55 deterministic multiplayer invariants to 55 permanent tests tagged `Regression=QZ0`, including the five reusable seeded fixtures required by the plan. |
| QZ1 historical dynamic collision | Implemented, opt-in; rendered loopback integration validated | Each `MatchInstance` owns a fixed 32-tick history for doors, force fields, and moving objects/platforms plus a bounded allocation-free static-query workspace. The deterministic 50-200 ms matrix changes three false-wall/door/field contradictions to zero. A closed compiled AMHE1 fixture also passes the real Node/external-Worker/SDL-client `off|players|dynamic` by `0|50|100|150|200 ms` matrix with 3,795 dynamic queries and zero misses, but naturally observes zero changed outcomes. Controlled local impairment is not internet-path proof, so dynamic geometry stays disabled by the default `players` mode. |
| QZ2 Node waitlist | Complete | The Node owns a bounded, identity-keyed waitlist with personalized offers, reconnect-safe reservations, strict revisions, bot-aware capacity, and a frozen-roster next-match policy. |
| QZ3 semantic feedback | Complete | One match-owned synchronous dispatcher feeds awards, reliable presentation, announcer/HUD, replay markers/raw award facts, and telemetry. Awards are bounded presentation facts and do not affect score or Ranking Points. |
| QZ4 protocol generation | Complete | The Roslyn generator supplies strict bounded Protocol 9 codecs and diagnostics. `JoinPendingPacket` is the first byte-compatible production migration; semantic awards also use a generated packet. Node control JSON remains separate. |
| QZ5 projectile adoption | Rendered loopback measurement complete; adoption stopped | Weapon-confirmed Missile measurements and final-frame captures exist at 100/150/200 ms across all three Worker modes. They use controlled loopback impairment, not a genuine WAN path or completed human visual review, and do not justify shipping new prediction/adoption architecture. |
| QZ6 content manifests | Complete | Required gameplay identity and optional local presentation packs are distinct. The client discovers bounded local packs, saves exact announcer/music choices, injects them into presentation, and falls back to built-ins independently. Optional differences do not enter gameplay compatibility; no downloader or executable content path was added. |
| QZ7 information minimization | Deferred by plan gate | No demonstrated cheating incident/evidence was supplied, so the plan explicitly keeps this work deferred. |
| QZ8 movement mutators | Deferred optional custom-mode work | No movement changes were introduced into the base game. |

## Evidence boundary

The deterministic comparison proves the intended false-wall/door/field outcome
change. The rendered fixture matrix separately proves the real Node handoff,
external Worker, client rendering, same-session reconnect, and dynamic-query
path. It does not naturally observe a contradictory geometry outcome, and its
process-local impairment is not an internet path. Reports therefore retain
`renderedWanProof=false`, `qz1Accepted=false`, `qz5Accepted=false`, and
`requiresHumanVisualReview=true`. Historical dynamic collision remains opt-in
and projectile adoption remains stopped.

The split operator/client/merge harness is ready for a two-endpoint run. Its
ordinary-room same-host smoke at
`output/qz-wan-validation-20260909/split-loopback-smoke-8` passed Node/Worker
orchestration, rendering, bounded debug refresh, reconnect, and strict offline
hash/binding merge. It is explicitly `same-host-split-smoke-non-wan`; no genuine
WAN path or human visual review has been completed, and QZ1/QZ5 acceptance remains
false.

## Validation commands

```text
GAME_DATA_DIRECTORY="$PWD/AMHE1" dotnet test tests/Tests/Tests.csproj -c Release --no-restore
dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --no-restore
dotnet test tests/Protocol.Generator.Tests/Protocol.Generator.Tests.csproj -c Release --no-restore
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --projectile-presentation
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --dynamic-lagcomp-comparison
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --rendered-wan-validation "$PWD/AMHE1" /tmp/qz-rendered-fixture --fixture unit1-rm1-dynamic --mode dynamic --rtt 200 --seconds 12 --reconnect-at 6
python3 tools/check-project-boundaries.py --root .
python3 -m unittest tools.tests.test_project_boundaries
```

The final review should record any unrelated dirty-worktree build or boundary
failure separately from these workstreams.
