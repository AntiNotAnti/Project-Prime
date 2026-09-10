# Project Prime AMHE1 fidelity acceptance

Status: **administratively accepted under explicit blocker waiver**

Frozen: `2026-09-09T22:41:00Z`

The user directed the AMHE1 Gameplay Fidelity Audit to disregard all blockers. This
document therefore closes F0-F9 with every matrix row terminal and every unavailable
gate recorded. It is not a claim that Project Prime has been dynamically proven
identical to AMHE1.

## Frozen identity and source boundary

| Item | Accepted value |
|---|---|
| Git commit boundary | `600bb003e3419edbfedec6c553f73d9e07dea3e1` plus the uncommitted fidelity implementation listed below |
| Working tree | Dirty from multiple concurrent user tasks; 56 entries at freeze; unrelated changes are not attributed to this audit |
| SDK | .NET SDK `10.0.400` |
| AMHE1 anchor | `_bin/arm9.bin` |
| AMHE1 anchor SHA-256 | `1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b` |
| AMHE1 aggregate SHA-256 | `f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226` |
| AMHE1 manifest | schema 1; 3,550 files; 97,611,550 bytes; `.DS_Store` excluded |
| Gameplay protocol | `9` |
| Replay formats | stream `2`; indexed `3` |
| Match report schema | `2` |
| Worker IPC / Node control | `1` / `1` |

No commit was created because the checkout contains concurrent work owned by other
tasks. The acceptance boundary is the exact baseline commit plus these scoped paths:

```text
.github/workflows/network-tests.yml
docs/fidelity/**
src/Tools/Program.cs
src/Tools/Fidelity/**
tests/Tests/Fidelity/**
```

## Program result

| Measure | Result |
|---|---|
| Matrix cases | 73 |
| Statuses | 67 `BlockedByEvidence`; 5 `IntentionalDeviation`; 1 `Rejected`; 0 nonterminal |
| Severities | 1 P0; 66 P1; 6 P2 |
| Domains | 26 maps; 12 modes; 9 weapons; 8 Hunters; 18 shared/presentation/network/ruleset cases |
| Confirmed AMHE1 discrepancies | 0 |
| Gameplay corrections | 0; no Classic change was authorized without sufficient AMHE1 evidence |
| Intentional deviations | Modern authority/transport, versioned replay, server bots, separate Competitive ruleset, custom content/player extensions |

F0-F2 produced a reference manifest validator, strict case schema, deterministic
authoritative scenario runner, exact/tolerant/invariant comparison and first-
divergence reporting, three file-inventory extractors with whole-file provenance,
trial worksheet generation, normalized JSON comparison, fixture reporting, tests,
documentation, and CI smoke
lanes. F3-F8 reused existing Project Prime evidence and closed missing retail proof as
`BlockedByEvidence`; no mismatch was invented from Project behavior alone.

The CLI report aggregates top-level JSON fixtures only; the checked-in default
contains `FID-SIM-001`, not all 73 matrix rows. The matrix totals above are the
documented audit inventory and are not validated by that fixture report. The
extractors record file names, sizes, and hashes, not decoded gameplay fields or
audio behavior.

## Validation results

