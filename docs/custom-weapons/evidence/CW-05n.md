# CW-05n evidence — offline authored world pickups

Base: `c4426b35fdbe8b656bfdafd92f14f64423186040` (CW-05m merge).
[Implementation and remaining boundaries](../05n-offline-pickups.md).

Final desktop Release PASS86.779s, zero warnings/errors; macOS arm64,
.NET10.0.401, DOTNET_PROCESSOR_COUNT=2. Final main DLL SHA256:
`5ef245c49ff2c7688cf78740bcf909608de4a15a364ae3a674c58c8c261a6837`.
Production source remained frozen during all compiler runs. Tools use
BuildProjectReferences=false; main/tool-copy hashes remain equal and stable.
Android Debug experiment PASS69.970s (26warnings), default PASS62.762s
(25warnings); zero errors. Headless server compile PASS45.793s, zero warnings/
errors, using early per-project isolated obj/bin directories; main DLL unchanged.
These are compilation checks, not Android or dedicated-server gameplay runs.

| Acceptance | Result |
| --- | --- |
| All custom commands including tool build | 17/17 PASS |
| Pure custom contracts | 15,147 PASS, including58 new stock catalog/preflight assertions |
| Actual public offline pickup lifecycle | 37 PASS |
| Actual window/offline launch | 17 PASS, hidden window/headless simulation |
| Scope/local/developer offline/public client | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Synthetic controller enhancements | 75 PASS; no physical-device claim |
| All27 repository pickup scenes | 27/27 PASS,25 assertions per room,15.318s |
| Native GL public pickup | 79 PASS; actual pre/post-contact PNGs visually inspected |
| UI regression | All5 tool build/test pairs PASS |
| Native registry/tuning | 78 old names and exact MP/1P weapon tables preserved |
| Native fidelity | Established46/47, raw exit1; unchanged baseline difference |

The lifecycle fixture starts a real public Proving Ground life empty, verifies
C cannot grant, fires native Power Beam, and walks from its actual native spawn4
into the authored station. Collection occurs in the real fixed-step item phase;
ownership and eligible auto-selection appear on the following tick. Native
weapon availability/ammo arrays stay unchanged and repeated overlap cannot fund
again. The fixture walks away and advances the full1,800 simulation ticks without
a ledger/time shortcut. A later real contact adds16 reserve during a real reload,
retaining magazine and reload/cadence deadlines. It checks actual canonical65
body damage, a pre-death flight resolving after shooter death, an empty native-
firing respawn, a fresh-life reacquisition, simultaneous explicit native input
winning auto-selection, and no delayed retry. Elevated collision and explicit
respawn geometry are declared seams; controls are synthetic.

The separate initial all-map probe passed27/27 actual public scenes with the final
main DLL, exact authored centres and real native walking/item contact. Each starts
unowned, consumes one item, funds4/16 next tick, auto-selects, retains cooldown and
native authored item/spawn identities/counts/types/positions, and keeps health99.
All27 receive distinct nonzero scene epochs. That probe's immutable copied game
DLL and raw results are under `artifacts/custom-weapons/CW-05n-offline-stock27/`.
The tracked runner and equivalent repository fixture are validated separately
below; no private assets or placement-search artifacts enter Git.


The tracked runner passed27/27 fresh processes (`stock27-repository/summary.json`),
25 assertions per room,15.318s; tool SHA274e463f2c1ae49bdc1274a5e1f47ed9a0732b864e39bae9d67f83e4250d2bee.
The subsequent GL-only harness camera fix changed the tool, not production or the
27-room fixture. GL PASS79 in2.010s; tool SHA824e4904ae808413544e5c8a734dc26ac47d1ed97f50c64b37504849551dda66.
Precontact world/window captures show the cyan procedural pickup clearly above
native floor; post-contact first-person capture shows M7 HUD4/16. Visual review
confirms a temporary box proxy and existing cannon viewmodel; final M7 art is CW-06.
The harness uses a declared presentation-only free-camera vantage before real W
collection and returns to Player camera afterward. It checks three proxy boxes
before contact, zero afterward, repeated draw purity and GL NoError. Raw captures,
renderer/version and PNG hashes are in `pickup-gl-camera/`. Initial tool compile
and camera-setup failures remain in `promoted-tool-build.log` and `pickup-gl/`.

Raw builds/custom/UI/fidelity commands, hashes, timings and exits:
`artifacts/custom-weapons/CW-05n-offline-pickups/`. Initial missing-using compile
failure and case-sensitive Gorea Prison fixture failure remain as separate raw
failed attempts; neither is counted PASS. The later final build fixes both.
Native fidelity retains weapon.missile.splash tick177,
victim.position4096.z expected52814/actual52815. Imperialist headshot F2:
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Online pickups/ownership knowledge and baseline replication, ordinary wheel/
cycle entry, physical device acceptance and final CW-06 art are not qualified.
No protocol change; custom native replay remains explicitly blocked. CI/review
are neither awaited nor reported PASS. CW-05 and the CW-16 objective remain open.
