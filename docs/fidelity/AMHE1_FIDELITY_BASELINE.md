# Project Prime AMHE1 fidelity baseline

Status: frozen F0 baseline, carried through the F9 administrative closure. On
2026-09-09 the user explicitly waived the implementation plan's missing entry and
external acceptance gates and directed the AMHE1 audit to proceed. Waived gates are
**unverified**, not passed, and remain recorded limitations of the accepted baseline.

## Entry-gate decision

| Gate | Recorded status |
|---|---|
| `docs/G1_G5_STABILIZATION_BASELINE.md` | Waived; absent at freeze |
| `docs/G6_ACCEPTANCE.md` | Waived; G6 rollback remains current |
| Desktop/controller/audio/high-refresh and Android physical acceptance | Waived; unverified |
| External WAN | Waived; unverified |
| PostgreSQL integration | Waived; private configuration absent locally |
| Combined and persistent one-hour soaks | Waived; unverified |
| Read-only reference mount | Waived for the local user-owned tree; audit tooling is read-only |

## Project Prime identity

| Item | Frozen value | Source |
|---|---|---|
| Baseline commit | `600bb003e3419edbfedec6c553f73d9e07dea3e1` | Clean `HEAD` before F0 work |
| SDK | .NET SDK `10.0.400` | `global.json` / local SDK |
| Gameplay protocol | `9` | `src/Game/Protocol/NetHeader.cs` |
| Replay formats | stream `2`, indexed `3` | `src/Shared.Replay/ReplayFile.cs` |
| Match report schema | `2` | `src/Backend/Rating/RatingPolicy.cs` |
| Worker IPC / Node control | `1` / `1` | Server.Shared codecs |
| Simulation | fixed 60 Hz; Worker authoritative; one writer per `MatchInstance` | Project architecture |

Concurrent gameplay, feedback, and networking work began after the clean commit was
captured. It is not silently attributed to this program; every fidelity case must
record the source revision and dirty-state boundary used for its observation.

## AMHE1 identity

The reference is the user's extracted USA revision-1 content and is excluded from
source control and publishing.

| Item | Value |
|---|---|
| Anchor | `_bin/arm9.bin` |
| Anchor SHA-256 | `1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b` |
| Manifest schema | `1` |
| Manifest files | `3550` (`.DS_Store` host metadata excluded) |
| Manifest bytes | `97,611,550` |
| Aggregate SHA-256 | `f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226` |

Reproduce without changing AMHE1:

```bash
dotnet run --project src/Tools/Tools.csproj -c Release -- \
  fidelity manifest --reference /absolute/path/to/AMHE1 \
  --output artifacts/fidelity/amhe1-manifest.json
```

The command rejects an unknown anchor, links, path escape, and excess file/path/byte
bounds. Generated output is covered by the existing `artifacts/` ignore rule.

## Scope

- Every retail multiplayer room with metadata ID 93-118.
- All 12 supported multiplayer modes.
- Samus, Kanden, Trace, Sylux, Noxus, Spire, and Weavel. Guardian is audited only
  where current compatibility/bot paths expose it.
- Classic is the AMHE1 comparison target. Competitive remains explicitly separate.
- Practice, hosted, dedicated, bot, remote-player, observer, and replay roles.
- Custom maps receive shared-rule compatibility coverage but cannot claim AMHE1
  geometry or map-entity fidelity.

No dynamic AMHE1 emulator/hardware environment has yet been frozen. Runtime-only
claims remain `BlockedByEvidence` until a case records configuration and repeatable
trials.

## F1 harness freeze

The bounded runner uses `ServerSimulation` and `ServerNetwork`, injects inputs through
the real `ServerInputStream`, steps only the fixed authoritative path, and observes a
small selected state surface after each tick. Its initial proof suite covers idle,
spawn/respawn, fixed-input movement, direct damage, projectile lifecycle, pickup
consumption/respawn, and node capture/score transition. These are harness proofs and
make no AMHE1 parity claim.

The runner also proves same-seed repeatability, interleaved match isolation through
other-match disposal, exact/tolerant/invariant comparisons, and first-divergence
context. Fixture and manifest inputs are versioned, bounded, canonical-path checked,
and reject symbolic links and unknown fields/reference identities.

## F2 toolkit freeze

The existing `Tools` application now exposes a fail-closed `fidelity` command group:

```text
fidelity manifest
fidelity extract binary-layout|multiplayer-archives|audio-sequences
fidelity run <case-id>
fidelity compare <case-id>
fidelity report
```

The three extractors are deliberately narrow. They emit deterministic records with
source-relative paths, whole-file offset zero, sizes, cryptographic digests, and
file provenance. These are file inventories, not field-level binary, archive, or
audio decoding. `run` creates a bounded dynamic-trial worksheet; it does not claim
to execute AMHE1. `compare` performs an exact normalized-JSON comparison and reports
the first canonical difference. Unknown commands/options, duplicate options,
unsupported domains, wrong revisions, links/path escape, malformed JSON, duplicate
JSON properties, oversized inputs, and unsupported fixture schema values fail closed.

Against the frozen tree, the extractors emitted 19 binary-layout records, 16 direct
multiplayer archive records, and 61 audio-sequence records. The existing deeper
multiplayer audit remains the source for per-room entity inventories.

`report` aggregates only top-level JSON fixtures in the selected fixture directory.
The checked-in default currently contains only `FID-SIM-001`; it does not cover or
validate the 73 rows recorded separately in `AMHE1_FIDELITY_MATRIX.md`.

## Local validation snapshot before F0

Point-in-time checks passed: Release solution build, 1000 content-backed main tests,
18 imaging, 53 Server.Shared, 114 Server.Node, 76 Python tests, project boundaries,
and native `osx-arm64` Backend/Node/Worker publishes. Backend passed 192 tests with
four PostgreSQL cases skipped. This does not close the waived gates.

F0-F2 validation added 20 passing fidelity-only tests. During implementation, a
concurrent presentation task temporarily prevented the full shared `Tests.csproj`
build from being the proof vehicle, so the same source files were also compiled and
run through an ignored, fidelity-only scratch test project. Final shared-project
results are recorded in `AMHE1_FIDELITY_ACCEPTANCE.md`.