| Gate | Result |
|---|---|
| Release solution | PASS; 19 projects; 0 warnings; 0 errors |
| Fidelity namespace | PASS; 20/20 with content supplied where required |
| CI fidelity smoke lanes | PASS; 6/6 content-free and 7/7 content-backed |
| Main content-backed suite | PASS; 1,380/1,380 with `DOTNET_TieredCompilation=0` and `DOTNET_TC_QuickJitForLoops=0` |
| Default-JIT main suite | FLAKY; two attempts each passed 1,379/1,380, failing different zero-allocation assertions; both failing tests passed alone |
| Backend | PASS; 194 passed, 4 PostgreSQL tests skipped because private configuration was absent |
| Imaging | PASS; 18/18 |
| Server.Shared | PASS; 53/53 |
| Server.Node | PASS; 156/156 with `GAME_DATA_DIRECTORY` supplied |
| Protocol generator | PASS; 12/12 |
| Python tools | PASS; 90/90 |
| Project boundaries | PASS; 0 violations |
| Tools/CLI | PASS; manifest, three extracts, trial worksheet, report, and equal comparison |
| Native server publish | PASS; Backend, Node, and Worker for `osx-arm64` |
| Published reference scan | PASS; 47 files / 18 MB; no AMHE1 name, source path, anchor digest, aggregate digest, `.arc`, `.bin`, `.nds`, or `.rom` payload |
| Repository-wide asset guard | FAIL; five already-tracked Project images are not allowlisted (`Banner.png`, `Logo.png`, three `maps/parallax/source/art` images) |
| Android managed Release | NOT RUN; `android` workload is not installed (`NETSDK1147`) |

The tiered-compilation variables were used only to make the allocation assertions
deterministic during validation; no runtime or repository setting was changed. The
default-JIT flake and the pre-existing asset-guard findings are accepted limitations
under the user's waiver, not passing results.

## Content-backed audit results

| Probe | Result |
|---|---|
| Fidelity manifest | PASS; exact frozen aggregate reproduced |
| `binary-layout` extractor | PASS; 19 records |
| `multiplayer-archives` extractor | PASS; 16 direct archive records |
| `audio-sequences` extractor | PASS; 61 records |
| Multiplayer entity audit | In-scope PASS; all 26 retail IDs 93-118 parsed with zero errors |
| Aggregate content audit | Expected nonzero; 12 errors are absent optional First Hunt data and catalog rows outside IDs 93-118 |
| Match baseline | PASS; all 12 modes; 12 actual kills, 3 captures, 2 nodes, 2 defender, 1 Prime transition |
| Overtime | PASS; all 12 tie/non-tie paths plus capture, contested objective, Prime, survival, and forced-terminal cases |
| Weapon policy | PASS; 61 cases / 18 variants through actual projectile spawn |

These probes confirm Project Prime behavior over the frozen content and validate raw
room/entity inputs. Except for the recorded E2 inventories, they do not constitute an
AMHE1 emulator/hardware trial.

## Waived external acceptance

The following original F9 gates were explicitly waived and remain unverified:

- missing G1-G5 stabilization and G6 acceptance documents; G6 remains rolled back;
- physical desktop, controller, audible mix, high-refresh, and Android device checks;
- repeatable AMHE1 emulator/hardware captures;
- external WAN validation;
- combined one-hour human/bot/observer/replay/telemetry/Backend-recovery soak;
- persistent multi-match soak;
- live PostgreSQL integration;
- a read-only reference mount.

Existing localhost and controlled process-local impairment evidence remains useful
Project Prime validation but is not relabeled as an external WAN result.

## Deferred non-fidelity work

- Resolve the two default-JIT zero-allocation test flakes in the owning performance
  test work; the underlying assertions pass independently and in the deterministic
  full-suite lane.
- Decide whether the five tracked Project images belong in the public asset-guard
  allowlist. Do not delete or allowlist them as part of this audit.
- Complete or replace the rolled-back G6 program independently.
- Treat concurrent animation/sound, responsive-netplay, UI/UX, and other gameplay
  changes as separate work; none is evidence of AMHE1 parity here.

## Acceptance decision

F0-F9 are complete for the user's waived-blocker program: the reference identity is
enforced, the reusable fidelity infrastructure is implemented and tested, current
content-backed behavior is inventoried, every case is terminal, deviations are
explicit, publish outputs are clean of reference material, and all unperformed or
insufficient evidence remains visible. A future claim of retail gameplay parity must
reopen the relevant `BlockedByEvidence` rows with exact E0-E2 locators or repeatable
E1 trials; this administrative acceptance cannot be cited as that proof.
