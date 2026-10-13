# Competitive bot AI, Phase 5: Observational opponent adaptation

This slice is stacked on PR #484 and depends on the native Hunter combat
improvements and existing per-target, line-of-sight-gated memory.

## What bots can learn

Hard and Insane bots maintain small per-opponent statistics **only while the
opponent is visibly observed**: repeated lateral strafe reversals, how often
the player is airborne, and whether they frequently push directly toward
the bot. These are short-lived numeric summaries, not hidden position data,
global profiles or machine-learned advantages.

- Against frequent strafe reversals, the bot scales down projectile velocity
  leading slightly instead of overshooting a direction change that has not yet
  occurred. Close-range Shock Coil tracking gets a bounded preference.
- Against players who remain airborne, it slightly prefers precision and
  tracking weapons over slow ground-splash projectiles.
- Against observed rushers, long-range weapons prefer more distance, while
  close-range tracking weapons retain their natural engagement ranges.

A bot needs at least 24–30 visible observations before these adjustments are
active. All learned traits decay without sight and reset when the contact
expires or the bot's life resets. The model is not shared between bots.

## Behavior boundaries

All output goes through the already authoritative weapon scoring, projectile
interception and safe tactical movement. The learning model cannot change
weapon damage, network acknowledgement, physics, human controls, bot movement
limits or hit authority. It cannot read a hidden enemy's live position to make
a strategic decision.

The pure `--bot-ai` suite checks observed strafe, jump, and rush habits,
difficulty restrictions, and decay during loss of visibility. Performance,
actual win rate, and remote presentation require asset-backed matches.

## Validation

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bot-ai
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
```
