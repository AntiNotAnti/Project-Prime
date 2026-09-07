# G1.1 timing normalization: first pass

The simulation remains fixed at 60 Hz. This pass names existing integer duration conversions; it does not change counter storage, decrement ordering, strict/inclusive comparisons, spawn selection, protection cancellation, wire fields, or replay clocks.

## Conversion contract

`src/Game/Simulation/SimTicks.cs` defines compile-time `Hz = 60`, `LegacyHz = 30`, and `TicksPer30HzFrame = 2`. `FromSeconds(int)` and `From30HzFrames(int)` use checked integer multiplication. Signed offsets are supported. Inputs whose converted result exceeds `int` fail with `OverflowException`.

`FromMilliseconds(int)` multiplies through `long`, divides by 1000, and truncates fractional ticks toward zero. No production call site uses this method in this pass; it does not introduce a rounding change to an existing timer.

Existing explicit `ushort` casts remain at metadata assignment sites. The helper does not saturate or narrow values. Exhaustive tests over the entire `ushort` source domain verify both the intermediate doubled integer and the original unchecked narrowing result. All currently migrated metadata values fit the checked intermediate operation.

## Converted expressions and original values

The original expressions below were inspected at baseline `b31bc5764b01da0d8dac8b1f261b11e2d791e312`. Locations name symbols rather than unstable line numbers.

| Source / timer | Original expression | New expression / exact tick value |
| --- | --- | --- |
| `PlayerEntity.RespawnTime` | `90 * 2` | `3 * SimTicks.Hz`, 180 |
| `PlayerEntity.Spawn`, spawn protection | `(ushort)(Values.SpawnInvulnerability * 2)` | cast of `From30HzFrames`; metadata 30 becomes 60 |
| `PlayerEntity.TakeDamage`, damage protection | `(ushort)(Values.DamageInvuln * 2)` | cast of `From30HzFrames`, same per-hunter value |
| `PlayerProcess.GetRespawnPoint`, selected point cooldown | `2 * 2` | `2 * TicksPer30HzFrame`, 4 |
| `GetTimeUntilRespawn`, automatic wait | `900/600/300 * 2` | 1800/1200/600, then original subtraction |
| `GetTimeUntilRespawn`, unspawned Survival | `210 * 2` | 420, then original subtraction |
| Respawn display threshold | `time < 150 * 2` | strict comparison against 300 |
| Respawn displayed seconds | `(time + 30 * 2) / (30 * 2)` | `(time + Hz) / Hz`, same integer division |
| Freeze repeat gap | `_timeSinceFrozen > 60 * 2` | strict comparison against 120 |
| Freeze long/short duration, graphic tail | `75 * 2`, `15 * 2`, `+ 5 * 2` | 150, 30, +10 |
| Disruption and burn duration | `60 * 2`, `150 * 2` | 120, 300 |
| Burn pulse after decrement | `_burnTimer % (8 * 2) == 0` | modulo 16, same location after decrement |
| Charge thresholds in player input/process/animation and projectile creation | `MinCharge * 2`, `FullCharge * 2` | `From30HzFrames` of the same metadata |
| Shot and autofire cooldown | metadata `* 2` | `From30HzFrames`, retaining assignment cast |
| Power Beam increasing autofire cooldown | `(ushort)((pbAuto + AutofireCooldown) * 2)` | convert the same sum, then same cast |
| Shoot press charge grace | `ChargeLevel <= 1 * 2` | inclusive comparison against 2 |

The Power Beam calculation still divides `_powerBeamAutofire` by 2, clamps to 90, and executes `(int)(pbAuto * 15 / 90f)` before the final conversion. Projectile partial-charge interpolation retains the original integer subtraction and floating-point division order, including zero-denominator behavior. Respawn display retains its existing extra second at exact positive multiples; this is not replaced with ceiling division.

Normal spawning occurs before the protection decrement in `ProcessPlayer`, so a newly assigned 60 becomes 59 in that pass. Server activation assigns 60 outside that pass. Both orderings remain intact. The bot protection refresh of 2 is already expressed in native simulation ticks and is unchanged.

## Scope deferred

SpawnDirector, Enhanced/Duel policies, attack cancellation of spawn protection, and spawn fallback changes are outside this pass. So are protocol/network fields, match rules, client presentation timers, CPU animation cadence, beam lifetime floats, movement/acceleration math, smoke/cost scaling, boost/alt-attack timers, and other unaudited legacy arithmetic. Remaining `* 2` expressions are not evidence of a missed duration migration; many encode different units or deliberate rounding.

## Verification

`tests/Tests/Game/SimTicksTests.cs` covers conversion constants, overflow boundaries, signed millisecond truncation, the full ushort conversion domain, every hunter protection value, respawn display boundaries, burn/freeze boundaries, weapon metadata charge comparisons and bitwise float fractions, and exhaustive Power Beam autofire counter values for each metadata cooldown.

Focused Release test run: **30 passed, 0 failed, 0 skipped**. The build compiled Game, Client, Server, Tools and Tests. Existing `NU1903` warnings for `Tmds.DBus.Protocol` 0.21.2 remain baseline warnings. Full main Release suite against the same built artifacts: **464 passed, 0 failed, 0 skipped** (434 baseline cases plus 30 new cases), duration 1 minute 38 seconds. Scoped `git diff --check` passed.

Artifacts: `/tmp/codex-re-prime-g1/timing-tests.log` and `/tmp/codex-re-prime-g1/timing-main-tests.log`. This is source and unit-test parity evidence, not live multiplayer or device proof.
