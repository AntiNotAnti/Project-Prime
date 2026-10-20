# CW-05k evidence — retire menu selection

The user's world-pickup clarification supersedes the attached loadout/menu
proposal. [Current contract](../05k-pickup-correction.md). Base:
`e17e06273c8194c669dd71ecaf064815539b8691` (CW-05j merge).

This slice removes custom Equip/Return menu controls, controller facts/actions,
Scene selection mailbox, cached publications and their obsolete tests. Physical
input, native-return fencing, custom authority, resources, optics and HUD retain
their established paths. Native menu action IDs 0–33 remain unchanged. The
BotSelect–BotApply range guard remains so later journal actions cannot enter bot
administration.

Desktop Release PASS 140.094s, zero warnings/errors. SHA256:
`ebf1311ce1170b6ef251acfc4b89209d1311db0d6efba821cfc0db9ba212b631`.
Android Debug experiment PASS 145.640s, 26 warnings; default PASS 66.885s,
25 warnings; both zero errors. macOS arm64, .NET 10.0.401,
DOTNET_PROCESSOR_COUNT=2. Source was frozen during builds. Tools use
BuildProjectReferences=false; final tool copies and before/after hashes match.

| Acceptance | Result |
| --- | --- |
| Custom suite, including build | All 17 commands PASS |
| Pure identity/resource/source/control/replica/payment | 14,807 checks PASS |
| Actual offline availability/window launch | 18/17 PASS; transitional spawn grants still exist |
| Scope/local/offline/client boundary | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Focused synthetic controller mapping | 75 PASS; no physical controller claim |
| UI runtime/offline Rules/live-menu/actual Rules/in-game restoration | All five build/test pairs PASS; restoration 46 |
| Native registry/tuning | 78 PASS; exact native tables preserved |
| Fidelity | Known 46/47, raw exit 1; established missile rounding divergence |

Fidelity differs only at weapon.missile.splash, tick 177,
victim.position4096.z expected 52814/actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Complete commands, raw exits, timings and hashes are in
`artifacts/custom-weapons/CW-05k-pickups/`.

The unmerged responsive-menu experiment was stopped and preserved separately
when the user clarified pickup gameplay. Its captures and results are not
qualification for this slice. A read-only Android emulator candidate discovery
proved Rules OFF→ON persistence and native Battle/FFA/zero-bot launch, then
exposed missing Android custom-runtime activation. That older APK is not final
CW-05k runtime evidence, and no M7 pickup/equip/fire claim follows from it.

Pickup acquisition is **not implemented by this removal**. Public automatic
spawn grants still need replacement; canonical item identity, contact, refill,
respawn, replication and normal weapon-cycle selection follow immediately.
Developer grants may remain as explicit combat fixtures. CW-05 and the full
CW-16 task remain incomplete. CI/review are neither awaited nor reported PASS.
