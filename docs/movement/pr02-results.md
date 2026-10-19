# PR-02: Offline biped integration

- Main base: `c6d8806309841bd47e5762ca58b9cbfd0552fb09`, including merged #702.
- Dependency: [PR-01 #705](https://github.com/AntiNotAnti/Project-Prime/pull/705),
  branch `movement/pr01-math`, commit `477caf256bd64b44073b587c240f824f53dc4de3`.
- Slice branch: `movement/pr02-offline-biped`; the PR history identifies its commit.
- Production: scene-owned immutable developer selection, player adapter,
  `PlayerInput` seam, shared input suppression, comparison-shadow exclusion and
  desktop developer command. Tests: CPU scene acceptance, asset-free admission,
  runner and CI entrypoints. No new dependencies or imported third-party code.
- Design and temporary profile: [offline integration contract](offline-developer-integration.md).

## Validation

Apple M4 Pro, macOS 27 ARM64, SDK 10.0.401/runtime 10.0.12, Release. Multiplayer
room: MP3 PROVING GROUND; one genuine local offline Hunter, fixed 60-Hz simulation,
profile v0. No rendering cadence or network impairment is exercised. The following
commands use the pinned local SDK as `dotnet` and an operator `ASSET_CONFIG` with
complete AMHE1 files:

```sh
dotnet build tools/nettest/nettest.csproj -c Release
dotnet build tools/nettest/nettest.csproj -c Release -p:BuildProjectReferences=false
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true -p:OutputPath="$SERVER_OUTPUT/"
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-math
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-admission
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-scene "$ASSET_CONFIG" artifacts/momentum-pr02/scene.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --compact-intents
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --grant-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --movement-validation-shadow
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --q3-mover-source
python3 -m unittest discover -s tools/nettest -p 'test_*.py'
PROJECT_PRIME_USER_DATA="$ASSET_CONFIG" dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -momentumdev "MP3 PROVING GROUND" -hunter Samus -seconds 3
git diff --check
bash tools/check-no-game-assets.sh
```

Math: **104 passed**. Admission: **21 passed** using real scene ownership with
synthetic room readiness and temporary role values; no transport or physics.
Declared archival contract remains
`A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A`.
The adapter adds no declared checkpoint state or wire field. Production desktop,
final test and separate-output server builds passed with zero warnings/errors.

Asset-backed offline scene suite: **57,346 assertions passed**, 84 fresh scenario
scenes (seven Hunters × six scripts × two modes), plus two gate scenes. It measures
15,120 total ticks, including **7,560 Momentum solver steps**, 7,182 clear final
velocity comparisons and 2,240 moving comparisons. All seven jump-only vertical
trajectories and jump/ground flags match Classic. Numeric tolerance is `1e-5`;
adapter Y bits are exact. The [numeric summary](evidence/pr02-offline-scenes.json)
contains invocation and final-velocity counts, not game assets.

Original Native golden suite: **56 trajectories / 10,080 ticks passed** at `1e-5`,
without changing expectations. Architecture, compact intents, timed-shot archival
(4,668 assertions), pickup/grant replay, movement-validation shadow and Q3 mover
source suites passed. Python: **639 tests passed, one skipped**. These archival
checks protect existing Native behavior; no Momentum recording mode exists yet.

During harness development, attempted use of the player presentation camera
required native HUD/OpenGL resources. The final CPU harness keeps Roam, uses
the actual player-registry/local-slot identity, and skips the intro through its
native end operation. This uncovered and corrected presentation-dependent gating
before publication. No production HUD guard or expected Native fixture was changed.

## CI and remaining work

The asset-free admission check joins the existing real-library math/allocation
matrix on Windows, Linux and macOS. Asset-backed scene checks require locally
supplied files and are not silently reported as CI coverage. PR-specific CI is
pending publication; it does not block committing this independently gated slice.

At this snapshot PR-01 math/allocation CI passed Windows and Linux; macOS remained
queued. Repository-wide failures in Social/Party generator setup, UI combat-tab
acceptance and Windows authored-asset allowlist handling are being addressed by
[PR #706](https://github.com/AntiNotAnti/Project-Prime/pull/706). That PR is open,
not part of this base. No overlapping movement sources were changed there. Full
green CI is not claimed.

The real macOS desktop developer command loaded the room, selected profile v0,
initialized the OpenGL renderer and closed at its 180-tick bound with exit 0. This
is a launch/render smoke only; no physical movement input was exercised. Server
compilation used a separate output directory, preserving the desktop smoke binary.
The full tracked-file asset guard passed: no game assets are being published.

The experimental gate protects normal offline, online and replay execution.
Actual keyboard/mouse gameplay, controller/touch, Windows/Linux/Android devices,
collision sweep stress, special-impulse gameplay, integrated allocation/timing
budgets, and multiplayer/replay Momentum behavior remain **unverified**. The pure
solver's PR-01 zero-allocation result does not establish whole-scene performance.
No approved v1 tuning, auto-hop, buffering, authoritative match rule, protocol bump,
replay adapter, bot integration or lobby UI ships here. PR-03 through PR-09 remain
required. Review/merge remains open; the feature is **not complete**.
