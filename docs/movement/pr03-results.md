# PR-03: Manual hops, air steering and v1 private tuning

- Main base: `63a3212219984d126a57abf54e8755e259e20738`, including #708, #704/#705, #706, #707 and #709.
- Compatibility prerequisite [#709](https://github.com/AntiNotAnti/Project-Prime/pull/709)
  is merged; protocol 58 archive readers were fixed independently before this slice.
- [PR-02 #707](https://github.com/AntiNotAnti/Project-Prime/pull/707) is merged,
  implementation commit `2315b3d1f14eb606ad0a777dea56725f81f0c4e0`.
- Slice branch: `movement/pr03-jumps-tuning`; the PR history identifies its commit.
- Files: immutable profile/contact/cap policy, one accepted-native-jump boolean at
  the existing seam, bounded diagnostics, pure and offline scene tests, runner/CI,
  tuning and evidence. No new dependency, wire field or persistent gameplay state.
- Native protections: original jump branch, Y impulse, abilities, gravity,
  `UsedJump`, collision, special bypasses and default movement remain native.
- Policy/rationale: [v1 tuning](tuning-v1.md). Manual jumps only, no new auto-hop,
  buffer or grace timer. A legal native takeoff skips ground friction; an ordinary
  landing receives it. Native flags supply the contact history.

## Latest-main compatibility

The movement stack was rebased after #708 and refreshed after #704/#705/#706. #708
changes presentation, session rules and live protocol, so the original plan's
protocol 57 / free bit 14 assumptions are obsolete. Current protocol is **58**;
ThirdPersonCamera owns bit 14 and session mask 32767. Bit 15 currently fits the
remaining `ushort` rule space; no Momentum bit is reserved yet. Recheck current
main again before PR-05. The adapter continues to use canonical body/aim vectors,
not third-person render offsets. The declared archival schema remains unchanged.

## Executed checks

Host: Apple M4 Pro, macOS 27 ARM64, SDK 10.0.401/runtime 10.0.12, Release. Scene
room MP3 PROVING GROUND, one genuine offline local Hunter, fixed 60-Hz simulation,
profile v1. Numeric calibration covers all seven stock Hunters. No network
impairment or physical device input is exercised. `dotnet` below denotes the
pinned local SDK, and `ASSET_CONFIG` contains operator-supplied complete AMHE1
files and `paths.txt`; none are committed.

```sh
dotnet build tools/nettest/nettest.csproj -c Release
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-profile
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-math
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-admission
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol58-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-jump-scene "$ASSET_CONFIG" artifacts/momentum-pr03/jump-scene.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-scene "$ASSET_CONFIG" artifacts/momentum-pr03/offline-scenes.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --compact-intents
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --grant-replay
git diff --check
bash tools/check-no-game-assets.sh
```

Pure profile: **20,046 checks passed**, including two 10,000-turn air-wish stress
sequences and zero allocations over 100,000 warmed profile calls. Pure math:
**104 passed**; scene admission/schema: **21 passed**.

On main `862acd431` plus the gameplay slice, the seven-Hunter jump suite passed
**187,405 assertions** across 126 fresh scenes: 56 paired Classic/Momentum cases,
seven Native calibration scenes and seven cadence/reference scenes. The paired
Momentum cases exercised **14,420 solver calls**, with 161 legal jumps, 9,646
airborne, 70 landing and 4,543 grounded samples. All seven Hunters showed six
nonzero moving-jump retentions (42 total), with exact before/after X/Z at the
no-input takeoff seam. There were also 42 neutral-ground braking checks. Held,
before/on/after-landing and native Space Jump cases preserve Y/flags/accepted
impulse ticks. Every stock calibration reproduced rise 2.4658203, apex tick 32 and
landing tick 65. These scenes do not reach the ceiling; pure stress exercises it.

Existing offline integration: **57,346 assertions passed**; original golden
Native values: **56 trajectories / 10,080 ticks passed**, unchanged at `1e-5`.
Numeric summaries: [jump/calibration evidence](evidence/pr03-jump-scenes.json)
and [integration evidence](evidence/pr03-offline-scenes.json). Full logs remain
locally under `artifacts/momentum-pr03/`.

The final integrated Release build with #709/#706 passed with zero warnings/errors.
The tested base tree `166434aae` is byte-for-byte identical to merged main
`63a321221`, despite different merge ancestry. On that integrated tree, the
20,046 profile, 104 math, 21 admission, 142 protocol-58 compatibility, architecture,
compact-intent, timed-shot (4,668), pickup replay (83) and grant replay (51) checks
passed again. The pre-fix archive failures and independent clean-main reproduction
are documented in [protocol 58 compatibility](../network/protocol58-replay-compatibility.md).
Both asset-backed scene suites also passed again on this final integrated build:
**244,751 assertions total**, with refreshed numeric summaries. The 56 original
Native trajectories passed again unchanged. Tracked-file asset guard and diff
whitespace checks passed.

## Numerical correction

The sustained perpendicular air-wish fixture initially failed its existing `1e-5`
bound. A float rounded just above the cap became the next step's incoming
overspeed budget, allowing cumulative gain. Scaling now rounds components inward
and checks the resulting circle. The original bound was retained; direct physical
units and native ×30 / ÷30 stress each run 10,000 turns. The profile also enforces
zero measured allocations over 100,000 warmed calls. These are solver/profile
checks, not the integrated +5% p95 scene-performance acceptance.

## CI, limits and remaining work

The real-library asset-free CI matrix now includes v1 contact/retention tests on
Windows, Linux and macOS. Local asset-backed acceptance is separate. Publication
does not wait for CI/review; the PR records its current status. Historical
generator/UI/Windows allowlist failures were addressed by merged #706. Its merge
does not prove the newest full matrix passed. PR-specific CI is pending at
publication; no full green CI is claimed.

The 1.5 retention multiplier is a private candidate safety budget. It is not proof
of balanced competitive combat, every-map collision safety, or legitimate impulse
classification. Genuine pad/bomb/teleport/platform/surface and form events remain
PR-04. Mouse/controller/touch and bot input integration remain PR-07; real render
cadences/devices, network/rewind, whole-scene performance and release policy remain
PR-09. Synthetic host scheduling is identified as such. PR-05/06/08 still need
authoritative rules, protocol compatibility, replay ownership and UI.

Stock spawn does not grant Space Jump. Its test sets only the dormant native
ability bit; it does not rewrite impulse or `UsedJump` state. Held and rejected
airborne presses do not become queued landing jumps. The profile retains incoming
overspeed without allowing further steering growth, and `IncomingOverCap` remains
a kinematic observation rather than an anti-cheat/provenance assertion.

Review/merge remains open. The feature is **not complete**. Next slice: PR-04
collision, impulses and full special-transition acceptance.
