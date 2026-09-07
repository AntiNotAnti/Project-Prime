# G1.7 spawn policies

Classic remains the default. Dedicated servers opt in with `-spawnpolicy enhanced` or `-spawnpolicy duel`; `-cancelspawnprotection true` independently enables offensive cancellation. Both settings are applied to the initial match and every rotation entry. Missing values and unknown names fail configuration; numeric policy names are not accepted. The immutable MatchRules constructor validates the enum, and With preserves both settings unless explicitly replaced.

## Ownership and Classic parity

Each Scene owns a SpawnDirector. PlayerEntity delegates GetRespawnPoint to it. The director commits the original four-tick spawn cooldown at selection time. The normal and ServerActivate callers still apply the returned authored position; no candidate means null, never a fabricated origin. Authored origin positions are valid. ServerActivate retains its explicit no-usable-spawn failure; normal respawn remains pending if the content has no eligible spawn.

Classic retains the first 25 spawn-entity limit, including rejected entries; active/cooldown/frame-zero Availability filters; Capture team check; minimum squared distance capped at 100 against every living player (including allies and requester); strict crowded-point greater-than comparison; safe candidate selection by FrameCount modulo count; and the second scan of all spawn entities that falls back to the first active point without the other filters. Classic never advances either the director RNG or the global combat/AI streams. Its temporary safe-candidate buffer is a fixed 25-entry array rather than a per-selection List.

## Enhanced and Duel

All authored spawn entities are considered. Active state, finite coordinates, frame-zero Availability, and authored team membership in team modes are hard filters. A neutral team index of -1 is eligible. The first pass also rejects cooldown. If it finds no candidate, a second pass relaxes only cooldown. Crowding and visibility affect score and never make an otherwise valid point unusable.

The score is deterministic float arithmetic over stable entity enumeration. These are initial opt-in weights, not a claim of measured competitive balance:

| Component | Enhanced | Duel |
| --- | ---: | ---: |
| Nearest enemy squared distance (capped at 900) | +distance | +distance |
| Each enemy with unobstructed collision LOS | -200 | -400 |
| Each visible enemy facing within dot > 0.5 | -150 | -300 |
| Each enemy inside squared distance 100 | -100 | -100 |
| Recent nearby death | -250 × danger | -500 × danger |
| Recent same-point use | -100 × recency | -200 × recency |
| Requester's most recent unexpired spawn is this point | -350 | -700 |
| Nearby friend in a team mode | +50 × proximity | +50 × proximity |
| Nearby contested/hostile node or enemy Capture base | -150 × proximity | -150 × proximity |
| Nearby available major pickup | -75 × proximity | -300 × proximity |

Proximity is max(0, 1 - squaredDistance/100). Major pickups are HealthBig, DoubleDamage, OmegaCannon and Deathalt; only enabled/always-active spawners with an available item contribute. Objective scoring is explicitly restricted to the corresponding match modes. LOS uses the existing scene collision query between player/spawn positions plus 0.5 vertical units, so walls affect the score. Living active enemies are considered; requester and inactive/dead slots are excluded. Friendly fire does not turn a teammate into an enemy for spawn placement.

Ties use reservoir selection driven by a private 32-bit LCG (`state * 1664525 + 1013904223`, unchecked), advancing only for exact equal scores. ServerSimulation initializes this stream from its captured initial Rng2 without consuming that global stream. Countdown reset clears histories and restores the same seed before player activation. No wall clock or platform random source participates.

## Bounded history and diagnostics

SpawnDangerHistory stores at most 64 deaths and 128 spawn uses in fixed rings. Entries older than 600 simulation ticks stop influencing score; records decay linearly over that interval. Death danger also decays by squared proximity. Ring writes and evaluation allocate no history collections. RecordDeath is called where the gameplay death counter increments, and replicas do not author death history. Successful selection records spawn id, player slot and scene tick. Reset clears logical ring counts and last selection.

LastSelection exposes the selected weighted SpawnCandidate components, including cooldown fallback. Evaluate exposes the same scoring calculation without selection, RNG, or history mutations for later telemetry. Classic has no weighted LastSelection.

## Protection cancellation boundaries

The option defaults to false even when Enhanced or Duel is selected. The private player hook clears only spawn protection, leaving damage invulnerability and all duration defaults intact. It runs after successful beam creation (after the NoSpawn early return), after a bomb is actually created, inside Sylux's legacy explicit three-bomb detonation branch, at accepted Spire/Trace/Weavel attacks, once Noxus reaches its active attack phase, and when a Samus boost starts. Failed fire/bomb creation, charge-only input, and Noxus windup do not cancel protection. Classic with the option false preserves existing protection behavior.

## Protocol and validation

The protocol owner is extending live MatchRules encoding and testing replica round trips; frozen protocol-7 demo rules retain Classic/false defaults. Completion requires those checks, the CLI tests, and real-content spawn behavior tests. Validation results are appended after they finish; this document does not claim live multiplayer balance or device evidence.


### Real-content protection and Classic characterization

`SpawnDirectorTests` now compares Classic selection to an independent frozen selector over six frame counts and five scenarios in both Battle and Capture: clear candidates, eight living players, all candidates on cooldown, an active fallback beyond the first 25 on the opposite team, and no active candidates. It checks the selected entity, cooldown, and unchanged director/global RNG streams. Synthetic spawn records are appended to the retail SANCTORUS scene through the real entity data constructor; room collision and player simulation use extracted AMHE1 content.

Attack cases use actual normalized input and damage probes to verify successful missiles, bombs, Spire/Trace/Weavel attacks, Noxus windup versus activation, and Samus boost charge versus release. Failed bomb allocation exhausts and restores the real scene pool. The NoSpawn firing case invokes the private real `TryFireWeapon` method because ordinary player processing automatically switches away from an unaffordable weapon before input; it does not read or write the spawn-protection timer.

A characterization exposed an existing Sylux boundary: `PlayerProcess` sets bomb ammo to `3 - SyluxBombCount`, then input requires positive bomb ammo before calling `SpawnBomb`. Thus the explicit three-bomb branch is unreachable through a normal fourth attack input; the autonomous bomb linking pass is separate. Tests preserve this guard and verify successful Sylux bomb placement cancels protection. No Sylux balance or inventory behavior was changed.

Validation: all 17 `SpawnDirectorTests` passed with AMHE1 content in Release, rebuilding referenced projects. Log: `/tmp/codex-re-prime-g1/spawn-gaps-tests.log`. This is deterministic headless gameplay evidence, not a live-client or balance evaluation.
