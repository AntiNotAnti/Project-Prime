# PR109-inspired combat presentation

Base: `0127fab5ffd411529a5c69a40e5467e140f50a60`, protocol 44.
Upstream Fruity PR109: `1756af902f960c82edde28bd31e6895f8bcb8198`, open and conflicting when checked on 2026-10-08 (America/Chicago).

This implementation retains dedicated-server combat authority, existing movement ownership,
claim grace/arbitration, shot/lifecycle identity and replay v1 facts. No release or merge is authorized.

## Coverage audit at the baseline

`PlayerEntity.TakeDamage` creates a fact only after a positive health/turret delta,
with a nonzero damage event, nonzero shot, valid weapon (PowerBeam through Omega),
and current authority/match identity. It does not publish a fact for every kind of damage.

| Source | Baseline evidence and limits |
| --- | --- |
| Normal/charged beams, Imperialist body/head, Omega | Native direct collision calls `TakePlayerDamageAt` with the collision point and sets `EnhancedDirectHit` around the call. Despite its name this flag DOES cover normal direct beams. |
| Splash | `TakePlayerDamageAt` uses the explosion origin. Not a victim body contact; do not derive a body offset from it. |
| Ricochet/multi-pellet, enhanced children | Parent ShotKey retained; no stable pellet/child component ID in the v1 fact. Exact component matching requires new evidence. |
| Shock Coil | Phase-aware positive damage ticks can publish; v1 contains a continuous flag but no tick component. Zero-damage ticks are not facts. |
| Halfturret | Beam source/target flags and turret delta fields exist. Turret-only damage is excluded by the precondition `damage > 0` in some paths. |
| Balanced weapons | Positive native beam deltas use the same final-damage seam; child identity remains a gap. |
| Bombs/Lockjaw, alt contact | Native sources usually have no beam ShotId/weapon; excluded by the fact precondition. Never infer exact projectile identity from proximity. |
| Burn, self/environment | Burn/environment commonly lack a valid beam/shot identity. A beam self-hit can publish when it has a legal identity. Do not advertise general coverage. |
| Rescued claims | Uses independently validated claim witness at final application; body-relative collision pose is not retained in v1. World point must remain the fallback. |

The live receive case only calls `ReplayCapture.AcceptedShotFact`. Authority publication
currently returns when recorder admission fails. The existing reliable replay lane is
background priority; a separate bounded live lane is required.

## Measurement contract

`NetImpactDiagnostics` is opt-in (`-liveimpactdebug`), a fixed 4096-event ring, and has
no simulation-time file I/O. Its join key includes the full ShotKey, victim slot/generation/life,
damage event, resolution tick (wrap discrimination), and component. Component zero means
unknown. Identity is session-local; no account names, endpoint addresses or tickets are logged.
Exports use process-monotonic timestamps; joining files must not subtract clocks from different
processes. `tools/live-impact/join.py` joins exact event keys into JSON/CSV.

Run `tools/live-impact/run-validation.py --output <directory> [--assets <data directory>]`.
A .NET 10 SDK is required. The local installed SDK is at `~/.dotnet/dotnet`; the default PATH
runtime has no SDK. Asset-backed commands take **data directory, then room**, correcting
the handoff examples. Headless synthetic-intent performance is labeled separately from
rendered multiplayer acceptance. Before/after records never imply unexecuted platform tests.

## Slice status

| Slice | Status | Evidence |
| --- | --- | --- |
| 0 Baseline | in progress | coverage audit, diagnostic ring, baseline contracts and headless runs |
| 1 Live transport | pending | |
| 2 Presentation | pending | |
| 3 Fast claims | pending; default remains existing arbitration | |
| 4 Predicted kills | pending; actual remote predicted death remains disabled | |
| 5 Collision performance | pending | |
| 6 Acceptance | pending | Cross-platform rendered campaign requires those environments |
