# G1-G5 stabilization baseline

This is the S1 technical baseline recorded at the last committed implementation
checkpoint. It captures the evidence already recorded for the G1-G5 work before
the stabilization and G6 UI/lobby work begins. It does not convert the user's
waived or assumed gates into independently executed test evidence.

## Checkpoint and scope

| Field | Baseline |
| --- | --- |
| Repository | `Fruity-Prime`, branch `main` |
| Implementation checkpoint | `f91f4d9` (`fix: align server update package protocol`) |
| Protocol | Live protocol 8; still unreleased |
| Runtime | .NET SDK `10.0.400`, MSBuild `18.9.6`; projects target `net10.0` |
| Configuration | Release |
| Recorded on | 2026-09-07 |

The checkout was already dirty at this checkpoint from an unrelated `LICENSE`
deletion and `maps/`/Parallax edits. Those changes are outside this baseline and
were not modified or included in the evidence below. Generated `bin/`, `obj/`,
`publish/`, and temporary test outputs are likewise not source changes.

The supplied stabilization plan defines S1 as the requirement to record this
baseline. Its S7/S8 instructions additionally call for a one-hour combined soak,
the maximum observer-delay test, and physical render/device acceptance. The user
directed this run to skip those gates and to assume them good. Their status is
therefore recorded separately below.

## Content identity

The test content is the extracted user-owned `AMHE1` multiplayer content from
Metroid Prime - Hunters (USA) (Rev 1). The available reference hashes are:

- source ROM SHA-256: `bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f9`;
- raw ARM9 SHA-256: `2d17479a1c4cbd7f6fce58935b344aeb4607ea0bbf6a30a3edbe01b88c1c8aa9`;
- decompressed/extracted `AMHE1/_bin/arm9.bin` SHA-256:
  `1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b`.

The last value is the source-controlled AMHE1 identity check in
`src/Game/Runtime/ServerContentPackage.cs` and matches the content used by the
recorded real-content checks. The complete extracted directory is not committed;
its per-file/server-package hashes remain in the content baseline and generated
test artifacts.

## Package and schema versions

The direct package references at this checkpoint are:

| Area | Packages |
| --- | --- |
| UI/rendering | Avalonia `11.3.11` (Desktop, Android, Fluent, Inter), OpenTK `4.9.4` |
| Audio/image | SoundFlow `1.4.1`, Silk.NET.OpenAL.Soft.Native `1.23.1`, ReFuel.StbImage `2.1.1` |
| Shared/runtime | CommunityToolkit.HighPerformance `8.4.2`, System.IO.Hashing `10.0.9`, OpenTK.Mathematics `4.9.4` |
| Backend | Microsoft.AspNetCore.Identity.EntityFrameworkCore `10.0.11`, Microsoft.EntityFrameworkCore.Design `10.0.11`, Microsoft.IdentityModel.JsonWebTokens `8.22.0`, Npgsql.EntityFrameworkCore.PostgreSQL `10.0.3` |
| Test infrastructure | Microsoft.NET.Test.Sdk `17.12.0`, xunit `2.9.3`, xunit.runner.visualstudio `3.0.2`, Microsoft.AspNetCore.Mvc.Testing `10.0.11`, Microsoft.EntityFrameworkCore.Sqlite `10.0.11` |

At this historical baseline, the transitive `Tmds.DBus.Protocol` NU1903 advisory
was recorded as a dependency warning; it was not treated as a test failure. The
final client pins `Tmds.DBus.Protocol` to 0.21.3, and the final solution
vulnerability audit reports no vulnerable packages.

The Backend migration chain is:

1. `20260907102416_InitialAccounts`
2. `20260907104309_MatchLedger`
3. `20260907104836_CareerStatistics` (current migration at this checkpoint)

`BackendDbContextModelSnapshot` reports EF Core product version `10.0.11`.
Backend integration evidence used an isolated PostgreSQL `18.6` loopback cluster.
The report/rating path at this historical checkpoint remained explicitly
`policyPending`/null; no rating policy version is claimed by this technical
baseline. The final implementation below supersedes that checkpoint state.

## Final implementation after this baseline

The stabilization and G6 work completed at `1c8df59`, with the lobby vertical in
`fc5b7dc`, Backend security/rating completion in `54722f9`, and the persistent
Prime Hunters client shell in `1c8df59`. The final migration chain adds
`20260907171057_RatingLedger` after the three migrations listed above. Its
operator rebuild must complete before a production Backend serves traffic.

The final validation record is 1122/1122 main C# tests with extracted AMHE1,
198/198 Backend tests against isolated PostgreSQL with no skips, Imaging 18/18,
Python 58/58, zero boundary violations, a successful Server publish, a zero
warning/error Android managed arm64 Release build, and no vulnerable packages.
UI acceptance is 14/14 with 68/68 deterministic captures across the six required
viewports. These results are recorded in [G1-G5 integration evidence](G1_G5_VALIDATION.md).

