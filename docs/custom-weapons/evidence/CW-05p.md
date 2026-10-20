# CW-05p evidence — acquired gameplay wheel

Base: `3ee4d00c9da740d28cc8c05bfab3ebb3d2d587ba` (CW-05o merge).
[Behavior and remaining boundaries](../05p-acquired-weapon-wheel.md).

Desktop Release PASS75.781s, zero warnings/errors, .NET10.0.401/macOS arm64,
DOTNET_PROCESSOR_COUNT=2. Final main SHA256:
`70404130be66962bfb97f64177f208c3d3bf6af5281321c67ce9fd741df4a34b`.
Android Debug experiment PASS74.265s (26 warnings) and default PASS82.377s
(25 warnings), both zero errors. Isolated headless-server compile PASS53.459s,
zero warnings/errors, with early per-project separate obj/bin paths and stable main
DLL. These are compilation checks, not new device/server gameplay runs.
Source stays frozen during all compilation. Tool builds use
BuildProjectReferences=false and verify equal main/tool-copy hashes before/after.

| Acceptance | Result |
| --- | --- |
| Pure custom contracts | 15,289 PASS, including101 new wheel assertions |
| All custom commands including tool build | 18/18 PASS |
| Native GL normal acquired wheel | 142 PASS |
| Native GL acquisition during native hold | 151 PASS |
| Actual offline pickup lifecycle/window launch | 37/17 PASS |
| Acquired keyboard cycling | 31 PASS |
| Scope/local/developer offline/public client | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Synthetic controller enhancements | 75 PASS |
| UI regression | All5 tool build/test pairs PASS |
| Native registry/tables | 78 old names and exact MP/1P weapon tables preserved |
| Native fidelity | Known46/47, raw exit1; unchanged missile splash baseline |

Both GL modes use real stock assets, public availability, actual native movement/
item contact and actual fixed-step input. Mouse delta enters the native input
sampler. The controller fixture injects a named synthetic mapped device through
GamepadManager, then uses production polling/actions/hold/release. It checks
neutral cancellation, a nondefault saved six-entry permutation, frozen targets
when that array changes during the hold, and M7 selection/release. The stylus
fixture injects native pen samples, opens through SEL, hits the exact drawn M7
pixel rectangle and releases through the native two-step debounce. Returning to
Power Beam with simultaneous Shoot emits/debits neither custom nor native fire.

The mid-hold mode walks into the actual pickup while the unowned native wheel
is held, verifies it stays native, releases through its native path, then uses
normal C to equip before exercising the acquired wheel. The ordinary mode
separately retains natural next-input acquisition auto-selection. Neither mode
calls custom Grant/funding or accelerates clocks. Initial native-spawn/free-camera
presentation setup is declared. At the end, a test-only canonical magazine/reserve
depletion models authority ammo changing between highlight and release; a stale
enabled bit cannot Equip the empty M7. It is not an online transport claim.

Native GL uses Apple M4 Pro, GL2.1 Metal. Actual window and offscreen PNGs, renderer/
version, capture SHA256 and draw-purity assertions are retained. Normal and stylus/
controller pictures were visually inspected: M7 highlight, readable disabled
native labels and native return are visible; native mid-hold keeps the original
six boxes. GL NoError and unchanged ownership/resources/actor/timers across draws
pass. This is existing font/flat-box presentation; final rifle art is CW-06.

Pure cases cover detached entries/order,7/8 counts, distinct native affinity IDs,
invalid permutations and bounds, concrete drag endpoints/skipping, extreme and
nonfinite input, controller centers/cardinals/deadzone, full/portrait/landscape/
stylus geometry and transformed hit centers.100,000 warmed interactions allocate
zero bytes. The model holds a bounded eight-entry array per acquired hold; no
broad performance or achieved display-Hz claim is made.

Raw results: `artifacts/custom-weapons/CW-05p-wheel/`. Earlier pure cardinal failures,
ambiguous test GamepadState compile failure and the pre-launch mismatched-DLL
refusal remain separately retained. The production direction fix and fresh-ammo
release gate are included in the final build. Pre-review GL results are not used
as final-build qualification. Fidelity retains weapon.missile.splash tick177,
victim.position4096.z expected52814/actual52815; Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

No new physical-controller/pen, Android interaction, online pickup, refresh-rate or
all-custom-HUD-profile claim. No protocol change relative to main71 or native replay
payload change. Custom replay remains blocked. CI/review are not awaited or
reported PASS. Online acquisition knowledge, world baseline/delivery and final
CW-05/platform gates remain open; CW-16 is incomplete.
