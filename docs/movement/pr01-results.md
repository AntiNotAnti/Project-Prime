# PR-01: Pure horizontal Momentum math

- Main base: `c6d8806309841bd47e5762ca58b9cbfd0552fb09`, after #702 merged.
- Dependency/base branch: `movement/pr00-audit`, commit
  `43a7975ebc349cd9ef1f477d1c22fbf2ce73269d` ([PR-00](https://github.com/AntiNotAnti/Project-Prime/pull/704)).
- Slice branch: `movement/pr01-math`; the PR history identifies its commit.
- Files: one independent production math file, nettest cases/entrypoints, a
  Windows/Linux/macOS CI workflow, contract/results and primitive benchmark data.
- Rationale: isolate horizontal equations and numerical validation before touching
  native input, collision, abilities or authority. Immutable value input/profile,
  stateless output, no new dependency and **no runtime gameplay call site**.
- Native semantics protected: all original golden trajectories pass; no changed
  protocol, packet, replay schema, scene/player fields or movement selection.

## Executed validation

Host: Apple M4 Pro, ARM64, macOS 27, SDK 10.0.401/runtime 10.0.12, Release. As in
PR-00, `dotnet` below means the pinned local SDK. `$ASSET_CONFIG` contains the
operator's local `paths.txt` for complete extracted AMHE1 files; none are committed.

```sh
dotnet build tools/nettest/nettest.csproj -c Release
dotnet build tools/nettest/nettest.csproj -c Release -p:BuildProjectReferences=false
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-math
DOTNET_TieredCompilation=0 dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-benchmark artifacts/momentum-pr01/benchmark-tiering-off.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --compact-intents
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --grant-replay
git diff --check
```

Builds passed with zero warnings/errors. The second build compiles additional test
cases against the already-built unchanged production assembly. Math: **104 checks
passed**, including rotated/signed bases, complete gradual reversal through zero,
partial air projection, analog/radial inputs, invalid/extreme values, unit scaling,
Y bit preservation and six synthetic host cadences at fixed 60-Hz simulation dt.
Equation tolerance is absolute `2e-5`, relative zero; exact-step/bit tests use zero.
Native: **56 original trajectories / 10,080 measured ticks passed**, tolerance
`1e-5`, without changing expected values. Selected owner-position, compact intent
and archival replay regression suites passed. The broader merged-main checks,
including 639 Python tests (one skipped) and 7,746 lobby assertions, are recorded
in [PR-00 results](pr00-results.md).

## Performance evidence

Both the normal-runtime and tiering-disabled workload passed **zero measured
allocations** over 819,200 player steps, following 122,880 warmup steps. Eight
scalar player states use precomputed wish reversals/ground-air phases. Results,
CPU/build context and exact solver-source hash are in
[benchmark evidence](evidence/pr01-macos-arm64-benchmark.json).

With tiered compilation disabled, normalized batch timings were about **131 ns
average, 109 ns p95 and 3,062 ns p99 per player step**. A concurrent native-regression
run with runtime defaults measured about 342/1,389/6,874 ns respectively. The host
also had compiler activity; these are shared-machine observations, not throughput
guarantees. The mean can exceed p95 because a few batches were much slower.
No timing gate was weakened or selected from repeated favorable samples. These
statistics are batch times divided by 4,096, not individual-step tail latencies.
The provisional +5% integrated scene p95 gate is **not evaluated** in this slice.

## CI, limitations and remaining work

CI is wired to run the real production library through nettest on Windows, Linux
and macOS, enforce zero allocations and retain per-machine JSON. PR-specific CI
was pending at publication and is tracked on the PR; local macOS results are not a
claim that cross-platform CI or real devices have passed. Commits/publication do
not wait for review or CI completion.

Publication snapshot: PR-00's broad **“no game assets” job failed at its earlier
“isolated Social and Party handler contracts” step**, before the asset scan. The
compiler reports CS0006: missing
`MphRead.Protocol.Generator/bin/Release/netstandard2.0/MphRead.Protocol.Generator.dll`
in `tools/native-party-travel-source-check`. The identical failure is present on
merged main `c6d880630`, so it is an inherited clean-build prerequisite issue,
not evidence of a new movement or proprietary-asset failure. Evidence:
[main job](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39291170828/job/118191884174)
and [PR-00 job](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39291942098/job/118194544360).
PR-01's new math CI was running/queued at this snapshot; it is not reported passed.
The inherited CI failure remains unresolved and prevents a claim of full green CI.

No gameplay tuning, biped adapter, legal jump/landing model, bhop speed ceiling,
collision changes, input routing, match-authoritative mode, replay rule extension
or lobby UI ships here. Those remain PR-02 through PR-09, including real multiplayer,
impulse/special-form, seek/export and device acceptance. The new main's private
Trooper wall-cling owner must be excluded or explicitly coordinated at integration.
The library only validates and updates planar velocity; it cannot certify gameplay
safety on its own. Review/merge remains open; the feature is **not complete**.
Next: PR-02 developer-only offline biped integration with Native fixtures unchanged.