Replay constants are `DemoFile.FormatVersion = 2` for legacy fixtures and
`DemoFile.IndexedFormatVersion = 3` for the shipping indexed authoritative replay
path. The current live replay evidence is format 3 over protocol 8.

## Automated and build evidence

The recorded Release checks completed with no skipped tests:

| Check | Result | Evidence/source |
| --- | ---: | --- |
| Main C# suite at S1 checkpoint | `954/954` | Historical S1 evidence; final result is 1122/1122 |
| Backend suite at S1 checkpoint | `43/43` | Historical S1 evidence; final result is 198/198 against isolated PostgreSQL |
| Imaging suite | `18/18` | `docs/G1_G5_VALIDATION.md` |
| Python tooling suite | `58/58` | `docs/G1_G5_VALIDATION.md` |
| Project-boundary guard | 0 violations | `docs/G1_G5_VALIDATION.md` |

The recorded build/publish commands were:

```text
dotnet build Game.sln -c Release --artifacts-path <artifacts>
dotnet publish src/Server/Server.csproj -c Release
dotnet build src/Android/Android.csproj -c Release \
  -p:RuntimeIdentifier=android-arm64 -p:RunAOTCompilation=false
```

The solution Release build, dedicated Server publish, and Android managed
Release build were recorded as successful. The observed macOS publish output
contains `publish/osx-arm64/server/FruityPrimeServer`. The Android check used
`RuntimeIdentifier=android-arm64`, Android API/SDK 36, JDK 21, and
`RunAOTCompilation=false`; it is a managed compilation result, not an APK/AOT,
emulator, or physical-device result.

The Backend tests exercised migrations, ticket issuance, UDP admission, ledger
concurrency/rollback/rebuild, and the server outbox to HTTP to committed
PostgreSQL receipt and spool removal path. These are focused loopback checks.

## Executed network, observer, replay, and telemetry evidence

The following runs are evidence already recorded before this baseline:

| Run | Result and limits |
| --- | --- |
| 300-second observer/replay run | Eight human UDP clients, sixteen separate observers, three-second delay, two rotating AMHE1 rooms, automatic replay and telemetry. Eighteen match epochs completed with no transport queue drops or send errors; maximum observed observer age was 192 ticks / 3.2 seconds. |
| Replay/telemetry export from that run | Eighteen seekable format-3 replay files, 33,324 records, 89 index entries, eighteen gzip telemetry files, and 1,008 telemetry events. Four analysis commands processed the completed export. Raw report: `/private/tmp/codex-re-prime-g5/observer-reliable-300.json`. |
| Impaired mixed combat | Corrected 300-second ON and OFF runs used eight UDP clients, 100 ms RTT, +/-20 ms jitter, and 2% loss. Both retained eight peers with zero server/client dropped ticks and zero reliable overflow/transport drops. Tick p95 was 1.8004 ms (ON) and 1.6220 ms (OFF). Raw summaries: `/private/tmp/codex-re-prime-g5/mixed-reliable-final/summary.json`. |
| Bot and Backend focused checks | Actual UDP bot retirement/mixed human-bot team checks and individual Backend outbox/admission/recovery checks passed as documented in `G1_G5_VALIDATION.md` and `G1_G5_PROGRESS.md`. They were separate workloads. |

These measurements establish bounded behavior for the workloads named above.
They do not establish rendered gameplay quality, physical-device behavior,
external WAN behavior, or a combined release soak.

## User-waived or assumed gates

The following status is an explicit record of the user's direction for this run,
not a claim that the checks were executed:

| Plan gate | Status for this baseline |
| --- | --- |
| Maximum spectator delay: 30 seconds with 16 observers | **Waived/assumed by user.** The executed observer run used a three-second delay with 16 observers. No 30-second-delay evidence is claimed. |
| Combined bots + observers + replay + telemetry + Backend-outage endurance | **Waived/assumed by user.** No one-hour combined run or ten-minute Backend outage/recovery endurance run was executed. Focused bot, observer/replay/telemetry, and outbox-recovery checks remain separate. |
| Physical Android, high-refresh, and long device tests | **Waived/assumed by user.** The managed Android build and source/focused render checks do not replace physical Android or 60/120/144/165/240 Hz rendered acceptance. |

The plan's S8 `G1_G5_STABILIZED.md` gate should not be inferred from this S1
record alone. This file preserves the exact distinction between the historical
checkpoint, the final focused validation, and the user-authorized assumptions so
that G6 acceptance does not rewrite test history.

## Source references

- [G1-G5 integration evidence](G1_G5_VALIDATION.md)
- [G1-G5 implementation progress](G1_G5_PROGRESS.md)
- [G5 replay format](G5_REPLAY.md)
- [G5 telemetry](G5_TELEMETRY.md)
- [AMHE1/ranking content reference](G4_RANKING_SPEC.md)
- [stabilization plan](PRIME_HUNTERS_G1_G5_STABILIZATION_TO_G6_UI_LOBBY_PLAN.md)
