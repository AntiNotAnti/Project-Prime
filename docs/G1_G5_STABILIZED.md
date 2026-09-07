# G1–G5 stabilization freeze

This is the release disposition for the G1–G5 work before and alongside the G6
lobby/UI implementation. The supplied stabilization plan remains the scope. The
owner's direction for this run waives the listed physical and endurance gates and
assumes them good; that decision is recorded as an administrative acceptance, not
as test evidence.

## Freeze record

| Field | Disposition |
| --- | --- |
| Repository/checkpoint | `Fruity-Prime`, branch `main`; final implementation head `1c8df59` after lobby `fc5b7dc`, Backend/rating `54722f9`, and client shell `1c8df59`. |
| Wire identity | Authoritative family 2, protocol 8; protocol 8 remains unreleased and clients, match servers, and directories must move together. |
| Simulation/presentation | Server-authoritative 60 Hz simulation; remote presentation remains fixed at six ticks / 100 ms. |
| Replay | Indexed authoritative replay format 3; legacy format 2 remains readable for old fixtures. |
| Backend schema | `20260907102416_InitialAccounts`, `20260907104309_MatchLedger`, `20260907104836_CareerStatistics`, then `20260907171057_RatingLedger`. The rating migration requires the operator rebuild before a production Backend serves traffic. |
| Rating policy | `PairwiseNormalizedV1` (policy version 1), report schema 2, explicit participant outcome reasons, and durable `LastOfficialMatchId`. Public Ranked remains intentionally disabled under the proof-of-possession boundary. |

The worktree was already carrying unrelated `LICENSE` and `maps/` changes. They
remain outside this freeze and are not evidence for it.

## Recorded automated and focused evidence

The final Release validation passed:

| Check | Recorded result |
| --- | ---: |
| Main C# suite | 1122 / 1122 with `GAME_DATA_DIRECTORY` set to extracted AMHE1 |
| Backend suite | 198 / 198 against isolated PostgreSQL, no skips |
| Imaging suite | 18 / 18 |
| Python tooling | 58 / 58 |
| Project-boundary guard | 0 violations |

The final solution Release build completed with zero warnings and errors, the
dedicated Server publish succeeded, and the Android managed Release build used
`android-arm64`, Android SDK/API 36, JDK 21, and `RunAOTCompilation=false` with
zero warnings and errors. It did not exercise an APK, emulator, or physical
device. The final solution vulnerability audit reports no vulnerable packages.

The final tree includes focused tests for rating persistence/rebuild, Backend
security, lobby authority and protocol, session coordination, screen models, and
UI foundation. UI acceptance passed 14/14 focused tests, and the capture matrix
contains 68/68 PNGs at 1280x720, 1920x1080, 2560x1440, 3440x1440, 360x640, and
768x1024. Counts come from the completed validation runs, not source totals.

The recorded headless/network evidence is:

- A 300-second run with eight UDP clients, sixteen observers, three-second delay,
  two rotating AMHE1 rooms, replay, and telemetry completed 18 match epochs with
  no transport queue drops or send errors. Maximum observer age was 192 ticks /
  3.2 seconds. The export contained 18 seekable format-3 replays, 33,324 records,
  89 index entries, 18 gzip telemetry files, and 1,008 telemetry events.
- Corrected 300-second impaired UDP runs at 100 ms RTT, ±20 ms jitter, and 2%
  loss retained eight peers with zero dropped ticks, reliable overflow, and
  transport drops. Tick p95 was 1.8004 ms with compensation and 1.6220 ms
  without it.

These are focused/headless measurements. They do not establish rendered quality,
physical-device behavior, public WAN behavior, or the combined outage workload.

## Acceptance disposition

| Plan gate | Disposition for this run |
| --- | --- |
| G1 high-refresh rendered matrix and input acceptance | **Owner-assumed/waived.** No 60/120/144/165/240 Hz rendered run is claimed. |
| G2 visual, audio, touch, and physical Android acceptance | **Owner-assumed/waived.** Managed compilation and focused checks are not device or mix evidence. |
| G3 external WAN acceptance | **Owner-assumed/waived.** No external Internet endpoint run is claimed; loopback impairment evidence remains separate. |
| S7.2, 30-second delay with 16 observers | **Owner-assumed/waived.** The recorded observer run used a three-second delay. |
| Combined bots + observers + replay + telemetry + Backend-outage endurance | **Owner-assumed/waived.** No one-hour combined run or ten-minute outage/recovery endurance run was executed. |

The user's assumption is sufficient to close these gates administratively for the
requested plan completion. It must not be cited as an executed log, metric, or
device result.

## Backend disposition

The source path is ready for the account and Verified Casual contracts: confirmed
accounts, endpoint-pinned server tickets, immutable authenticated reports,
PostgreSQL projections, durable outbox behavior, and the versioned rating ledger
are implemented. Production deployment still requires the environment, TLS/SMTP,
secret rotation, migration/backup, and operator rebuild steps described in
[`G4_PRODUCTION_READINESS.md`](G4_PRODUCTION_READINESS.md).

Bot fill remains server-owned: bots are ready automatically, never become lobby
hosts, and are excluded from official rating. Lobby chat mute is a local client
projection keyed by published sender identity; administrator mute is the
server-side chat gate. Live movement, combat, score, results, and lobby state
remain authoritative on the server, and delayed observers never fall back to
live state.

Ranked is **intentionally disabled** under Path B until authenticated session
proof-of-possession protects the plain UDP handshake. The hosted lobby owner
capability is a single-use, source-address-bound permission for private lobby
host actions; it is not Ranked session security and does not satisfy that gate.

## Known limitations and finalization

- Final automated validation is complete at `1c8df59`; the exact test/build/UI
  results are recorded above and in `G1_G5_VALIDATION.md` and `G6_ACCEPTANCE.md`.
- The physical, external WAN, maximum-delay, combined-outage, and long device
  tests above were not run in this turn.
- Protocol 8 remains an unreleased compatibility boundary. The client pins
  `Tmds.DBus.Protocol` to 0.21.3, and the final solution vulnerability audit is
  clean.

The G1–G5 baseline is therefore frozen for G6 planning and implementation with
the explicit owner assumptions above. A later release record may replace an
assumed gate only when it contains the corresponding raw run or device evidence.

See [`G1_G5_STABILIZATION_BASELINE.md`](G1_G5_STABILIZATION_BASELINE.md),
[`G1_G5_VALIDATION.md`](G1_G5_VALIDATION.md), and
[`G4_PRODUCTION_READINESS.md`](G4_PRODUCTION_READINESS.md).
