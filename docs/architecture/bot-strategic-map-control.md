# Competitive Bot AI, Phase 3: Map Control and Team Roles

This is an incremental enhancement **stacked on Phase 2**, not a replacement
for Project Prime's native MPH node graph. Live bots remain owner-simulated by
the dedicated authority. Replay replicas and clients never make strategic
decisions, and this change introduces no new network messages.

## Strategic map control

Hard and Insane bots now opportunistically pursue *spawned, reachable*
health, ammunition, weapons, cloak, Double Damage and Omega Cannon pickups.
A missing item is not treated as a visible pickup. Pickups are evaluated by
health deficit, ammo reserve, weapon ownership, and nearby distance.

For Nodes Teams, Defender Teams and Hardpoint Teams, bots evaluate locally
reachable scoring nodes using authority-observed node ownership, contest state,
hostile occupation, teammate proximity, and other bots already committed to
that destination. Hardpoint bots only evaluate the current active point.

Team bots acquire deterministic roles derived from active teammate slot order:
Anchor, Vanguard, and Flanker. An Anchor prefers to defend an owned node,
Vanguards favor capturing/contesting, and Flankers avoid crowded owned zones.
Current team role also influences the priority assigned to *visible* enemy
objective carriers and recent attackers. No opponent location is shared or
fetched through walls.

In Capture and team Bounty, a bot without an immediate enemy can support a
nearby friendly flag carrier, and a second escort is discouraged.

## Safety / routing

All strategic goals are local (within nine game units), nearly same-height,
and require a safe destination floor and unobstructed direct line to the goal.
The existing tactical collision, headroom, footprint and hazard probes must
approve motion before the common Bot Motor emits analog input. Otherwise
**the native MPH navigation tree remains authoritative** for longer or more
complex routes, including jump pads, one-way drops, morph corridors, doors,
teleporters and moving platforms.

A strategic choice lasts at most 32–42 simulation ticks before reevaluation.
A pickup that despawns or a Hardpoint that changes invalidates its goal.
Strategic goals never replace an existing immediate combat target, flag/token
carrier path, or critical native health-pickup route.

## Validation

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bot-ai
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-online /path/to/game-config 'MP1 SANCTORUS'
```

The headless contract suite asserts objective roles, contested priorities,
claim spreading, injury-based pickup valuation and unsupported pickup rejection.
It does not prove in-match win rate, multi-floor routing, client presentation or
replay. Those must be validated with real game data and runtime traces.

## Follow-up

Build a true multi-hop strategic graph planner on top of directed native nodes,
then evaluate long-distance weapon-control rotations. Follow with native ability
timing per Hunter and team response to visible calls for assistance. Avoid
general global pursuit using hidden player coordinates.
