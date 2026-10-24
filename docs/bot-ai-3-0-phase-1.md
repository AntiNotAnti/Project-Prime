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
