# PR-07: Bot, input and accessibility parity

Classic/Native remains the default. Momentum uses the same fixed 60 Hz biped
solver for the local human and authority-owned bots. Existing native alternate
forms, abilities, collision, signed intent fields and owner-reported positions
retain their existing owners.

## Corrections found by the new fixtures

An uphill collision can leave `Standing` true and project motion into positive
vertical velocity. The previous contact classifier treated that velocity as an
airborne state and skipped friction. On the authored ramp, neutral Momentum
motion coasted 5.323 units and fell past the ledge. Contact now follows native
standing flags unless the native jump branch accepted a jump. Active native
impulses still bypass the adapter; the horizontal solver never changes Y bits.
The coefficients and profile identity remain unchanged.

The dedicated server retains a presentation intro sequence without a local
main-player camera. The previous eligibility test suppressed Momentum for its
bots throughout the run. Camera exclusion now applies to the main player,
matching the existing native input ownership boundary. The authority fixture
does not end the sequence or fabricate role/readiness state.

Even with correct friction, the old 1.5-unit floor probe left a negative body
margin when the initial position included one maximum-speed integration step
past the first veto. Eligible Momentum footwork now probes ahead by the greater
of 1.5 units and `bodyRadius + retentionSpeedCap / groundFriction` (approximately
1.625 units for stock Hunters). The latter includes a pending integration step
and the remaining geometric neutral coast. Wall/head rays remain 1.5 units;
Native navigation is unchanged. This changes input selection, never velocity.
The actual late-entry ramp regression stops within 0.917 units with at least
0.027 units of body clearance. These are declared initial-cap fixtures, not a
claim that autonomous AI earned that speed or that the bound covers airborne
motion, external overspeed, pads, slippery surfaces or exempt objective routes.

Existing aim turning, projectile-relative dodge prediction, target leading and
reaction delays already use observed velocities and ordinary controls. Those
policies remain unchanged. Bot analog synthesis reaches the ordinary solver and
existing animation/replication paths without a separate physics implementation.

## Input boundaries

Actual scene tests cover all seven Hunters, keyboard/controller forward and
diagonal movement, 25/50/75 percent stick magnitude, asymmetric mixed inputs,
reversal, deadzones, disconnect, remapping, menu/text/focus suppression, native
death/respawn and touch-driven form transitions. They use synthetic device
delivery into production input processing. `HasAnalogMove` describes wire-field
presence; it is not the local analog override's activation state. Current zero
axes preserve digital fallback, while absent/neutral input clears an old analog
override. Canonical and compact packets retain their existing layouts.

The linked, unmodified Android `TouchControls` model emits eight digital
directions. Tests mirror the four existing GameView key assignments explicitly;
they do not pretend that this is an analog Android stick or execute MotionEvent.
The real GL/HUD fixture separately exercises local Samus native absolute pen/touch
input, relative mouse input, device gesture permissions and bound manual boost.
All six paired Native/Momentum alternate-form traces match exactly and execute
zero Momentum solver steps. Fixture setup establishes pointer aspect/settings
before sampling and supplies continued mouse motion across native sample ticks.

Two independent clients per mode also drive the actual touch model through real
localhost UDP with the production impairment queues set to 200 ms added round
trip and 20 ms jitter. Directional, diagonal, release, legal jump, remote movement
and connection checks require positive samples. This proves that boundary under
delay; it is not physical Android or networked absolute-gesture acceptance.

## Validation and integration

Host: macOS 27 ARM64, Apple M4 Pro, .NET SDK 10.0.401/runtime 10.0.12, Release.
The branch incorporates main `421c3728f`, including the merged custom-weapon
mechanics and beam presentation/audio changes. The full movement matrix ran on
`7b2d2c83e`; the final build and focused compatibility checks use the newer main.
Custom weapon mechanics remain outside native movement and replay ownership.

