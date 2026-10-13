# Competitive bot AI phase 4: Hunter-specific combat abilities

**Scope:** server-authoritative multiplayer Battle and Balanced Mode bots at
Hard/Insane difficulty and below, sharing the ordinary native combat rules.
This slice does not modify Enhanced Hunters' existing separate bot policy,
single-player story encounter AI, gun damage, network protocol, or replay schema.

## Finite combat-state decisions

The Phase 4 policy makes tactical choices for each playable Hunter while
retaining native control of physics, animation, cooldown, projectile creation
and hit resolution. It is called after the legacy personality and tactical
layers have made their decisions. Every action is emitted as the same input a
human would use.

- **Samus:** use Morph Ball defensively under close pressure, place native
  bombs if a relevant alt opportunity appears, and charge/release a short
  native boost when the path and facing are safe. Prefer charged missiles at
  useful medium range.
- **Kanden:** deploy Stinglarva bombs in close combat, use charged Volt
  Driver disruption at medium/long range.
- **Trace:** keep Imperialist zoomed in long-range sighted duels; in very
  close encounters, switch briefly to Triskelion and use its native lunge.
- **Sylux:** maintain uninterrupted Shock Coil fire while aligned at close
  range and deploy Lockjaw bombs at very close range.
- **Noxus:** prefer Judicator freezing at midrange; spin with Vhoscythe only
  if a nearby opponent is genuinely frozen and visible.
- **Spire:** prefer charged Magmaul in its effective range, use Dialanche
  slam at close range with a safe approach path.
- **Weavel:** use Halfturret as a temporary defensive stance while wounded
  or contesting nearby objectives, letting the native turret do the shooting;
  use its native close-range lunge when safe.

Bots commit to alt combat for finite windows, then attempt to return to
biped combat. Morph attempts have cooldowns. The policy only manages forms it
deliberately entered; native alt forms needed for map navigation remain the
responsibility of the original MPH navigation tree. Objective carrying and
special game modes retain their existing native behavior.

The unfinished legacy touch-gesture boost simulation is disabled *for
multiplayer bots*; Samus uses the same Boost button hold/release available to
human players. Single-player scripted hunter encounters are unchanged.

## Fairness and regression requirements

No abilities are activated without a visible legitimate opponent. The
same standard weapon charge limits, cooldowns, physical collision, alt attack
gates, energy/ammo and network authority apply to bots and humans. The pure
bot contract suite checks each Hunter's action policy and the no-blind-combat
constraints.

Required asset-backed acceptance:
- Seven Hunters across normal/Balanced/Enhanced rule sets
- Enter/recover alt form under wall and low-ceiling conditions
- Samus charge-release and Sylux/Kanden bomb network correctness
- Frozen Noxus ability timing; Trace/Weavel lunges; Spire slam
- Dedicated multiplayer bots, remote observer presentation, replay seek
- Seven-bot CPU allocations and human difficulty testing

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bot-ai
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-online /path/to/game-config 'MP1 SANCTORUS'
```
