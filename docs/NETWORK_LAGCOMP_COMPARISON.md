# Deterministic lag-compensation comparison

`tools/run-lagcomp-comparison.py` runs identical scripted scenarios with lag
compensation enabled against two baselines: fully disabled, and historical
traces enabled with projectile catch-up disabled. Each run creates a real headless multiplayer
scene from locally extracted game data. One subprocess owns each scene because
the game has process-global player and collision state.

This is controlled simulation evidence. It does not measure real UDP delivery,
WAN conditions, rendered aiming, or competitive balance. The separate multiplayer
and performance harnesses exercise those other paths.

## Reproduce

From the repository root with .NET 10 and Python 3 available:

```sh
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true \
  --artifacts-path /tmp/project-prime-comparison-build
python3 -m unittest discover -s tools/tests -p test_lagcomp_comparison.py
python3 tools/run-lagcomp-comparison.py \
  --dotnet dotnet \
  --harness /tmp/project-prime-comparison-build/bin/nettest/release/nettest.dll \
  --data "$GAME_DATA_DIRECTORY" --version AMHE1 \
  --output /tmp/project-prime-comparison-results
```

Use a new output directory for each invocation. The default matrix covers five
weapon scenarios at delivery delays of 0, 4 and 8 simulation ticks, compared
against both baselines. Each run lasts
1,200 ticks, uses seed 661336, includes up to two ticks of deterministic jitter,
and drops 3% of generated bundles. `--scenario`, `--delay`, `--ticks` and `--seed`
select smaller reproductions. `--baseline off` or `--baseline trace-only` limits
the baseline selection. `--repeat-on` verifies two identical enabled runs;
its output is labeled repeatability evidence and is not an ON/OFF comparison.

The underlying command is:

```text
nettest --lagcomp-script DATA CONFIG_JSON OUTPUT_JSON
```

Its JSON configuration accepts `Scenario`, `Enabled`, `ProjectileCatchUpEnabled`, `Seed`, `Ticks`,
`DelayTicks`, `JitterTicks`, `LossPerThousand`, `Room` and `Version`. See the
runner's generated `.config.json` files for concrete inputs.

## Controlled workload

The fixture loads `MP1 SANCTORUS`, gives the shooter its scenario weapon once,
and uses the normal player shooting and charging inputs. Both runs use connection
identities 100 and 200 and the same initial scene RNG values. The fixture never
resets scene RNG after initialization to conceal diverging shot seeds.

Input bundles use the production codec and `ServerInputStream`. Their delivery
ticks, loss decisions and ordering are predetermined; no UDP socket participates.
Input view timestamps describe the scripted target six ticks before sampling.
Each accepted command and every root shot is recorded, including its command,
tick, weapon, charge, affinity, identity, position, direction and spread seed.

The shooter stays at a fixed grounded position. The target follows a prescribed
2.4-unit lateral path in a collision-checked lane eight units away. Before each
tick the fixture restores both actors' health to 10,000 and resets velocity. This
prevents death, respawn and earlier knockback from changing the subsequent input
schedule. Actual damage still uses the normal gameplay resolver. Target positions
after each completed frame are also compared. These controls deliberately limit
what the raw damage counts mean.

| Scenario | Normal weapon input | Intended policy coverage |
| --- | --- | --- |
| `trace` | Imperialist, Trace | Historical trace |
| `travel` | Uncharged Power Beam, Samus | Ordinary projectile catch-up |
| `homing` | Charged Missile, Samus | Historical acquisition and steering |
| `continuous` | Shock Coil, Sylux | Continuous-beam exclusion |
| `area` | Charged affinity Judicator, Noxus | Angular-area exclusion |

Charging also produces normal uncharged precursor shots. Their outcomes can
change when ordinary projectile catch-up is enabled. Excluded-outcome checks
therefore select charged root commands for the area scenario, and all root
commands for the continuous scenario. Homing is eligible for historical catch-up.
Every precursor remains included
in the strict shot-schedule and spread-seed comparison.

## Acceptance and outputs

The runner rejects differences in configuration, actual policy options, encoded
input delivery, accepted input, root-shot count or records, and the controlled
target trajectory. An empty shot schedule or an overflowing combat/catch-up queue
also fails. It does not normalize hit rates to compensate for unequal workloads.
Eligible shots may intentionally produce different damage. The trace-only baseline
must preserve historical-trace outcomes. Excluded root shots
must retain identical resolved damage facts.

Each case retains its configuration, process log and full JSON output. A complete
passing matrix additionally writes `comparison.json` with raw shot counts,
damage-event counts, total damage and script hashes. A failing matrix exits
nonzero and retains the raw cases for diagnosis without writing a passing summary.

A preliminary Judicator run detected identical accepted commands and nine root
shots but different spread seeds on three later charged shots after earlier
impacts resolved differently. A match-owned root-shot RNG now selects spread
seeds independently of damage/effect randomness. The ordinary gameplay RNG still
advances at the original call site, and pellets/children retain their root seed.
With that correction, all 30 paired cases passed: five scenarios, three delivery
delays and both OFF/trace-only baselines. The comparator keeps the seed-equality
gate, so future regressions cannot be hidden by normalizing hit counts.
