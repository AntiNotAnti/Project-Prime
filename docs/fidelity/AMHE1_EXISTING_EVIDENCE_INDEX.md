# Project Prime AMHE1 existing evidence index

This index records which existing Project Prime evidence was reused for F3-F8. It
does not upgrade Project-only results to AMHE1 runtime evidence.

| Phase | Current implementation evidence | Content-backed or integration evidence | Boundary |
|---|---|---|---|
| F3 simulation/movement/collision | `docs/G1_TIMING.md`, `docs/G1_SPAWNING.md`, deterministic fidelity runner and movement trace | main test suite; multiplayer content audit | No exact AMHE1 timer/movement locator or dynamic trial |
| F4 weapons/damage | `docs/NETWORK_WEAPON_POLICIES.md`, authoritative combat tests, fidelity projectile/direct-damage proofs | `nettest --weapon-policy` passes 61 cases / 18 variants through spawn | Expectations are Project Prime reviewed values without recorded E0-E2 field locators |
| F5 Hunters/alt forms | Hunter, alt-form, combat, death/reset, bot, observer, and replay tests | shared real-content main suite | No complete seven-Hunter AMHE1 dynamic capture matrix |
| F6 pickups/maps/spawn/objectives/modes | `docs/G1_SPAWNING.md`, `docs/G3_OVERTIME.md`, `docs/G3_WORLD_EVENTS.md`; fidelity pickup/node proofs | 26 scoped room entity files parse without errors; match baseline and overtime pass all 12 modes | Entity inventory and Project behavior do not prove dynamic AMHE1 parity |
| F7 presentation | `docs/G2_AUDIO.md`, `docs/G2_FEEDBACK.md`, `docs/G2_RADAR.md`, camera/presentation/imaging tests | local rendered and controlled-loopback records where already documented | No frozen AMHE1 captures; physical desktop/controller/audio/high-refresh/Android waived |
| F8 network/replay/bots | `docs/NETWORK_MODERNIZATION.md`, `docs/G5_REPLAY.md`, `docs/G5_BOTS.md`, `docs/G1_G5_VALIDATION.md` | localhost and controlled process-local impairment suites | Modern ownership/transport/replay/bots are intentional deviations; external WAN/soak waived |

## Reproducible content-backed probes

Run these against the frozen AMHE1 tree without changing it:

```bash
dotnet tools/nettest/bin/Release/net10.0/nettest.dll \
  --audit-multiplayer /absolute/path/to/AMHE1 \
  /tmp/amhe1-multiplayer-audit.json - maps

dotnet tools/nettest/bin/Release/net10.0/nettest.dll \
  --match-baseline /absolute/path/to/AMHE1 AMHE1

dotnet tools/nettest/bin/Release/net10.0/nettest.dll \
  --overtime-check /absolute/path/to/AMHE1 AMHE1

dotnet tools/nettest/bin/Release/net10.0/nettest.dll \
  --weapon-policy /absolute/path/to/AMHE1 AMHE1
```

The full content audit returns nonzero when optional First Hunt data and catalog
entries outside IDs 93-118 are absent. That aggregate result is not a failure of the
AMHE1 fidelity scope: all 26 required retail room entity files (IDs 93-118) parse as
format 2 with 16 layers and no errors. The frozen audit observed 1,284 entities across
27 available retail files overall; the matrix records the 26 in-scope inventories.

## Interpretation rule

“Pass” in this index means the named Project Prime behavior or content parser passed.
Only an exact AMHE1 locator (E0-E2) or repeatable AMHE1 trial (E1) can support a retail
parity conclusion or authorize a Classic gameplay correction. No such discrepancy
was established during this audit, so F3-F7 made no gameplay balance or physics
changes.
