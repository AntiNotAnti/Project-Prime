# Fidelity Oracle

The default pack captures production clock, random-stream, lifecycle and
continuous-weapon-phase behavior without game content (F1). An opt-in F2 pack
runs the actual offline engine with extracted AMHE1 content: walking, jumping,
air control, knockback, all eight weapons, aimed hits, a classified headshot and floor-impact splash damage, death/respawn, match timer expiration, five match modes, and the final seven-hunter alt-form matrix. Passing these packs is never F3–F5 evidence.

```sh
dotnet run --project tools/fidelity-oracle -c Release
dotnet run --project tools/fidelity-oracle -c Release -- verify all -baselines tools/fidelity-oracle/baselines
dotnet run --project tools/fidelity-oracle -c Release -- list
dotnet run --project tools/fidelity-oracle -c Release -- run simulation.rng
```

The game also accepts `-fidelityoracle list|run|verify|record SCENARIO`. Published
builds copy the public normalized baseline files beside the binary. `-baselines`
selects another directory. `-presentation-hz` drives the clock and F2 scenarios at a chosen
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
an explanation of the intended gameplay/infrastructure change. The initial content-free
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

## Optional actual-engine scenarios

Run through the game entry point so its configured `paths.txt` is loaded:

```sh
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -fidelityoracle verify all \
  -allow-content -presentation-hz 144
```

`all` excludes content scenarios unless `-allow-content` is supplied. Explicit F2
scenario IDs also require that flag. Missing extracted files fail clearly; tests
never download assets or silently replace F2 with a content-free approximation.
The configured AMHE1 Proving Ground is loaded with two Samus players and a fixed
seed, followed by 120 warmup ticks and 180 captured ticks (420 for respawn). Slot 1 uses scripted
inputs through production controls; slot 0 is the suppressed headless host lane.
Weapon probes must create actual projectiles; movement probes must actually move.
Captured fields include normalized position/velocity, animation, health, RNG,
ammo, charge and bounded projectile identity/origin/velocity/damage parameters.
No proprietary bytes are included. Engine loading diagnostics go to stderr.

The original `weapon.NAME` probes capture firing state. `weapon.NAME.hit` also
requires an actual aimed projectile to reduce the victim's health. The Imperialist
headshot probe requires the production headshot-kill counter to advance. The
missile splash probe requires the first floor impact to apply the distinct splash
damage value rather than direct-hit damage; later hits may become direct as the
victim is knocked away. This is a fixed geometry regression, not exhaustive hitbox coverage.

The five match packs run actual Battle, Survival, Capture, Nodes and Prime Hunter
handlers on the first compatible native room in the stable room catalog. They
capture scoring, objectives, ownership, health, deaths, match state and RNG. Every
pack must reach its specified objective/ownership transitions and match completion.
No overtime scenario invents a rule: this engine has no separate overtime mode.

The final seven alternate-form packs each run 840 ticks after warmup. They require
movement, attack/bomb state, morph/unmorph/remorph, death and respawn; Samus must
release a charged boost and Weavel must create a live turret. Noxus receives a long
held attack and 120-tick transition windows, matching its actual startup and
animation durations. This is deterministic state evidence, not rendered pose or
hit-volume acceptance.

The presentation-rate argument changes clock scheduling without rendering.
All 47 current scenarios matched at 30, 60, 120, 144, 240 and 997 Hz against the
proposed normalized `e80098f4` engine references. The 26 F0/F1 framework checks also
exercise full-width authority epochs, serial match wrap, and ending-state fences.
No baseline regeneration occurs during verification.

Slope and corner probes select actual native collision surfaces deterministically.
The slope must traverse at least ten grounded steps at changing elevation; the
corner must constrain sustained movement. The platform probe selects a native
campaign moving platform (entity layer 0), uses the normal collision/physics loop,
and requires the passive rider to retain its relative position within 2/4096.
Campaign entities are loaded in the isolated Battle fixture; no campaign save is
written. Fixture selection fails if suitable native content is unavailable.

`identity.disconnect-resume` drives production session reset and authoritative
roster/life admission without sockets: stopping must clear identity, and resuming
must reject the old occupant and old life. Actual transport reconnect remains in
the separate real UDP suite; this F1 probe does not claim F4 evidence.
Broader rendered/physical acceptance remains F3–F5; do not infer it from this matrix.
