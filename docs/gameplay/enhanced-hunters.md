# Enhanced Hunters

Enhanced Hunters is an optional match rule, OFF by default. It is independent
of Affinity Weapons pickup replacement and excludes Guardian. Enable it in the
lobby, offline match settings, console match settings, or with the dedicated
server `-enhancedhunters` switch.

## Implementation

The seven hunter modules live in `src/MphRead/Mods/EnhancedHunters`. Accepted
authority damage drives marks, stacks, secondary damage and status. The shared
bonus-damage scope prevents recursive procs and duplicate hit-claim credit.
Spawn protection and team rules gate rewards.

- Samus: three lock pips, charged missile children and reduced rocket-jump damage.
- Kanden: Lightning Rod, direct-hit Overload, bounded chains and Stinglarva consume.
- Trace: normal/perfect marks, one boosted lunge per mark, cloak acceleration and Ghost Step.
- Sylux: continuous-contact tether, overcharge, release burst, bounded movement and alt carryover.
- Noxus: per-target Frost, Brittle, Shatter, Cryo Launch, Flash Freeze and floor ice patches.
- Spire: capped ricochets, floor pools, detonation, volcanic launch and Dialanche momentum.
- Weavel: direct-hit Siege Charge, turret rounds, overclock cadence, scythe targeting and per-target Crossfire.

HUD pips, target brackets, numeric meters, audio cues, temporary floor surfaces
and colored world indicators use the existing rendering and HUD infrastructure.
Bot decisions recognize the new state. Spire bots permit self-detonation only when a bounded ballistic probe predicts
non-hazard floor near a navigation node.

### Network and replay

The supplied plan referred to protocol 24 and rule bit 1024. This checkout was
already protocol 28, with bits 1024–4096 occupied. The implementation therefore
uses **protocol 29 and rule bit 8192**.

`MatchState` appends two rule bytes. Each player appends 26 enhanced bytes:
10 state/target bytes plus two sequence-tagged owner impulse events. Targets
carry both life and slot generation. Maximum fast datagram size is **1,151
bytes**. The assembled in-memory snapshot has a separate 4,096-byte limit;
fast and world datagrams remain bounded by 1,200 bytes.

Temporary zones are scene-owned, capped at 16 overall and two per owner/type.
World changes publish immediately. Historical protocols 24–28 upgrade at replay
boundaries with Enhanced Hunters disabled. Checkpoint schemas include runtime
state, per-target Frost, cooldowns, projectile metadata and zones.

Local movement responds to confirmed replicated rule state; volcanic/release
impulses can predict locally and consume matching sequenced confirmations.
No local position reconciliation was introduced.

### Telemetry

Counters emit through the existing production telemetry configuration, with a
bounded `enhancedHunters` summary dictionary. Keys are `hunter:counterId`, where
counterId is FNV-1a of the event name in `EnhancedHunterTelemetry.Event` calls.
Totals such as pips-at-fire, charge-at-transform and tether-frames support
averages; enabled-frames supplies the denominator for rates. Telemetry never
changes gameplay.

## Validation recorded for this change

- Enhanced state/rule/packet checks: 37 passed.
- Eight-player authority scene checks: 31 passed.
- Combat scene: 99 passed.
- Protocol/combat telemetry: 143 passed.
- Advanced match rules: 105 passed.
- Lifecycle: 3,688 passed.
- Lobby/load/rotation: 6,829 passed.
- Replay format: 2,712 passed.
- Architecture, protocol 17/18, health/shot, continuous-target, alt-hit,
  reliability, queue, weapon-policy, lag-compensation and claim stress passed.
- Eight mixed-quality UDP peers: passed, including 128 exactly-once controls,
  packet impairment and a 400 ms client pump stall.
- Linux x64 and Windows x64 publish builds passed in the isolated worktree.

Run the focused checks using:

```sh
dotnet run --project tools/nettest -- --enhanced-hunters
dotnet run --project tools/nettest -- --enhanced-scene /path/to/data-and-paths-txt
```

## Remaining acceptance work

The Android build was attempted but stopped with XA5300 because the Android
SDK directory is unavailable; the .NET workload is installed. It needs a valid
SDK path before this platform can be signed off.

The transport impairment suite validates the shared networking path. It does
not replace a complete seven-hunter end-to-end matrix at every requested RTT,
or hands-on replay/killcam, ultrawide/custom-HUD and visual/audio review.
Overclock uses the existing instantaneous aim/acquisition implementation, so
there is no finite tracking speed to multiply by 1.25. Its fire cadence is
increased. The conservative Spire landing predictor still needs hands-on navigation review.
These are explicit acceptance gaps rather than completed tests.
