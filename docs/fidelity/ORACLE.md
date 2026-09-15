# AMHE1 retail oracle (P0-A)

The oracle is an optional development adapter behind the existing `fidelity`
command. Project Prime never depends on it at runtime. A private AMHE1 `.nds`
image is accepted only through an explicit `--rom FILE` argument and the
frozen header/length/SHA-256 identity below. No deterministic external oracle
adapter executable is present, so recording remains blocked until an operator
supplies that adapter and the matching extracted AMHE1 environment.

## Commands

```text
fidelity oracle list [--scenarios DIRECTORY]
fidelity oracle record SCENARIO --reference DIRECTORY --adapter FILE
    [--rom FILE] [--artifact-root DIRECTORY] [--timeout-ms N] [--run-id ID]
fidelity oracle compare SCENARIO --expected FILE --actual FILE
    [--normalizer FILE]
fidelity oracle verify ARTIFACT --reference DIRECTORY [--rom FILE]
```

`verify` requires the extracted AMHE1 directory and hashes it against the
artifact's frozen source identity. Artifact parsing alone is reported as
schema/source validation, not content verification.

`record` verifies the extracted AMHE1 reference before launching the adapter,
hashes the exact regular executable it is about to launch, creates a fresh
run-owned directory, passes only bounded scenario/reference/output arguments,
and launches without a shell. With `--rom FILE`, it also verifies the private
AMHE1 header, exact 67,108,864-byte length, and frozen whole-image SHA-256
before passing that exact path to the adapter as `--rom`; the path is never
written to an artifact. It kills only that direct child on timeout. The adapter
must write `artifact.json` with `result: "normalized"`; missing, stale,
mismatched, or unnormalized artifacts fail closed. Existing run directories
are rejected rather than reused.

The artifact must bind `oracleExecutableIdentity` to the computed
`sha256:<digest>`. When `--rom` is supplied, `romIdentity` must bind to the
verified complete-image `sha256:<digest>`; without it, the adapter must
explicitly emit `unverified:<reason>`. This distinction is an evidence
boundary, not a retail behavior claim.

## Strict model boundary

Version 1 contains `OracleScenario`, `OracleArtifact`, `OracleCheckpoint`,
`OracleComparison`, and `OracleNormalizer` models. The hand-written JSON
boundary rejects unknown/missing envelope fields, duplicate properties and
checkpoint identities, non-finite numbers, wrong Project Prime/AMHE1 source
identity, path escapes, unsupported state roots, and oversized documents,
arrays, strings, and nesting.

Timing is retained on independent axes:

```text
VBlank index | elapsed game milliseconds | semantic event index
```

Exact, per-field tolerant, and invariant comparisons report `Compatible=false`
for incompatible inputs and otherwise stop at the earliest divergence. Missing
state, checkpoint, event, or normalizer data is never treated as equal.
The reviewed baseline normalizer is checked in at
`tools/fidelity/normalizers/AMHE1-v1.json`; callers may supply a different
strictly parsed normalizer only when its AMHE1 revision matches the artifacts.

The frozen private cartridge identity is `AMHE1`, game code `AMHE`, ROM
revision `1`, length `67,108,864`, and SHA-256
`bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f`.

## Checked-in scenarios

`tools/fidelity/scenarios/` contains content-free specifications for all seven
retail Hunters, Imperialist, alternate-form movement, pickups, lifecycle, and
basic beam/aim/movement cases. They identify the AMHE1 source but contain no
ROM-derived state or proprietary observations. Their checkpoints specify only
candidate VBlank observation points; `elapsedMilliseconds` and
`semanticEventIndex` are intentionally `null` until an independently observed
adapter supplies those values. They are candidate schedules, not proof that a
live retail adapter has executed them.

AMHE0 Recomp observations may motivate a scenario or invariant, but cannot be
promoted to AMHE1 truth without an independently verified AMHE1 observation.