| Check | Result |
| --- | --- |
| Input scenes, seven Hunters | 7,520 assertions |
| Signed packet/override units, no assets | 36 assertions; added to three-OS CI |
| Autonomous offline bots | 56 Hunter/model/difficulty cases; 298,674 assertions |
| Actual dedicated-server bots | 28 scenes; 496,890 assertions; accepted intents, signed solver input, actual snapshot poses and camera regression |
| Navigation geometry and braking | 28 cases; 19,154 assertions; no fallback/overflow |
| Native alternate-form GL input | Six paired scenarios; 11,117 assertions |
| Delayed touch-model UDP, Native | Both clients pass: 6,191 / 6,200 assertions; zero Momentum steps |
| Delayed touch-model UDP, Momentum | Both clients pass: 7,999 / 7,915 assertions; 723 actual solver steps each |
| Native golden trajectories | All 56 unchanged; pinned `dc7f097`, tolerance `1e-5` |
| Native jump/Space Jump/landing | 187,405 assertions |
| Collision | 364 traces; 378,667 assertions |
| Special movement/events | 98,549 assertions; all cases pass |
| Hunter abilities / native carrier | 14,442 / 1,051 assertions |
| Historical rewind geometry | 18,489 assertions |
| Pure math / contact profile | 104 / 20,048 checks; zero allocations over 100,000 warmed profile calls |
| Admission / authoritative / offline / replay rules | 21 / 146 / 29 / 82 checks |
| Compact intents and architecture | Pass, including virtual impaired-history reconstruction |
| Existing Native/Momentum recordings | 76,861 / 76,783 assertions, all 2,982 / 2,979 frames; memory and durable seeks |

The recordings retain their PR-06 aggregate gameplay/presentation digests:
Native `BAE5662C01088E7195A093652563122EAB1B2F6C1E9169D5410BC54FA87618B6`;
Momentum `C866B47544BB8ADB6775B624237EE7D319C4796719A00E24C7A22B9016EA3A5B`.
Playback does not run the owner movement adapter. This is evidence within this
environment and these fixtures, without a cross-platform bit-exact claim.

The final `421c3728f` build passes with zero warnings/errors. Authority-bot,
Native golden, replay/durable, schema-admission, contact-profile and signed-packet
checks were repeated on it; both aggregate replay digests remain unchanged.
The merged beam-effect policy also passes its 275 asset-free assertions.
Authority intents are checked against their actual canonical accepted facts;
their capture pose need not equal either whole-frame entry or final body pose.
No fixture invents a capture-time position contract. The new numeric evidence is
retained in `docs/movement/evidence/pr07-*.json`.

## Reproduction and remaining gates

Build `tools/nettest/nettest.csproj -c Release`. New commands are:

```sh
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-input-packets
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-input-scene "$ASSET_CONFIG" input.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-bot-scene "$ASSET_CONFIG" bots.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-bot-authority "$ASSET_CONFIG" authority.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-bot-navigation "$ASSET_CONFIG" navigation.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-touch-gl "$ASSET_CONFIG" touch-gl.json
```

For delayed UDP, start `--momentum-test-server "$ASSET_CONFIG" 28871 Native 40`
and two independent `--momentum-touch-online "$ASSET_CONFIG" 28871 Native
client.json 12` processes. Repeat with Momentum and another port. Give each
process its own `PROJECT_PRIME_USER_DATA` directory containing only the local
extraction's `paths.txt`. Run the GL fixture in an ordinary desktop process.
Dense traces and recordings remain ignored local artifacts; committed evidence
contains only bounded numeric summaries.

Publication follows the instruction to commit and merge without waiting for CI
or review. CI status is reported separately; local success is not an all-green
claim. The preceding PR's CI exposed a missing portable social-contract source
dependency and a removed Studio parent-focus identifier; narrow follow-up repairs
are prepared. The newly merged procedural beam audio also needs a separate replay
audio-ownership repair. Neither is hidden by weakening movement tests.

PR-08 still supplies the visible authoritative selectors and descriptions.
PR-09 retains the four/eight-player, impaired-network combat, performance,
cross-platform build/input and real-device acceptance gates. Mac and Windows
hardware are available; Windows SSH authentication is awaiting connection setup.
Android device/View integration is unverified. The feature is not complete.
