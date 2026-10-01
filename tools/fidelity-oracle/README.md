# Fidelity Oracle

This initial pack captures production clock, random-stream, lifecycle and
continuous-weapon-phase behavior without game content. It does **not** simulate
player movement, projectiles, damage, objectives, or alternate forms. Those require
additional engine scenarios; passing this pack is F1 evidence, never F2–F5 evidence.

```sh
dotnet run --project tools/fidelity-oracle -c Release
dotnet run --project tools/fidelity-oracle -c Release -- verify all -baselines tools/fidelity-oracle/baselines
dotnet run --project tools/fidelity-oracle -c Release -- list
dotnet run --project tools/fidelity-oracle -c Release -- run simulation.rng
```

The game also accepts `-fidelityoracle list|run|verify|record SCENARIO`. Published
builds copy the public normalized baseline files beside the binary. `-baselines`
selects another directory. `-presentation-hz` tests the clock scenario at a chosen
30–1000 Hz presentation rate; it never changes the 60 Hz simulation contract.

Each scenario declares a version, seed, tick count, capture interval, content
requirement and evidence tier. Checkpoints contain sorted integer values. Where
floating-point state must be captured, normalization rounds to 1/4096 with explicit
midpoint-away-from-zero semantics and rejects non-finite/overflowing values.
The canonical SHA-256 excludes machine-specific paths, wall time and revision;
it includes the scenario contract and each normalized field. Baselines also record
the engine revision used to obtain the reference data. No proprietary content or
memory dumps belong in this directory.

Verification is read-only, validates the stored hash and exits nonzero for missing
or invalid baselines, changed contracts, or differing fields. The first difference
is printed as a readable diagnostic and structured JSON with scenario, tick, field,
expected and actual values. Normal tests never regenerate expected values.

To propose a baseline change deliberately:

```sh
dotnet run --project tools/fidelity-oracle -c Release -- record simulation.rng \
  --developer-record -engine-revision REVIEWED_COMMIT -baselines tools/fidelity-oracle/baselines
```

Recording requires both explicit developer opt-in and a revision, and is disabled
when `CI` is set. Baseline changes require code review of the normalized diff and
an explanation of the intended gameplay/infrastructure change. The initial six
baselines are proposed references from `e80098f4`, not evidence that the rest of
the implementation plan has passed gameplay acceptance.

Evidence tiers:

| Tier | Evidence |
| --- | --- |
| F0 | Schema/unit tests |
| F1 | Content-free deterministic production components |
| F2 | Extracted-content deterministic engine simulation |
| F3 | Rendered local validation |
| F4 | Real process/network E2E |
| F5 | Physical device / real WAN acceptance |

Remaining packs: match transition/respawn, movement, all eight projectile/damage
weapon scenarios, disconnect/resume and match epoch, all match modes/objectives.
Comprehensive alternate forms remain the **last** gameplay pack, after these core
scenarios. Do not substitute invented toy movement or damage calculations for
production simulation to fill out the scenario list.
