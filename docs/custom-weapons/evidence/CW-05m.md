# CW-05m evidence — bounded world pickup arbitration

Base: `c2b386a8eeea41e85cd679a543666aca7baf034e` (CW-05l merge).
[Layout, ledger and engine-adapter contract](../05m-world-pickup-ledger.md).

Desktop Release PASS 66.408s, zero warnings/errors; macOS arm64, .NET10.0.401,
DOTNET_PROCESSOR_COUNT=2. Final main SHA256:
`71f70bbee44e8718bca6ad9930a2a824b644d2964cd589e3db0f9132ab593306`.
Source remained frozen through compiler runs. Tools use BuildProjectReferences=false;
main/copied hashes are stable before/after all checks.
Android Debug experiment PASS 72.637s (26 warnings), default PASS 65.803s
(25 warnings); both zero errors. These are compile checks, not new device runs.

| Acceptance | Result |
| --- | --- |
| Focused layout/world ledger | 139 assertions PASS |
| All pure custom contracts | 15,089 checks PASS |
| All custom commands including tool build | 17/17 PASS |
| Existing offline availability/window launch | 18/17 PASS; transitional public spawn grants remain |
| Scope/local/developer offline/public client | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Synthetic controller mapping | 75 PASS; no physical-device run |
| Native registry/tuning | 78 names and exact MP/1P tables preserved |
| Fidelity | Established 46/47; raw exit1 |

The focused suite checks detached/canonical layouts, every placement field,
full map GUID/name/content/package hashes, exact manifest/registry identity,
strict Unicode encoding, hard 64-placement cap and infinite enumeration cutoff.
Contact cases cover exact radius/height boundaries, alt-form spheres, gates,
foreign world/current-time refusal and 2,000 parity samples against native
geometry. Actual empty journals are funded through the trusted callback, while
one competing owner is refused for the consumed incarnation.

Other cases cover exact initial/respawn ticks and uint wrap; definite callback
refusal without consumption; reentrant collection/advance/ACK/state-read refusal;
128 pending outcomes applying pressure before funding; exact FIFO head release
and unchanged-incarnation retry; exception and malformed-success quarantine.
100,000 warmed collection/publication cycles and 100,000 state-peek/quiet-advance
cycles each allocate zero bytes. An initial test-tool compile failure from a
shadowed local was corrected; the raw failed attempt is retained separately.

Native fidelity keeps the known weapon.missile.splash tick177 difference,
victim.position4096.z expected52814/actual52815. Imperialist headshot F2:
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Raw commands, hashes, timings and exits: `artifacts/custom-weapons/CW-05m-world-pickups/`.

Separate placement research uses a copied CW-05l DLL and real native collision/
movement to qualify proposed additive positions. It is not M7 world-acquisition
acceptance for this build. This slice does not activate public pickups, remove
spawn grants, implement network baseline/delivery or change ordinary selection.
Those engine integrations and final device/release gates remain open. There are
no protocol changes. CI/review are neither awaited nor reported PASS; CW-05 and
the complete CW-16 objective remain incomplete.
