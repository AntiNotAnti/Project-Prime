# G1 baseline — Prime Hunters

- Repository: `/Users/jarrett/Documents/Development/Fruity-Prime`
- Baseline commit: `b31bc5764b01da0d8dac8b1f261b11e2d791e312`
- SDK: `/tmp/codex-re-fruity-refactor/dotnet10/dotnet` (`10.0.400`)
- Data gate for the main C# tests: `GAME_DATA_DIRECTORY=/Users/jarrett/Documents/Development/Fruity-Prime/AMHE1`
- Artifacts: `/private/tmp/codex-re-prime-g1/baseline`
- Logs: `/tmp/codex-re-prime-g1/`

## Commands and results

| Check | Command | Result |
|---|---|---|
| Game solution Release build | `/tmp/codex-re-fruity-refactor/dotnet10/dotnet build Game.sln -c Release --artifacts-path /private/tmp/codex-re-prime-g1/baseline/solution` | **PASS**, 0 errors, 6 NU1903 warnings |
| Main C# tests | `env GAME_DATA_DIRECTORY=/Users/jarrett/Documents/Development/Fruity-Prime/AMHE1 /tmp/codex-re-fruity-refactor/dotnet10/dotnet test tests/Tests/Tests.csproj -c Release --artifacts-path /private/tmp/codex-re-prime-g1/baseline/main` | **PASS**, 434 passed, 0 failed, 0 skipped |
| Isolated imaging tests | `/tmp/codex-re-fruity-refactor/dotnet10/dotnet test tests/Imaging/Imaging.Tests.csproj -c Release --artifacts-path /private/tmp/codex-re-prime-g1/baseline/imaging` | **PASS**, 18 passed, 0 failed, 0 skipped |
| Python tests | `python3 -m unittest discover -s tools/tests -p 'test_*.py' -v` | **PASS**, 47 tests, 0 failures |
| Nettest Release build | `/tmp/codex-re-fruity-refactor/dotnet10/dotnet build tools/nettest/nettest.csproj -c Release --artifacts-path /private/tmp/codex-re-prime-g1/baseline/nettest` | **PASS**, 0 errors, 4 NU1903 warnings |

The solution build also produced the imaging, main test, and nettest assemblies under its own artifact directory. The standalone commands above are the authoritative counts for each requested test project.

## Known warnings and failures

- The initial unit/build checks had no failures; the extended network checks below include failed acceptance gates.
- The existing `Tmds.DBus.Protocol` 0.21.2 dependency reports NU1903 (known high severity advisory GHSA-xrw6-gwf8-vvr9): six warnings in the solution build and four in the nettest build. The main test build emitted the same dependency warning; the imaging-only project emitted none.
- The initial table covers unit/build checks. Extended WAN, content and platform results are recorded below and in G1_NETWORK_BASELINE.md.

## Worktree preservation

The pre-existing `LICENSE` deletion and dirty `maps/` parallax changes were left untouched. No production or test source files were modified for this baseline. The plan and map-art files visible in the final status were also left untouched.

## Extended harness failures reproduced before behavior changes

The following fail twice on the frozen b31bc57 solution binaries. Exact commands,
stdout/stderr and repeat exit codes are in `/tmp/codex-re-prime-g1/network/`.

| Fixture | Observed failure | Source finding |
|---|---|---|
| `--match-lifecycle DATA` | Child refuses server launch | Fixture still launches `FruityPrime.dll -server` instead of standalone Server |
| `--catch-up DATA AMHE1` | Pending beam received an extra scene step | Fixture creates independent combat owners while scene services still point to the initial simulation owner |
| `--homing DATA AMHE1` | Delayed homing acquisition/catch-up was not enabled | Same stale fixture combat-owner assumption |

Fixture-only repairs in `34d3549` retained the assertions. All three repaired checks pass separately from the frozen baseline. No gameplay balance change was made for these fixture failures.

The real-content scoring, phase, history, bomb, weapon and shared-lock fixtures, all twelve world modes, direct combat and spectator checks pass. Both WAN matrices pass 16/16 cases; deterministic lag compensation passes 30 paired comparisons. Five-minute mixed runs fail: one reliable queue overflow, server scheduling drops in OFF, and client scheduling drops in an unchanged ON repeat. The overflow did not reproduce in that repeat. See [full network baseline](G1_NETWORK_BASELINE.md) for exact metrics and uncertainty.

## Observation limits

Socket fixtures do not create a rendered client. Render frame timing at
60/120/144/240 Hz, interpolation/prediction statistics and perceived look latency
are therefore not inferred from the headless numbers. Server input starvation
and skip counters exist but are not emitted by the current baseline logger.
Real Android visual acceptance requires hardware under the supplied plan.

Source review also found a pre-existing rendering/collision dependency in
PlatformEntity: drawing sets WasDrawn and model animation matrices subsequently
feed collision attachment transforms. New render interpolation must isolate and
restore these caches, or evaluate collision pose in simulation, before blending
platform visuals. This is a source finding, not a measured rendering failure.

## Platform baseline

Fresh Android managed Release (AOT disabled for this check) builds with zero warnings/errors. Standalone osx-arm64 Server publication, dedicated connection smoke and package/map guards pass. These are frozen-baseline results, not validation of later protocol changes.

The native render probe fails before gameplay: `NSGL: The compatibility profile is not available on macOS`. FrameTiming arithmetic checks pass at simulated 60/144/240 schedules, but no real high-refresh capture, rendered demo or Android device acceptance was obtained. Commands and logs are retained in `/tmp/codex-re-prime-g1/baseline-platform.md`.

The five-minute timing results were collected on the shared development host; concurrent implementation builds were possible. Scheduling overruns therefore cannot be attributed to a gameplay-cost regression. G3 diagnostics now separate reliable capacity/ID-window refusal and reject stale-snapshot client health; the frozen results above remain unchanged.
