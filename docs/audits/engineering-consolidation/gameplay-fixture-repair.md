# Gameplay fixture repair and invocation supersession

The combat and full Balanced scene failures reproduced on pristine main
`91b1c0c6c44e8c90effd250729709f791a84765e` and integrated
`164990e60244ecdb21fdcc4e98e91f3ea097090b` are test setup defects. This repair changes
only `NetCombatCheck` and `AdvancedMatchRulesCheck` fixture preparation; damage,
physics, affinity, lifecycle, telemetry and replay production behavior are unchanged.

## Source diagnosis and controlled evidence

`NetCombatCheck.Run` initially settles two players for 120 frames. Its preceding
recovered-shot/history subtests then leave the shooter at Y32.341797, far above the
room floor Y4.341797. Restoring HP99 alone allows gravity during the held-input
section: at frame288 unattributed damage (beam255, attacker255) subtracts26 HP,
and at frame303 a75-point fall receipt kills the shooter during Magmaul. The
successful-shot counter stays unchanged. Clearing retained projectiles in a
diagnostic arm still fails at the same point, ruling those out as the cause.

The repair saves the original settled position, facing and node before those
subtests, then restores that pose through the real `PlayerEntity.Spawn` method
before the independent held-input section. Retained flights remain intact.
Every original assertion, including subsequent old-launch damage, shooter
respawn, forged life rejection and ricochet identity, passes on both clients.

`CheckBalancedAffinityScene` constructs synthetic disrupt, freeze and burn beams
without `ModLaunchKey`. `NetDamage.Suppress` rejects a projectile for which
`NetPlayerLifecycle.CurrentProjectile` is false. On both clients the Kanden source
initially produces HP199/timer0; the same source stamped using production
`NetPlayerLifecycle.StampProjectile` produces HP198/timer75, the intended 1.25s.
The fixture now stamps all three sources and captures `ModOwnerHunter`, mirroring
metadata assigned by production `BeamProjectileEntity.Spawn`. Three new assertions
prove each source has a valid current launch identity before applying damage.
`EnhancedDirectHit` remains unchanged and still distinguishes direct from splash.

After restoring those sources, both builds expose a later telemetry setup
assumption: room construction/activation has already recorded six picks
`1,1,1,0,1,1,1` before the fixture explicitly spawns seven Hunters. Its aggregate
then has 13 picks, while revision 3 and metric length 6 are correct. The fixture now
starts a fresh telemetry observation window immediately before its explicit
seven-Hunter spawn/status phase. The original sum 7 requirement remains, strengthened
to exactly one pick per Hunter. Status applications, rejected chains, burn,
Battlehammer concussion and stock behavior assertions remain unchanged.
This does not claim to validate telemetry counting during initial room construction.

## Validation

SDK 10.0.401, macOS arm64, device-free. User-data directories were newly created
and seeded only with `paths.txt` pointing to locally installed assets. No user
preferences were modified. All runs set `PROJECT_PRIME_USER_DATA` to the isolated
directory and `PROJECT_PRIME_MAP_COMMUNITY=https://maps.example.test/`.

| Check | Result |
|---|---|
| Focused fixture branch Release nettest build | PASS, 0 errors / 31 existing warnings |
| Actual focused branch `--combat-scene <paths-directory>` | PASS 204 |
| Actual focused branch `--advanced-rules-scene <paths-directory>` | PASS 347 |
| Repaired fixtures against immutable pristine baseline library | Combat PASS 204, Balanced scene PASS 347 |
| Repaired fixtures against immutable integrated library | Combat PASS 204, Balanced scene PASS 347 |
| Actual focused branch `--advanced-rules` | PASS 130 |
| Protocol18 | PASS, 1,172B ceiling, zero decode allocations |
| Replay format / game-mode lifecycle | PASS 3034 / PASS 2198 |
| Headless asset mechanics | PASS406 (main still has live Enhanced mechanics) |
| Native GL/audio, physical platforms, rendered historical matrix | NOT_RUN |

The comparison harness references the already-built baseline client MVID
`bb5922d1-051d-4a89-940b-d14096c550fe` and already-built integrated library.
It compiles the repaired fixture methods as a temporary friend assembly; it does
not rebuild or change either production library. The integrated combat fixture
retains the retirement PR's one native Missile/no-barrage expectations, while
pristine baseline retains its original historical live expectations. Applying
this small setup patch to the integrated source is still checked separately by
the integration owner. No retail assets, binary archive or generated capsule are
committed.

## Correct registered commands

The initial integration commands `--enhanced-retirement-replay` and `--advanced`
are unregistered. `--enhanced-retirement-scene <paths-directory>` omitted its
required fixture-directory argument. Each fell through to hostname handling and
terminated with DNS errors (exit -6). Those are **INVALID_INVOCATION**, not runtime
regressions. Preserve their original logs and supersede their classifications.
Correct commands for the source containing retirement PR #498 are:

```sh
# Set both environment variables above, then use a fresh isolated paths directory.
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --enhanced-retirement
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --enhanced-retirement-scene /absolute/isolated-paths-directory /absolute/frozen-fixture-directory
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --enhanced-scene /absolute/isolated-paths-directory
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --advanced-rules
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --advanced-rules-scene /absolute/isolated-paths-directory
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --combat-scene /absolute/isolated-paths-directory
```

The correctly registered retirement, archive, live scene and advanced checks
already pass 80/46/20/130 on integrated 164990e6 before this fixture repair. Replay
validation reads the frozen pre-change fixture; do not regenerate it on candidate.
No CLI parsing or help behavior is changed by this PR.

## Evidence preservation

[The durable evidence manifest](gameplay-fixture-evidence.json) records exact
commands, library/log hashes, comparison results and explicit supersession.
Original logs remain under the integrated worktree's
`artifacts/integration-checks/`; corrected invocation, diagnostic control arms and
all repaired-library runs remain under
`artifacts/integration-checks-corrected-20261013/`. Focused branch runs are under
`artifacts/gameplay-fixture-repair/`. Earlier temporary probe iterations also
remain: the first diagnostic script dereferenced inactive players' null beam
arrays, and one initial transplanted baseline combat fixture retained live
Enhanced expectations against the retired integrated runtime. These instrumentation
iterations were corrected in later named logs, not treated as product failures.
Original valid combat/Kanden failures and telemetry observations remain available.
The synthetic capsule and its three original hashes remain unchanged.
