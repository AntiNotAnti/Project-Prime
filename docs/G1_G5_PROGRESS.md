# Prime Hunters G1–G5 implementation progress

The [supplied implementation plan](PRIME_HUNTERS_G1_G5_IMPLEMENTATION_PLAN.md)
is the scope. Classic balance, 60 Hz authority, existing package/namespace
identities, and unrelated working-tree changes are preserved. Protocol 8 remains
unreleased; historical demo adapters remain isolated from live admission.

| Epic | Current implementation | Evidence/disposition |
|---|---|---|
| G1 | Timing projections, bounded render interpolation/late latch, afflictions, spawn policies, and input paths | The final integrated suite is 1122/1122 with `GAME_DATA_DIRECTORY` set to extracted AMHE1. High-refresh rendered acceptance is owner-assumed/waived; no device trace is claimed. |
| G2 | Authoritative feedback, assists, radar/audio, results, retained recaps, and touch navigation | Focused/source behavior is implemented. Visual layout, audio mix, physical touch, and Android acceptance are owner-assumed/waived; see [G2 release acceptance](G2_RELEASE_ACCEPTANCE.md). |
| G3 | Team allocation, overtime, late join/reconnect, world events, weapon queue, browser, status v4, and network HUD | Recorded impaired UDP runs pass; interpolation remains six ticks. External WAN acceptance is owner-assumed/waived; see [G3 WAN acceptance](G3_WAN_ACCEPTANCE.md). |
| G4 | Account/license UI, endpoint-pinned admission, durable reports, PostgreSQL career/history, versioned rating ledger, secure session boundary, and production checks | Backend is 198/198 against isolated PostgreSQL with no skips. `PairwiseNormalizedV1` and schema-2 report outcomes are implemented; public Ranked remains intentionally disabled under Path B pending proof-of-possession. |
| G5 | Delayed observers/cameras, indexed replay, bots/Practice, telemetry, presets/Duel, voting, and tournament controls | Recorded 300-second observer/replay and impaired combat runs pass as separate workloads. The maximum-delay and combined outage soak are owner-assumed/waived. |

## Acceptance boundaries

[G1–G5 stabilization](G1_G5_STABILIZED.md) and [integration evidence](G1_G5_VALIDATION.md)
separate source behavior, recorded automated tests, loopback measurements, owner
assumptions, and unavailable release evidence. The final Release validation is
recorded at implementation head `1c8df59`: Game.sln built with zero warnings and
errors, Server publish succeeded, Android managed arm64 Release built with zero
warnings and errors, Imaging was 18/18, Python 58/58, the Backend was 198/198
against isolated PostgreSQL with no skips, and the project-boundary guard reported
zero violations. The solution has no vulnerable packages.

The final G6 UI acceptance is 14/14 focused tests and 68/68 PNG captures across
1280x720, 1920x1080, 2560x1440, 3440x1440, 360x640, and 768x1024. The physical
Android/high-refresh/audio/touch gates, external WAN run, 30-second/16-observer
gate, and combined bots+observers+replay+telemetry+Backend-outage endurance run
were not executed; the owner explicitly assumed them good for this plan and they
remain administrative assumptions rather than measured proof.

The Backend production runbook, migration/rebuild operation, rating policy, and
outbox state are documented in [G4 production readiness](G4_PRODUCTION_READINESS.md)
and [SERVER.md](../SERVER.md). `Tmds.DBus.Protocol` is pinned to 0.21.3, and the
solution vulnerability audit is clean. Pre-existing `LICENSE` deletion and
maps/Parallax edits are outside this task.
