# FID-SIM-001 — bounded authoritative simulation harness

Status: `BlockedByEvidence`

Severity: `P1`

Evidence: `E2` for Project Prime behavior; AMHE1 runtime comparison pending

## Question

Can a bounded, renderer-free fixture drive Project Prime's real Worker-owned
authoritative path deterministically and produce actionable comparisons?

## Evidence

- Project Prime baseline: `600bb003e3419edbfedec6c553f73d9e07dea3e1`.
- AMHE1 aggregate: `f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226`.
- Fixture: `tests/Tests/Fidelity/Fixtures/FID-SIM-001.json`.
- Runner: `FidelityScenarioRunner`, using `ServerSimulation`, `ServerNetwork`, and
  `ServerInputStream` with no renderer, Backend, or socket.

## Confirmed behavior

Same-seed repeated runs produce the same selected-state digest. A second match can
be interleaved and disposed without changing the primary trace. A deliberate
mismatch reports its first divergent tick, preceding values, identity, seed, and
reproduction command.

The companion F1 tests exercise idle, normalized spawn/respawn, fixed-input movement,
direct damage, projectile spawn/despawn, pickup consume/respawn, and node capture.
These prove harness reachability only.

## Decision

Accept the F1 harness foundation. Under the user's 2026-09-09 blocker waiver, retain
the missing AMHE1 frame-to-tick mapping as a terminal `BlockedByEvidence` result. Do
not infer AMHE1 parity and do not change simulation constants until an E0-E2
reference case establishes the expected value.

## Reproduction

```bash
GAME_DATA_DIRECTORY=/absolute/path/to/AMHE1 \
dotnet test tests/Tests/Tests.csproj -c Release \
  --filter FullyQualifiedName~FidelityScenarioTests
```
