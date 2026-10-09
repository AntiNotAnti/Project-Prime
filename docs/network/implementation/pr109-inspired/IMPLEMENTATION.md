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

## Slice 1: independent live fact transport

`NetCombatFactPublisher.Publish` receives the single final applied-damage fact. Its
server-only live fanout runs before optional recorder admission. A separate packet
71, protocol 45, carries the unchanged 61-byte replay fact plus version, kind,
component and a 1/4096-unit body offset descriptor (74-byte payload, 98-byte framed
datagram). Unknown offsets are encoded explicitly; claims and splash cannot pretend
to have a certified body offset. No snapshot payload grows.

`-liveimpacts` opts in on server and client. `-noliveimpacts` overrides it. Default is
off. Per ready peer: 64 retained live events, two unreliable attempts three simulation
frames apart, 12-frame sender expiry, at most four transmissions per simulation frame.
The ceiling is 240 datagrams/23,520 framed bytes per second per peer, excluding UDP/IP;
8 peers at saturation add at most 188,160 framed bytes/s. Pressure drops cosmetics;
no reliable retry window, replay history or match state waits for these events.
The receiver reserves admission for gameplay and limits queued cosmetics to 64.

The deterministic matrix uses the production outbox/codec over the fault scheduler:
2/4/8 peers, 0/50/150/250/350ms RTT, 0/40/80ms positive FIFO jitter, 2% loss, 3%
reorder and 1% duplication. It is **not** a symmetric-jitter UDP/rendered campaign.
All 45 scenarios pass >=98% unique delivery at 10 events/s/peer. Saturation explicitly
drops expired cosmetics. `--impact-transport` passes 484 assertions; baseline contracts
and both asset-backed authority checks remain green. See `transport.json` and `slice1/`.

The full engineering script passed network/replay checks then stopped at authored-mip
because this new macOS worktree lacked the pinned native libktx. Building the documented
runtime is the next verification step; this is not reported as a passing engineering gate.

PR chain: slice 0 https://github.com/AntiNotAnti/Project-Prime/pull/410, base main.

## Slice 2: conservative presentation

The live client submits depth-tested weapon-colour endpoint particles through the same
pure drawing helper as Replay Studio. A fixed 256-entry projectile index matches full
ShotKey, weapon and primary projectile ordinal. Native child emissions can reuse spawn
ordinals across calls, so children use an authority-only component (high bit set) and
truthful fallback cues; they are **not** falsely matched by proximity. Rescued claims
also fall back when their client-correlatable component is unknown. This is an explicit
fidelity limitation, not full child correlation coverage.

Native direct contacts capture a quantized offset before the historical victim pose is
restored. Splash, turret, continuous and claims retain the authoritative world point.
The presenter holds missing relays for three simulation frames, displays cues for six,
and expires after 90. Existing matching native impact points suppress duplicate cues;
known direct/splash pairs can share a blast cue. Continuous events are not blast-coalesced.
A backwards, >60-degree, >4-unit or blocked correlation becomes an endpoint-only fallback.
There is no live corrected tracer, synthetic projectile or gameplay transform mutation.
These conservative thresholds are provisional, not empirically tuned recommendations.

`--impact-crossview` currently means 22 **asset-free presentation contracts**, not an
observed multi-camera success rate. Existing replay format/control checks pass after
sharing the drawing helper. The pinned macOS libktx runtime now builds and the remaining
engineering checks are being resumed. Rendered first-person/spectator/high-refresh and
weapon-specific particles/audio remain unverified; default stays off.

Slice 1 PR: https://github.com/AntiNotAnti/Project-Prime/pull/411 (depends on #410).
