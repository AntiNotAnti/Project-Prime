# Projectile presentation prediction measurement

This document records the QZ5-A measurement slice. It does not enable
projectile adoption, client-side collision, damage prediction, or a new wire
protocol.

## Current path and scope

The authoritative client has two existing presentation paths:

1. The local game simulation creates a predicted `BeamProjectileEntity`. The
   existing `ISceneServices.NoteFired` hook runs immediately after that spawn.
2. `AuthoritativePlay.DrainEvents` receives the reliable `Shot` presentation
   fact. `PresentationPlayerEntityCombat` suppresses the echoed local visual
   with `predictedLocalShot`; remote authoritative shots still create their
   normal presentation visual.

The current `Shot` fact contains spawn position/direction, command sequence,
and `(slot, connection, life)` actor identity. It does **not** contain a
continuous authoritative projectile transform/trajectory stream. The
measurement therefore compares spawn facts only; it cannot prove travel-path
snapping or rendered correction quality.

QZ5-A observes local age-zero beams through the existing client-only hook and
copies immutable position, direction, weapon, and bounded visual-count facts.
It does not retain the local object as authority. Authoritative matching uses:

```text
match presentation epoch
+ CombatActor(slot, connection, life)
+ input command sequence
+ deterministic shot ordinal
```

The ordinal is normally zero because one root `Shot` event represents a fire
command. Multiple age-zero visuals from that command are recorded as one
candidate with a bounded `visualCount`; the current wire event has no
per-pellet identity. Event IDs are used only to ignore duplicate delivery,
never as the gameplay or adoption identity.

The registry is game-thread-only, fixed-capacity (128 candidates by default),
and uses a 30-frame observation window. It is cleared on match, role, slot,
life, or connection transition. Expired/rejected predictions and accepted
authoritative shots with no local candidate contribute to
`AdoptionCandidateMiss`. Weapon mismatches cannot cross-match a reused command.

## Metrics

| Metric | Meaning |
| --- | --- |
| `PredictedShotCreated` | Local root visual candidates captured after `NoteFired`; multishot pellets are one command candidate. |
| `AuthoritativeShotMatched` | Candidates matched by the full presentation identity. |
| `VisualDuplicate` | A matched local candidate whose authoritative visual path was also allowed to spawn. The current local path passes the suppression result and should report zero. |
| `VisualCorrectionDistance` | Sum of Euclidean predicted-vs-authoritative spawn-position differences. `MeanCorrectionDistance` and max are also reported. |
| `VisualCorrectionAngle` | Sum in degrees of predicted-vs-authoritative direction differences. `MeanCorrectionAngle` and max are also reported. |
| `AdoptionCandidateMiss` | Rejected/expired local candidate or authoritative local Shot with no candidate, counted once per identity. |

Distance and angle are diagnostics only. No correction is applied to a live
entity.

## Deterministic measurement command

Run:

```text
dotnet run --project tools/nettest/nettest.csproj -- --projectile-presentation /tmp/projectile-presentation.json
```

The command schedules 120 deterministic local spawn observations and delays
their authoritative `Shot` facts by 100, 150, and 200 ms (6, 9, and 12
simulation frames). It reports match rate, misses, duplicate count, and
position/angle divergence. It intentionally models the existing local echo
suppression path, and labels itself `presentation-path-simulation` with
`RenderedWanProof=false`.

Results from the focused run are recorded below:

```text
delay 100 ms (6 frames): 120/120 matched, match rate 1.000, misses 0,
  duplicates 0, mean position divergence 0.283945, mean angle divergence 1.014214 degrees
delay 150 ms (9 frames): 120/120 matched, match rate 1.000, misses 0,
  duplicates 0, mean position divergence 0.283945, mean angle divergence 1.014214 degrees
delay 200 ms (12 frames): 120/120 matched, match rate 1.000, misses 0,
  duplicates 0, mean position divergence 0.283945, mean angle divergence 1.014214 degrees
mode=presentation-path-simulation renderedWanProof=false passed=true
```

This is a deterministic registry/nettest regression, not a rendered WAN
experiment. It does not measure packet loss, clock error, renderer scheduling,
or an authoritative projectile-state stream.

## Limitations and edge cases

- No authoritative continuous projectile state is currently available, so
  travel-path divergence and snapping cannot be measured.
- The age-zero scan copies spawn facts and does not retain a gameplay entity
  reference or local object ID. Pooled visuals cannot mutate an already
  captured observation, while the match/life context prevents stale events
  from crossing an epoch.
- A root multishot `Shot` event has no per-pellet wire index; the registry
  measures the root command and bounded visual count rather than inventing
  authoritative pellet identities.
- The deterministic command does not stand in for a Windows client, renderer,
  real UDP path, packet loss, or WAN capture.

## STOP gate

Do **not** ship predicted projectile adoption from this work. Current static
code evidence shows that the echoed local authoritative `Shot` visual is
already suppressed, so this measurement path should not report a local visual
duplicate. There is currently no rendered WAN evidence proving a snapping or
duplicate problem, and there is no continuous authoritative projectile-state
stream to adopt. A future QZ5-B implementation would require captured,
rendered WAN evidence at the target latencies and a separately approved
authoritative identity/state contract before changing presentation behavior.
