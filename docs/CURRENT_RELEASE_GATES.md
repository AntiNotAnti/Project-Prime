# Current Project Prime release gates

This document defines the gates. It does not duplicate a particular run's
commit, protocol identifiers, warning counts, or test totals. CI and local
acceptance runs generate those volatile facts with:

```text
python3 tools/generate-release-evidence.py
```

The inspectable outputs are:

```text
artifacts/release/release-evidence.json
artifacts/release/release-evidence.md
```

## Evidence states

Every gate uses one of three states:

- `passed`: the named check ran in its qualifying environment and passed.
- `failed`: the check ran and failed, or its evidence was malformed.
- `not-run`: the check or required environment did not produce evidence.

`not-run` is not success. Missing content, hardware, credentials, test results,
or diagnostics must remain visible instead of being inferred from another gate.

## Automated gates

| Gate | Qualifying evidence | Release effect |
| --- | --- | --- |
| `builds` | Release builds for the intended production projects and targets | Failure or missing target evidence blocks that target |
| `tests` | Machine-readable TRX from the applicable content-free and content-backed suites | Any failure blocks release; omitted required suites remain `not-run` |
| `warnings` | `check-warning-budget.py` comparison against the committed production baseline | New warnings or increased counts block release |
| `projectBoundaries` | `check-project-boundaries.py` and applicable client/server boundary guards | A dependency or ownership violation blocks release |
| `multiplayerGuard` | `check-multiplayer-only.py` | Retired campaign runtime in the shipping source blocks release |
| `protocolGuard` | `check-current-protocol.py` plus protocol identity read from source by the evidence generator | Drift between source and protocol documentation blocks release |
| `postgres` | Backend integration TRX produced with the private PostgreSQL connection-file contract | Failure blocks release; no configured database is `not-run` |
| `lifecycle` | Lifecycle-fast, real Worker vertical, and required stress TRX | Required suite failure or absence blocks architecture acceptance |
| `fidelity` | Scenario/oracle artifacts backed by a verified AMHE1 retail observation | Synthetic or Project Prime-only observations do not qualify |
| `e2e` | Canonical two-client semantic lifecycle evidence and diagnostics | A harness smoke or status-only path does not qualify |
| `platformAcceptance` | The manual matrix below, tied to the tested commit and package | Unaccepted required platforms block public release |

The JSON artifact is authoritative for a run. Its protocol, Node-control, and
replay-format identifiers are resolved from their defining C# constants. Test
and lifecycle totals are read from TRX. Warning state is read from the warning
budget comparison. A missing artifact is recorded as `not-run`.

## Manual and external acceptance

The generator cannot turn source or CI confidence into physical evidence. The
following remain manual or environment-backed:

- real Windows, macOS, Linux, and Android launch/render/input/audio checks;
- physical controller, touch, high-refresh, resize, focus, and device-loss checks;
- geographic WAN behavior and long-duration mixed-combat/worker soak;
- deployed PostgreSQL backup and restore rehearsal;
- protected binary launch, symbols, crash reporting, and recovery;
- update publication, rollback, and clean-install/upgrade rehearsal;
- retail AMHE1 oracle capture from a verified deterministic adapter.

Each result must identify the commit or immutable package, platform/runtime,
scenario, outcome, and artifact location. Secrets, tokens, private connection
strings, cartridge data, and ROM paths must not enter release artifacts.

## Release decision

A release is blocked when any required gate is `failed` or `not-run`. A narrow
development build may proceed with open external gates only when its scope is
stated explicitly and it is not represented as physical, retail-fidelity,
deployed, or public-release acceptance.

### Temporary P5 development decision

At the project owner's explicit direction on 2026-09-15, P5 is treated as
temporarily passing for continued development because the required physical,
WAN, device, deployed-service, protected-package, rollback, and long-soak tests
cannot currently be completed.

```text
evidenceStatus: not-run
effectiveDecision: temporarily-passed
scope: local and CI development only
releaseEligible: false
expires: before any public release candidate
```

This is an administrative waiver, not fabricated test evidence. It does not
change any underlying gate from `not-run` to `passed`, certify a platform or
retail-fidelity result, or permit a public release. Replace this decision with
commit- and package-bound P5 evidence before public release certification.

The roadmap ordering remains:

```text
G1-G5 stabilization
 -> multi-instance server architecture
 -> isolation/architecture acceptance
 -> G6 Lobby + UI/UX
 -> stabilization
 -> AMHE1 gameplay-fidelity audit
```
