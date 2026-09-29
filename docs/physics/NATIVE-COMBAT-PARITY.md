# Native combat and projectile evidence

The diagnostic native-cadence mode passes four original-ROM Power Beam fixtures on macOS, with commands delivered on either half of the 60 Hz scene. This is scoped acceptance for Power Beam firing, prescribed freeze recovery, and static-wall projectile motion. Other weapons, damage delivery, multiplayer combat fairness, bombs, turrets and AI remain unverified.

## Matched scenarios

| Fixture | Observed behavior |
|---|---|
| `power-single` | One press and release, flight, wall contact and expiry |
| `power-repeat` | Repeated button edges and cooldown-limited shots |
| `power-charge` | Held input, automatic fire, full charge and release |
| `freeze-release` | Six prescribed frozen native ticks, suppressed firing, movement recovery and a fresh fire edge |

Each scenario runs 50 native ticks. The shared script drives both the original AMHE1 ROM and Project Prime. Frozen state is prescribed at setup; this does not prove that a weapon applies freezing correctly. Power Beam uses no ammunition, so its different native/Prime backing counters do not establish ammo-cost parity.

`artifacts/fps-audit/followup/native-fire-final-phase0/` and `native-fire-final-phase1/` contain complete captures, provenance and strict comparisons. All eight runs pass movement, firing/charge/freeze timelines and projectile comparisons. Each phase compares 174 live projectile observations. The maximum position difference is below 0.000000629; the unchanged tolerance is 0.0001. Pool identity, weapon and flags match exactly. Age and lifespan comparisons use seconds.

## Corrections

Native Power Beam firing consumes buffered edges at the same native boundary as movement. Charge and automatic-fire counters advance by two 60 Hz units per native operation. The v4 replay capsule's version-2 cadence appendix records independent jump and fire edges; version-1 appendices restore fire edges as false. Unknown bits and malformed appendices fail closed.

Launch position and velocity use native fixed-point rounding. Projectile updates preserve the original order and whole native step, including integer lifetime and age. Static-wall intersection and impact offsets use native plane arithmetic. The diagnostic path is deliberately restricted to Power Beam; dynamic-entity collisions have no native acceptance yet.

Moving launch fixtures also require bob updates at the native boundary and the previous native camera position when constructing aim. The held 60 Hz camera update otherwise feeds presentation state into the next native aim operation. Actual camera presentation can continue at 60 Hz.

Projectile draw interpolation uses the prior and current authoritative positions, without changing flight state. Collided projectiles retain their impact endpoint. Checkpoint continuation verifies projectile state and draw poses across both input phases, including a buffered fire edge. A headless firing crash was fixed by allowing the absent HUD target-circle instance while retaining gameplay timers.

## Reproduce

Run `tools/fps-scenarios/run_corpus.py` with the same bridge, ROM, state, Prime and dotnet arguments as the biped corpus, plus:

```sh
--scenarios tools/fps-scenarios/combat --native-cadence60 --input-phase 0
```

Repeat with `--input-phase 1`, using a new output directory. `check_combat.py` and `check_projectiles.py` reject missing or malformed observations; their negative tests are run by `python3 -m unittest discover -s tools/fps-scenarios -p 'test_*.py'`.

The original-ROM executable, states, raw RAM and extracted game data remain private ignored artifacts. Runtime acceptance on other platforms is deferred by the user.
