# Responsive netplay acceptance

## Scope and authority

This pass adds presentation-only speculative hit feedback and local self-impulse
prediction. The Worker remains the sole owner of health, death, score,
afflictions, objectives, collision history, and lag-compensation selection.
The later NetPlay fidelity pass changed the input wire contract to protocol 12
only after a deterministic death/respawn regression proved that unseen
pre-death commands needed an explicit life epoch. This does not grant the
client gameplay authority; the Worker validates the epoch against its current
player life.

## Repository implementation status

| Phase | Status | Evidence boundary |
| --- | --- | --- |
| P0 foundation | Implemented | Bounded metrics and settings; confirmed timing remains the player default. |
| P1 hit prediction | Implemented | Existing collision attempts are observed before replica suppression; identity is match/connection/life/command fenced. |
| P2 markers | Implemented | Predicted, hit, headshot, and kill use one priority state with authoritative-only audio. |
| P3 self impulse | Implemented, developer-disabled | Verified Missile, Battlehammer, and Magmaul self-damage directions use the shared engine transform. Bomb jump remains its separate retail velocity-floor path. |
| P4 WAN tooling | Implemented | Existing rendered Node to Worker tools now capture hit and impulse metrics. Real geographic evidence is not yet recorded. |
| P5 rewind tuning | Not authorized by evidence | The server cap remains 15 ticks. |
| P6 rollout | Pending | Instant timing and self-impulse defaults require the external gates below. |

Fidelity-alignment observability is implemented: headshot classifications,
rewind clamp error in world units, and committed presented-versus-simulation
position error are all bounded metrics. The deterministic headshot scenario
and its validity gate are available only in the isolated loopback developer
fixture. No presented collision proxy or production-default change has been
authorized by those local measurements.

Development controls:

```text
-nohitprediction
-selfimpulseprediction
-noselfimpulseprediction
```

The rendered WAN validation client explicitly enables the candidate features;
ordinary client defaults remain `HitMarkerTiming.Confirmed` and self-impulse off.

## Representative WAN matrix

| Effective RTT | Loss | Jitter | Run | Human review |
| ---: | ---: | --- | --- | --- |
| 50 ms | 0% | low | Pending | Pending |
| 100 ms | 1% | low | Pending | Pending |
| 150 ms | 2% | moderate | Pending | Pending |
| 200 ms | 3% | moderate | Pending | Pending |
| 250 ms | 3% | high | Pending | Pending |
| 300+ ms | 5% | high | Pending | Pending |

Use the existing `--rendered-wan-operator`, `--rendered-wan-client`, and
`--rendered-wan-merge` commands so the path includes WSS Node admission, signed
Worker handoff, and direct UDP gameplay. The merged report deliberately remains
candidate evidence until endpoint geography and human review are recorded.
Client reports carrying the new hit and impulse metrics use the versioned
`project-prime.rendered-wan-split.v2` tooling schema; this does not change the
gameplay wire protocol.

## Required external gate

- Run rendered clients on at least US, Europe, and Japan/APAC paths, including
  one 250+ ms and one 300+ ms path.
- Record discrete and continuous confirmation rates separately.
- Review marker promotion, false feedback, Shock Coil stability, self-jump
  correction, remote snapping, and death trustworthiness.
- Verify zero predicted deaths, score changes, afflictions, duplicate damage,
  duplicate impulses, and cross-epoch matches.
- Compare 15/18/21/24-tick caps only if 15-tick clamping is material in those
  runs. Select the smallest cap with a measured benefit.

Until that evidence exists, `LagCompensationPolicy.MaxRewindTicks` remains 15,
`HitMarkerTiming` remains Confirmed by default, and self-impulse prediction
remains opt-in for development.
