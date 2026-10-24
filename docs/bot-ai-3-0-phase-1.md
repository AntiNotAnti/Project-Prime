# Bot AI 3.0, first implementation slice

## Changes
- Persist strategic goals through hidden/distant enemies, while permitting
  defensive fire on the move and interrupting for immediately visible threats.
- Plan biped strafes and retreats in enemy-relative world space, then
  translate them to exactly one legal local movement request.
- Follow safe walking segments of the existing directed room route table
  toward distant objectives and pickups. The original personality AI remains
  responsible for advanced aerial, alt-form and special transitions.
- Reduce the long-distance objective penalty so large arenas are playable.
- Improve Normal, Hard and Insane aim lead, shot alignment and targeting
  noise. Easy retains its original forgiving accuracy.

## Asset-free regression suite

```sh
dotnet run --project tools/nettest -c Release -- --bot-ai
```

Before merging, run native asset-backed simulations and dedicated-server
matches. Check stock and custom maps, LOS loss, distant nodes, one-way drops,
all Hunters, objective play, 7-bot CPU usage, and measured hits/shots.
These static checks do not demonstrate a higher win rate.

## Next slices
- Traversal executors for jumps, morph corridors, teleports and vertical paths
- Longer-horizon utility goals, route caching and explicit stuck-route avoidance
- Predictive combat positioning, weapon expected-damage policy and squad tasks
- Bot debug overlay and reproducible difficulty/accuracy benchmark campaign


## Competitive AI acceptance and skill benchmark

The original policy suite is still `--bot-ai`. Run the real asset-backed
authority benchmark in **fresh processes** for each difficulty, all seven
Hunters, and each match mode. Use identical room, roster size and simulation
length when comparing results:

```sh
dotnet run --project tools/nettest -c Release -- \
  --bot-skill-eval "/path/to/AMHE1" "MP1 SANCTORUS" 3 7 3600 \
  "/tmp/bot-insane.json" Battle

dotnet run --project tools/nettest -c Release -- \
  --bot-skill-eval "/path/to/AMHE1" "MP1 SANCTORUS" 2 7 3600 \
  "/tmp/bot-hard.json" Battle
```

Supported modes: Battle, BattleTeams, NodesTeams, HardpointTeams and
DefenderTeams. Place the output on a writable existing directory; JSON paths
must not already exist. Results contain per-Hunter fire/hit ratios, damage,
kills/deaths, objective time, travel, frame-time percentiles and allocations.
Repeat each scenario multiple times; RNG seeds may differ. No target accuracy
or skill claim is justified by headless unit tests alone.

### Release gates

- All cross-platform builds and asset-free `--bot-ai` regressions pass
- Offline and dedicated 7-bot full matches with asset data complete cleanly
- Bots can reach multi-floor goals without impossible traversal or wall attacks
- Team objective score, travel time, and stuck intervals improve in paired trials
- Real fire/hit ratio is measured by difficulty and Hunter without damage cheats
- Worst-step/p99 and allocation regressions are measured, not inferred
