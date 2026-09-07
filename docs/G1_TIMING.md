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

## Second pass: world entities and gameplay camera

The next audited group names item-spawner delay/interval metadata (ushort casts retained), dropped-item lifetime 450→900 ticks, Morph Ball/Stinglarva bomb lifetime 43→86, Lockjaw lifetime 900→1800, and bomb shortening 22→44 with the original strict `>` comparison (the source comment about the original game's 22.5 remains). Jump-pad cooldown and trigger repeat/check delays retain their ushort source domains and original destination types.

Platform delay metadata, player-contact timeout 3→6, recoil 31→62 and added move wait 60→120 use the named conversion. `BeamInterval` is a uint field; its existing `(int)` conversion and unchecked multiplication may encode values outside the checked helper domain. That site deliberately uses `(int)data.BeamInterval * SimTicks.TicksPer30HzFrame`, preserving the full unsigned-domain behavior. Movement-derived timers, speed factors and fixed-point math remain untouched.

The dedicated force-field lock's 30-frame weapon delay is still byte 60. Door initialization's strict frame threshold remains >6. Shock Coil's escalation threshold remains 240 ticks and its below-threshold integer damage quotient remains `timer / 60`; the alternating-frame damage cadence is unchanged.

Gameplay camera switch metadata now uses the checked conversion while retaining ushort casts, timer reversal subtraction, comparison order, and floating-point interpolation denominators. Landing bob still performs integer `360 * timer / 18` before angle conversion. Camera collision delay remains a byte counter bounded at 30. FOV multiplications, smoothing factors, aim speeds and shake cadence are not duration conversions and remain unchanged.

### Second-pass float-second compatibility boundary (historical; superseded for nodes/beams below)

Node/Octolith objective state and projectile lifespan/decay fields still store float seconds and advance by the existing scene FrameTime. Converting their storage to integer ticks would alter accumulated rounding and potentially threshold frames; that requires a separately authorized behavior change. This pass names the 30 Hz denominator (`(float)SimTicks.LegacyHz`) and adds the compile-time reciprocal `SimTicks.LegacyFrameSeconds = 1f / LegacyHz` where the original code multiplied by a reciprocal. Original multiplication-versus-division forms remain separate because they can differ by an ULP. No timer clock or wire representation changes.

`WorldTimingTests` adds golden world durations, exhaustive ushort float conversion bits, camera reversal/interpolation across all hunter metadata and ushort timer values, unsigned platform interval edge cases, and complete ushort-domain Shock Coil/bomb boundary parity. Focused Release build and tests passed: **41 tests, 0 failed, 0 skipped** (30 first-pass plus 11 world/camera cases). Log: `/tmp/codex-re-prime-g1/world-timing-tests.log`. Scoped whitespace check passed.

## Third pass: remaining player, AI, HUD and audio durations

Player input/process/collision now name boost charge windows, alt-attack startup/cooldown/knockback, bomb refill/cooldown and Sylux overuse windows, jump-pad/grounding grace periods, idle/landing delays, Survival hiding/reveal counters, powerup 900→1800 tick lifetimes and their warning thresholds. Kanden's 13-frame segment cycle remains a compile-time 26-tick constant. The existing Survival authority guard and G2 healing/feedback hooks are preserved.

Noxus's `AltAttackStartup / 2 * 2` is deliberately `From30HzFrames(AltAttackStartup / 2)`: halving still happens first, preserving odd input truncation. Boost damage retains integer multiplication/division and its original ushort narrowing. Jump-pad lockTime retains ushort wrapping; its physics-derived float lockInc only names the final scale factor. GunIdleTime keeps its original cast to ulong before multiplication, including the full signed-source conversion behavior.

AI FramesUp/FramesDown, aggro expiration, timed context counters, deviation/shot delays, and legacy weapon charge thresholds now name the same durations. Random delay bounds change numerically neither the requested range nor the number/order of RNG calls. The uint bot-level tables and behavior-tree parameter thresholds use the compile-time factor to retain their original integer domain. The teleporter's AI wait remains 148→296 ticks. Geometric `2 * 2`, aim error ranges, smoothing and velocity factors remain unchanged.

Client HUD heal/pickup 10→20, damage flash 6→12, reticle 60→120 and disrupted-HUD 32→64 tick windows now use the helper. Both directional-indicator initialization paths use the same source-backed 63→126 duration. Existing legacy float-second HUD/audio expressions retain division by the named LegacyHz; landing audio retains float division by 180 ticks. Native-tick G2 feedback lifetimes, bitmap/mesh dimensions, FOV factors, color interpolation and blink bitmasks are unchanged.

`PlayerTimingTests` covers all hunter timing metadata and narrowing, complete ushort domains for Noxus rounding, boost damage, Survival hiding clamps and jump-pad casts; it also checks local RNG seed progression and AI-selected delay values across fixed seed sequences and every multiplayer weapon. Release integration build and focused timing suite passed: **47 tests, 0 failed, 0 skipped** (30 foundation, 11 world/camera, 6 player/AI cases). Log: `/tmp/codex-re-prime-g1/tail-timing-tests.log`. The earlier concurrent Client errors were resolved by their owners before this successful build; scoped whitespace validation passed.


## Fourth pass: authoritative node and projectile tick storage

The previous node/beam float-storage deferral is now replaced by integer simulation counters. This pass preserves the old binary32 arithmetic as immutable projections, rather than rounding all durations to their nominal seconds. Runtime timers advance integer tick indices only; float samples are derived compatibility values for interpolation, existing public getters and the unchanged node wire representation.

### Node capture and scoring

`NodeDefenseEntity` stores `_progressTicks` and `_scoreTicks`. Capture completes at tick600, the start cue sees20 accumulated ticks before the next increment, and contested occupancy pauses the same counter without losing progress. A 601-entry immutable projection reproduces every old repeated `+1/60f` progress value used for spin speed and WorldRecord.C. Replica progress remains a received presentation value and never authors capture/score timing.

| Owned node count | Old threshold seconds | Exact old crossing tick | New interval ticks |
|---|---:|---:|---:|
| 0 or1 | 5 | 300 | 300 |
| 2 | 3.5 | 211 | 211 |
| 3 | 2 | 121 | 121 |
| 4 | 0.5 | 30 | 30 |
| 5 or more | <= -1 | first evaluated tick | 1 |

The 3.5 and 2 second boundaries are deliberately one tick later than nominal multiplication. Capturing primes the score counter and awards its first point on the capture tick itself, preserving statement order. Later score thresholds can change with owned-node count without resetting accumulated ticks. Rotation/acceleration formulas and Defender's cumulative TeamTime metric are unchanged; they are not the mutable capture/score countdowns migrated here. The cosmetic blink pulse remains presentation state.

### Projectile lifespan, age and speed decay

`BeamProjectileEntity` stores integer `_remainingLifeTicks`, `_ageTicks`, and `_speedDecayEndAgeTicks`; the original ushort speed-decay metadata is retained as an authored interpolation parameter. Process decrements lifespan before collision-tail handling, advances motion age only for moving beams, and evaluates speed-decay eligibility against its integer endpoint. Existing float Age/Lifespan getters project the exact old values, including the terminal negative countdown residue, so consumers keep their prior comparisons and catch-up diagnostics. Projectile velocity, gravity, speed interpolation, damage fractions and collision ordering are unchanged.

`LegacyTickProjection` prepares immutable legacy elapsed samples and authored countdown samples once during beam-pool initialization. All 37 currently authored weapon records have equal min/full charged lifespans; therefore partial charge does not create an unbounded set of duration projections. All player, platform, ricochet and force-field weapon endpoint durations plus the four-frame collision tail are prepared. An explicit custom duration setter builds an uncached immutable projection; it does not add unbounded entries to a global cache. Non-finite durations or durations exceeding the ushort authored domain fail explicitly. The elapsed projection reserves 135166 ticks (ushort 30 Hz maximum plus 4096 rounding margin); the maximum authored-domain countdown is checked in tests. This allocation is initialization/configuration work, not recurring per-frame work.

The projection is necessary for physics parity: replacing accumulated Age with `ageTicks/60f` changes the binary32 ratio used by speed interpolation. Deriving the exact historical value from an integer index preserves trajectory bits while removing advancing float timer storage. Immutable float definitions/projections are not authoritative clocks.

### Validation scope

`AuthoritativeTimerTests` characterizes repeated-float score crossings, every authored lifespan/countdown tick and speed-decay ratio, the full elapsed-projection domain, custom/max-duration bounds, a real SANCTORUS node's 600-tick capture plus 13 contested paused ticks and capture/scoring order, actual charged Missile/VoltDriver/Omega 30-tick trajectories against a frozen float-age/interpolation oracle, collided-tail expiration/reuse and zero allocations for warmed authored projection access. The world/weapon baseline tests remain separate. Release integration build plus all four timing groups passed: **61 tests, 0 failed, 0 skipped** (47 prior +14 new), log `/tmp/codex-re-prime-g1/authoritative-timers.log`. A separate Game+Server-only build also passed all 14 new cases (`/tmp/codex-re-prime-g1/timer-isolated/tests.log`). Existing real-content catch-up and homing harnesses both passed, including historical collision/recycling and bit-exact trajectory comparisons: `/tmp/codex-re-prime-g1/timer-catchup.log` and `/tmp/codex-re-prime-g1/timer-homing.log`. The warmed projection test measured zero allocated bytes across 10000 lookups. These are focused/headless regression results, not a complete rendered match or device proof.
