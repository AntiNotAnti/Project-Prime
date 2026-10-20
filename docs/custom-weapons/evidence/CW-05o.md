# CW-05o evidence — acquired weapon cycling

Base: `ff0a40212c266ca57c9347ee16a88d435b7f9660` (latest main, including movement reliability PR852).
CW-05n was merged as `f99af4a6de1e5ee2222f9533c83afc7e708e5160`.
[Selection behavior and boundaries](../05o-acquired-weapon-cycling.md).

Desktop Release PASS67.874s, zero warnings/errors; .NET10.0.401 on macOS arm64,
DOTNET_PROCESSOR_COUNT=2. Final main SHA256:
`19218180cb06cafb68929f4c012f7451c9bdff1495f872ea4915c23c3a4c0a70`.
Android Debug experiment PASS116.348s (26 warnings) and default PASS95.961s
(25 warnings), both zero errors. These are compilation checks, not device runs.
Source remains frozen during compilation. Tools use BuildProjectReferences=false,
with equal main/tool-copy SHA before and after every command.

| Acceptance | Result |
| --- | --- |
| Pure contracts | 15,188 PASS, including32 concrete cycle assertions |
| Real acquired cycle scene | 31 PASS |
| All custom commands including tool build | 18/18 PASS |
| All27 stock pickup scenes | 27/27 PASS,25 assertions/room,25.175s |
| Actual offline pickup lifecycle/window launch | 37/17 PASS |
| Scope/local/developer offline/public client | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Synthetic controller enhancements | 75 PASS |
| Native registry/tables | 78 names and exact MP/1P tables preserved |
| Native fidelity | Known46/47, raw exit1; unchanged missile splash baseline |

The integrated full18-command run includes all31 cycle assertions. A subsequent
tool-only client fixture extension drives Previous Weapon into M7 and verifies the
actual authenticated reliable91 and atomic93 requests carry the canonical owner/
bootstrap/manifest and pending Equip; all24 client-boundary checks pass in2.806s.
Private authority/native snapshots are explicit synthetic facts in that fixture;
it does not qualify real online pickup acquisition.

The new scene fixture acquires M7 through real Proving Ground walking/contact,
then drives actual fixed-step keyboard cycles. It checks native-only/no-ownership
behavior, forward/reverse wrap, both Scroll All modes, Power Beam+M7 round trips,
missile fallback, held controls, native zero-ammo filtering, normal affinity order,
direct native selection priority, simultaneous Next/Previous priority and selection
plus Shoot producing no dual debit/emission. Death clears offline custom suppression.
Initial native spawn setup, later native inventory/ammo top-up and explicit health
zero are declared fixture seams; M7 is never granted or custom-ammo seeded.
The initial extra-native assertion incorrectly assumed spawn universal ammo was
nonzero. The retained failed attempt confirms dry natives were correctly skipped;
the final fixture explicitly checks that behavior before the declared native top-up.

Pure tests assert concrete native/custom sequences, excluded-current fallbacks,
empty M7 escape, native order immutability and bounded order validation. The
production resolver uses a bounded ten-candidate scan with no per-edge allocation;
no broad timing/performance or achieved display-Hz result is claimed.

Raw commands, build logs, timed exits and stable hashes:
`artifacts/custom-weapons/CW-05o-cycle-integrated/`. Pre-rebase qualification and
failed fixture assumptions remain separately under `CW-05o-cycle/`. The first
integrated registry check correctly refused its stale protocol70 expectation after
main advanced to71; the script now pins71 while preserving the exact78-name/
packet-ID assertions. This slice makes no protocol change relative to main.
Fidelity retains
weapon.missile.splash tick177, victim.position4096.z expected52814/actual52815.
Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No new GL/device/online pickup qualification. No protocol or native replay payload
changes. Review/CI are not awaited or reported PASS. Wheel integration, online
pickup correctness and final CW-05/device gates remain open; CW-16 is incomplete.
