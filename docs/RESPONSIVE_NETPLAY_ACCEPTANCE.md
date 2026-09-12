# Responsive netplay acceptance

## Scope and authority

This pass adds presentation-only speculative hit feedback and local self-impulse
prediction. The Worker remains the sole owner of health, death, score,
afflictions, objectives, collision history, and lag-compensation selection.
The later NetPlay fidelity pass changed the input wire contract to protocol 12
only after a deterministic death/respawn regression proved that unseen
pre-death commands needed an explicit life epoch. The current wire contract is
protocol 16: it preserves the protocol-15 presented-frame denominator,
authenticated UDP envelope, and explicit optional radial movement sample, and
adds authoritative Spire alternate-form attack presentation state. The movement sample
is quantized to signed axes in the inclusive range -127..127; -128 is reserved
and malformed radial values are rejected. This does not grant the client gameplay
authority; the Worker
validates the epoch against its current player life and remains the sole
authority for gameplay state.

The authentication boundary introduced in protocol 14 is Node-issued, per-handoff, and
direction-bound. A handoff exposes a bounded `AdmissionId`; the associated
32-byte key is installed and acknowledged by the owning Worker before the
handoff is published. Client joins and established packets are authenticated
before body validation and state application. Enabled mode drops unknown or
unauthenticated joins and has no keyless fallback. `UdpAuthenticationEnabled`
is enabled for production; disabling it is an explicit legacy/test seam only.
Keys are redacted from logs, string representations, tickets, CLI arguments,
and environment values. Protocol 15 and older peers are intentionally
incompatible with this live wire contract. Protocol-14 replay timelines remain
readable when their stored timeline format is independent of the current
input-command payload.

## Repository implementation status

| Phase | Status | Evidence boundary |
| --- | --- | --- |
| P0 foundation | Implemented | Bounded metrics and settings; confirmed timing remains the player default. |
| P1 hit prediction | Implemented | Existing collision attempts are observed before replica suppression; identity is match/connection/life/command fenced. |
| P2 markers | Implemented | Predicted, hit, headshot, and kill use one priority state with authoritative-only audio. |
| P3 self impulse | Implemented, developer-disabled | Verified Missile, Battlehammer, and Magmaul self-damage directions use the shared engine transform. Bomb jump remains its separate retail velocity-floor path. |
| P4 WAN tooling | Implemented, evidence-pending | Existing rendered Node to Worker tools capture hit and impulse metrics; N5/N6 tooling fails closed for missing real-WAN evidence. Real geographic evidence is not yet recorded. |
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

## N5/N6 fail-closed tooling boundary

N5 is represented by `--rendered-wan-snapshot-matrix-plan` and its self-test.
It creates paired 30/60 Hz cells for an operator to run, but local or
process-local impairment remains `rendered-loopback-process-local-impairment`.
The decision remains `INSUFFICIENT_REAL_WAN_EVIDENCE` unless all cells are
complete and explicitly marked `real-wan-independent-path`; no planner output
can promote 60 Hz or change the production default.

N6 is represented by the authenticated two-client report/merge checks. The
merge validates run, match, Worker, role, connection, and shot-identity
bindings, rejects developer-fixture or mismatched reports, and will not emit
headshot agreement without actual headshot evidence. `renderedWanProof` and
human-review acceptance remain false for local fixtures, incomplete reports,
or missing geographic endpoints.

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
