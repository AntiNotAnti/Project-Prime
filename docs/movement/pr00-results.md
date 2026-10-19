# PR-00: Native baseline and design audit

- Base: `main`, `dc7f097067d755ec76d145fc7cc333068566863d`.
- Branch: `codex/momentum-pr00-audit`; commits are identified in the PR history.
- Scope: documentation, test-only recorder/verifier and golden numeric trajectories.
  Production C# is unchanged. Current numeric protocol documentation is corrected
  to 57; no live protocol bump or packet/schema change.
- Design review: [audit and protected contract](classic-vs-momentum-baseline.md).
  Implementation audit completed; maintainer review/merge remains pending.

## Reproduction and results

Local host: macOS 27.0, Apple M4 Pro, ARM64, .NET SDK 10.0.401/runtime 10.0.12,
Release, headless. Use the pinned SDK (`/Users/jarrett/.dotnet/dotnet` locally);
the system `dotnet` on this host has only the runtime. In the commands below,
`dotnet` means that pinned SDK. `$ASSET_CONFIG` is a directory containing the
operator's `paths.txt` pointing at complete extracted AMHE1 data; assets stay local.

```sh
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build tools/nettest/nettest.csproj -c Release
python3 -m unittest discover -s tools/nettest -p 'test_*.py'
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --movement-validation-shadow
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --q3-mover-source
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --gamemodecheck
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --advanced-rules
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --lobby
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --compact-intents
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --grant-replay
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -frametimingcheck
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -fpsconvertaudit
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
bash tools/check-no-game-assets.sh
```

All above passed locally. The initial clean nettest build had four existing
nullable warnings in RmlUiLobbyAdminController/RmlUiMenuController; zero errors.
Python: 561 tests, one skipped. Game modes: 2,198 lifecycle checks; advanced rules:
130 checks; lobby: 7,750 assertions including real UDP; mover reference: 27 checks;
timed-shot archival: 4,668 assertions; pickup replay: 83; grant replay: 51.
Compact intents includes historical replay byte/layout checks and impaired virtual
transport. These tests retain their existing scope; archival fixtures are not
native visual replay acceptance.

The baseline was recorded with the same command plus `--record` to a new explicit
path, then verified in a fresh process. Recording refuses to overwrite fixtures. A copied golden fixture with a +0.01
position perturbation was correctly rejected at the exact sample/field.
The manifest and seven Hunter parts total about 3 MiB, each part under 0.5 MiB.
They contain values, not extracted assets. Raw local logs are under the worktree's
ignored `artifacts/momentum-pr00/`; checked-in fixture metadata carries provenance.

## Golden measurement scope

Room: `MP3 PROVING GROUND`. Stock biped, one local owner in a ServerSim-owned
scene, dedicated Roam camera, fixed 60-Hz stepping. Seven Hunters × eight fresh
seeded scenarios × 180 measured ticks = **10,080 measured ticks**, plus 120 warmup
ticks per scenario. Samples include idle, forward/strafe/diagonal acceleration
and release, direction changes/reversal, one jump, repeated jumps, and air input
through landing. Every second sample marks a paired 30-Hz boundary; this does not
claim a new original-cartridge 30-Hz reference for all Hunters.

Controls, finite position/velocity/facing, native flags and table values are
captured. Nontrivial input/displacement, upward impulses, repeated jump impulses,
landing and directional reversal have activation checks. Floats use absolute
tolerance **1e-5 engine units**, no relative tolerance; discrete values compare
exactly. Source SHA differs legitimately when verifying later changes.

In this stock fixture all seven Hunters reach maximum forward/strafe `Speed`
0.23999026 and observed upward `Speed` 0.2998047. Those are native internal units,
not gameplay tuning for Momentum. Release slows planar speed to approximately
2.41e-11 by sample 180. Hunter-specific alt mechanics remain outside this fixture.

## CI and limitations

The fetched main commit's five workflow runs were all successful: [build](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39068577097),
[scene ownership](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39068577042),
[semantic foundation](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39068577132),
[campaign acceptance](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39068577036),
and stale-build cancellation policy. PR-specific CI is tracked on the PR;
main's green CI is not reported as evidence for this new diff.

Keyboard aim does not turn the player body through this ServerSim/Roam seam; an
initial activation check correctly failed before any golden data was emitted.
The recorded reversal scenario changes movement direction through actual W/D/S
controls. Camera-facing/aim/HUD parity remains unverified here. Native reference
checks also expose the existing quarter-gravity position difference at paired
30-Hz boundaries; PR-00 does not change or conceal it.

Not established: all-map collision parity, alt forms/special impulses, rendered
cadences, controller/touch, bots, real multi-client movement, historical native
replay seek/export, Windows/Linux/Android execution, or integrated performance.
No public feature is enabled, no new tuning has been approved, and no release
acceptance is claimed. Next slice: PR-01 pure math and allocation/performance tests.
