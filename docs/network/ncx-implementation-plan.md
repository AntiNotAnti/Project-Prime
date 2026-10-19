# Project Prime: Competitive Netcode Overhaul

**Implementation plan for an AI coding agent**  
**Research snapshot:** 2026-10-19  
**Primary repository:** [`AntiNotAnti/Project-Prime`](https://github.com/AntiNotAnti/Project-Prime), `main` at `dc7f097067d755ec76d145fc7cc333068566863d` when researched  
**Comparison repository:** [`liveteklol/Fruity-Prime`](https://github.com/liveteklol/Fruity-Prime), `master` at `12ff677b9700bc8dabde37eb9a3fdd10c62b87b6` when researched  
**Status:** Proposed work. **No PRs have been created or code changed by this plan.** Refresh both branch heads before implementation.

## 1. Mission and non-negotiable scope

Improve Project Prime's multiplayer netcode enough to **demonstrably outperform the current Fruity Prime native netcode** in a controlled, repeatable, matched-gameplay comparison. Optimize competitive hit registration, frame-accurate headshots, perceived responsiveness, shooter/victim/observer visual agreement, server efficiency, packet-loss recovery, and authoritative combat correctness. Pursue better consistency and lower tail latency without sacrificing safety.

**Absolutely out of scope:** implementing or enabling **server-authoritative player movement**. Do **not** make the server independently simulate, reconstruct, correct, reject, or override ordinary player movement. Maintain the existing owner-reported movement, immediate local camera/aim/movement, and Source-style movement integration. Do not change player movement physics as a disguised networking optimization. Existing advisory movement diagnostics may remain intact but must not become an enforcement path.

The dedicated server **must remain authoritative** for damage, health, deaths, scores, effects, pickups/powerups, objectives, match transitions, and combat settlement. A shooter-authored hit is a **claim**, never unquestioned truth. Remote deaths require server confirmation; predicted remote lethal damage may visually stop at 1 HP. Never grant special authority to the creator, host, or lowest-latency player.

Do not rewrite the whole networking system, port Fruity's C++ netcode wholesale, duplicate the existing authority ledgers, switch languages or rendering backends, or redesign replay formats without an explicit migration. Preserve 1v1 vanilla compatibility/rule toggles, multiplayer map identities, existing replay compatibility, the Community Hub transfer paths, Android, and the current RmlUi/Studio work. Support custom weapons through explicit policy extensions rather than hard-coded assumptions that all weapons behave alike.

### Security boundary that cannot be removed without movement authority

The server can authenticate the packet/session, weapon inventory, legal shot timing, fire-event identity, historical *reported* target position, geometric path, and admissible damage. It **cannot prove that a player's owner-reported position was physically reachable** without separately authoritative movement. Consequently, this plan improves combat integrity and detects forged combat events, but **must not claim to prevent all teleport, speed, noclip, or origin-spoof attacks**. Nonfinite/oversized packet rejection and bounded admission are transport hygiene, not a substitute for movement authority.

## 2. Verified codebase baseline and competitive gap

### Project Prime: reuse these implementations

- **Live protocol**: `NetConfig.ProtocolVersion = 57` at the captured head. The protocol number is **not** a netcode-quality score; do not compare it with Fruity's numeric version.
- `NetUnlagged.cs`: **128-frame** historical ring and **45-frame/750 ms** default rewind ceiling, with sub-frame ACK and projectile catch-up. Do not simply increase rewind limits, because the victim's cover/peek fairness deteriorates.
- `NetHitClaims.cs`: historical geometry, owner claim rescue, life/generation/stream fencing, fire-frame-order arbitration, duplicate prevention. **Standard pending grace is adaptive, 24–72 frames** (`GraceFor`). Authority-paid claims can be answered earlier. It is incorrect to say *every hit* waits the full grace period.
- `NetHitPrediction.cs`: instant local hit prediction and existing authoritative remote-death rule, already clamps predicted lethal remote damage to **1 HP**. Do not implement that a second time.
- `NetFireEvents.cs`, `NetIntentHistory.cs`, `NetShotInputProof.cs`, `NetShotSourceJournal.cs`: exact fire identity and pose, repeated bounded fire-event histories and source checking. Avoid introducing a redundant firing-event protocol.
- `NetCombatPayments.cs`, `AcceptedShotLedger.cs`, `NetContinuousPayments.cs`: authoritative payment identity, lifecycle tombstones and duplicate suppression. **Extend these**, do not add parallel damage accounting.
- `NetShotProof.cs`: source/geometry/physical-witness evidence; `PRIME_SHOT_PROOF` defaults to **Shadow**. Some weapon families are explicitly `Unavailable`. Experimental `Enforce` is not production-certified. Do not switch it on globally.
- `NetTimedShotProof.cs`, `NetTimedShotModifiers.cs`, grant/pickup ledgers: authority-source and timed-powerup evidence; many stronger enforcement cases remain limited to Shadow or narrow families.
- `NetConfirmedImpacts.cs`, `ConfirmedImpact.cs`, `NetImpactTransport.cs`: **53-byte, full-key confirmed-impact fact** and separate cosmetic traffic. `PRIME_CONFIRMED_IMPACTS=1` is **opt-in**, off by default. The existing code explicitly warns that draw submissions are not proof of visible pixels.
- `WeaponLagPolicy.cs`, `NetDynamicGeometryHistory.cs`, `NetHistoricalTrace.cs`, `NetNativeImpactWitness.cs`: reuse policy, moving-obstacle and native collision evidence.
- `NetTransport.cs`, `NetPacketQueue.cs`, `NetReliableChannel.cs`, `NetReplicationLanes.cs`: bounded UDP, separated data lanes, retries, status counters, fault injection.
- `NetPerformanceEvidence.cs`, `NetShotDiagnostics.cs`, `NetCampaignEvidence.cs`: reuse private instrumentation and source-qualified evidence.
- Existing testing: `tools/nettest`, `tools/nettest/run-net2.py`, `run-net13-matched.py`, `NetcodePerformanceTests.cs`, `ClaimStressTests.cs`, `CombatArbitrationTests.cs`, `ConfirmedImpactTests.cs`, `ImpactAuthorityTests.cs`, `ContinuousCombatTests.cs`, `LagCompensationTests.cs`, `NetworkAllocationTests.cs`, `Replay*`, etc.

**Known qualification gaps:** `docs/netcode2-n13.md` records inconclusive matched p99 timing and incomplete real-UDP settlement coverage. `docs/netcode2-n14.md` records short successful canaries, but **not** a finished external-host release, long rendered hit-fidelity study, or cross-platform production acceptance. Treat historical PASS and FAIL evidence by exact commit, binary and workload; do not upgrade a prior result just because a later test passes.

### Fruity Prime: match or improve its validated strengths

- Latest native code is **protocol 34**, not the historic C# implementation.
- Shooter-first hit claims for eligible human-vs-human shots, with **zero arbitration grace for that class**; the dedicated server checks history, weapon/damage limits, exact shot events, fired rays, line of sight, rate, collision plausibility, and then owns damage/deaths. Server replay remains for special cases and bots. `-servershots` is a control arm.
- Confirmed landing points and same-shot reconstruction for victim and observers. In the historical, real-network PR #109 report at approximately 250 ms, reported same-shot victim visibility improved to **94% (Wells), 95% (Still), 94% (Lanes)**; head-band agreement **99–100%**; same-shot observer visibility approximately **93%**. These are **Fruity before/after**, **not** Prime-vs-Fruity measurements.
- In the specific #109 CPU A/B, avoiding redundant human-shot replay reduced the observed *worst* simulation step from **6.7 to 1.9 ms**. Do not treat that as comparable to Prime's independently measured server CPU on another machine.
- PR #116 fixes canonical shot events, turret and bomb synchronization and claim/shot identity under loss. PR #126 reinforces powerup grants, damage bounds, compact optional intent sections and reliable settlement. PR #204 prevents old authoritative morph transforms from rewinding the owner's local animation. PR #205 (merged 2026-10-19) enforces server-authoritative remote deaths; PR #206 fixes hit-claim timeout diagnostics. These later PRs must be included in the comparison; old PR #109 predicted-kill behavior is no longer the latest policy.

**Never presume Fruity is universally faster or better without paired tests.** Its shooter-first approach trades server CPU and perceived confirmation latency against increased dependence on shooter-authored evidence. Prime can outperform it by keeping verified combat truth while fast-settling safely eligible claims and improving visual agreement and recovery.

## 3. Definition of “surpass Fruity” and success criteria

A claim of superior netcode requires matched, captured-source **head-to-head** evidence, **not** a code-feature count, higher protocol version, cherry-picked debug benchmark, or artificially disabled competitor feature. Different game balance, recoil, collision, hunters, game modes and graphics can confound results. First compare common vanilla-equivalent gameplay with matched test assets and definitions; evaluate Prime-only custom modes and future weapons as additional qualification, not substituted competitor results.

**Competitive targets (proposed release gates, not achieved measurements):**

| Area | Metric and initial exit criterion | Comparison discipline |
|---|---|---|
| Competitive real-hit fidelity | >= **99.5%** authority adjudication of independently labeled legal hits on clean links; >= **98.5%** under moderate impairment, with no false payment | Count offered legal shots / landed legal contact / server verdict separately; no denominator swapping |
| Same-shot victim rendering | >= **96%** in each Wells/Still/Lanes-type moving-target arm at ~250 ms; match/reproduce Fruity test semantics | Rendered shot attribution, not just packets received or draw submitted |
| Same-shot third-party observer | >= **95%** under same conditions | Test victim, shooter, and spectator jointly; no unrelated projectile substitution |
| Headshot/body precision | >= **99.5%** head-band consistency for supported exact historical contact cases; **zero** fabricated head bonuses in adversarial fixtures | Report actual-head, reported-head, applied-head, rejected/unknown separately |
| Feedback and final settlement | Immediate local feedback; eligible fully verified, dependency-ready claims settle within **2 server ticks at p95 after sufficient evidence arrives** | Separately publish input-to-client-feedback, server-arrival-to-verdict, and shot-to-confirmation latency. No promise to beat physics/RTT. |
| Competitive kills/trades | **Zero** false remote deaths, resurrection artifacts, double kills from a single payment, double payment, unauthorized score changes, or arrival-order trade regression across required fixtures | Distinguish expected same-world trade from incorrect duplicate death |
| Network resilience | Zero unexpected disconnects, stuck rooms, dropped critical verdicts or incomplete drain in specified 2/4/8-player acceptance scenarios; all intentional timeouts classified | Include 0/2/5% packet loss, jitter, reorder, duplication and long stalls |
| Server cost | No statistically credible >5% p99 regression in equivalent Prime-vs-Prime controls; target >= **15% reduction** in p99 authority combat CPU for heavy human-projectile cases when safe replay suppression is enabled | Same hardware, OS, release runtime, assets, loads; A/A control and paired order. Record p95/p99/p99.9, GC, allocations and max step. |
| UDP/MTU | No routine datagram fragmentation; preserve current gameplay **<=1200-byte** target, prioritize critical receipts, measure bytes/s, retries and loss | Any new wire bytes must be accounted for by reliable budget and 8-player worst case |
| Supremacy claim | Meet all Prime safety gates **and** beat Fruity in at least **three independently matched key metrics** with a predeclared paired analysis; no severe category regression | Publish both raw run manifests and confidence intervals; otherwise report competitive parity or unverified |

The ambitious target metrics above are engineering goals. **Fail / incomplete / not-run must not be reported as PASS**. Head-band and hit thresholds require meaningful sample counts and uncertainty intervals; a 100% result from six events proves little. Where 99.5% is unachievable for a mechanically unsupported projectile, explicitly identify and retain its separate safe fallback rather than falsifying coverage.

## 4. Architecture to implement

### Authority roles

```text
CLIENT (movement owner)
  - source inputs; local motion/camera immediately, unchanged
  - predicts outgoing shot/hit feedback; does NOT own remote death
  - sends existing keyed FireEvents, exact authored fire pose, hit candidates
                 |
                 v
SERVER (sole combat and match authority)
  - verifies connection, generation/life, source/weapon, causal time and aim
  - checks historical *reported* victim and world geometry
  - runs weapon-family collision/damage/effect eligibility policy
  - resolves authoritative payment exactly once by existing ShotPaymentKey
  - sends terminal CombatAck, snapshot, authoritative impact facts
                 |
                 v
CLIENTS / OBSERVERS
  - reconcile exact hit/health/score/death
  - render *the confirmed shot* at its correct time and landing point
  - never turn a visual cue into additional gameplay damage
```

### Fast combat settlement states

Add a **small, bounded decision layer** inside the existing claim/authority pipeline (name example: `NetCombatFastPath`, not a second independent netcode):

- `NoCandidate`: no legitimate authored shot or verified victim contact.
- `PendingDependencies`: awaiting the exact accepted source event, retained history, active-life evidence, a missing earlier fire event, authoritative pickup fact, or an ordering barrier.
- `VerifiedNative`: the server already produced an exact physical payment/witness, so reply from the existing ledger immediately without re-paying.
- `VerifiedHistorical`: source/fire/weapon/state and exact historical legal hit have independent native-equivalent evidence; safe fast-settlement candidate. **A ray close to a claimed point alone is not enough to assert collision or the claimed damage.**
- `LegacyRequired`: unsupported ballistic/homing/ricochet/splash/effect/alt/turret configuration, ambiguous obstacle, incomplete or clamped history, unknown custom weapon, or insufficient causal certainty. Use the existing authoritative simulation/rescue path; never automatically accept a speculative claim.
- `Reject`: impossible source/identity, illegal fire rate/weapon/charge, absent legal history, impossible impact/headshot, blocked shot, forged effects or duplicate payment.
- `Settled`: one authoritative payment (or an exact previously paid result) with one stable, retriable terminal outcome.

The decision layer should return **eligibility, reasons, and existing payment identity**, without calling damage directly. Only one existing payment owner may mutate health/score/effects. Add bounded result/telemetry counters, no per-hit heap allocation or unbounded logs. Preserve stream/match/slot generation/owner life, victim generation/life, `ShotId`, component ID, continuous phase, and damage event sequence across every path.

### Fundamental ordering rule

**Do not globally replace Prime's 24–72-frame grace with 0.** A blanket change could lose earlier fire-world shots under reordering and break trade-kill causality. Bypass grace only when its *purpose* has already been met: sufficient source-event/ordering frontier, no unresolved earlier applicable payment, and exact duplicate-payment guarantees. Nonlethal supported cases may qualify sooner; lethal, multi-hit and posthumous cases may need a short independent causal barrier. If early finalization would change the current earlier-world/strictly-prior-fire trade rule, retain legacy handling until parity is proven. Cross-shooter ordering, missing historical inputs, and rollbacks need explicit tests.

### Configuration and rollback

- Keep the existing `PRIME_SHOT_PROOF`, `PRIME_CONFIRMED_IMPACTS`, and their current defaults initially.
- **Proposed new setting only, not currently implemented:** `PRIME_COMBAT_FASTPATH=off|shadow|eligible` with `off` default initially; `shadow` computes decisions and compares against default but never changes payments; `eligible` commits only certified policy families. Do not add an unbounded `all` trust mode.
- Provide a server-side fast disable that reverts to existing combat handling without altering movement. If a wire change requires a protocol bump, require exact matched client/server, fail closed at handshake, and version replay compatibility explicitly.
- Keep old code paths only as controlled comparison/rollback, not as duplicate permanent implementations; delete redundant branches after release evidence and explicit migration.

## 5. Ordered implementation PRs

Each PR must stand alone, add tests for its own behavior, be reviewable, be committed, and have a written source-qualified validation manifest. Do not silently merge stacked PRs or enable any new default without the prior gate. Prefer PR IDs **NCX-00 through NCX-13** (working labels, not existing GitHub PR numbers).

### NCX-00 — Fresh audit, freeze baseline, and enforce scope

**Priority: P0.** Before editing, refresh both repo heads, read all open Prime PRs (including UI/Studio PRs) and list overlapping network work. Map packet flow from authored fire input to server physical payment, claim rescue, damage event, CombatAck, prediction retirement, spectator display and replay. Enumerate actual default flags from runtime source, not historic docs. Catalogue each supported weapon and source/effect family including Balanced/Instagib variants, Omega, Imperialist, Shock Coil, Magmaul burn, Judicator ricochet, homing Missile/Volt, Battlehammer, alt forms, bombs, turrets and future custom sniper. Record all missing or inconsistent evidence and existing CI warnings. Capture build/runtime hashes and goldens.

**Deliverables:** `docs/network/ncx-baseline.md`, `tools/nettest/baselines/ncx-baseline.json`, ownership diagram, exact-head CI results. **Exit:** zero undocumented movement ownership changes; passing reference code; characterized failures retained.

### NCX-01 — Reproducible matched combat measurement and real visual rig

**Priority: P0.** Extend `run-net2.py`/`NetCampaignEvidence` and existing test harness rather than create a second controller. Introduce a keyed, privacy-safe shot timeline with timestamps/frames: authored, sent, received, accepted, collided, claimed, payment attempted, payment committed/refused, terminal delivered, shooter feedback, victim render, observer render, replay captured. Explicitly distinguish timestamp domains; align peer clocks via existing source ACK/sub-frame metadata and measured offsets. Separate uniquely authored shots, resent claims, adjudicated collisions, hits, lethal events, server-paid facts and visible pixels.

Create comparative fixtures emulating Fruity's stationary, lateral and jump-pad Wells scenarios; matched stock rooms and exact hunter/weapon sets; fixed seeds, 30/60/120/144/240/360+ Hz presentation observations. Acquire actual screenshots or framebuffer readbacks for selected real clients and an observer; avoid using a headless draw-submission counter as proof of visual equality. Build independent expected-hit oracle on frozen and replayed collision data without trusting the contested client hit verdict. Include pre-existing failed N13 live settlement coverage as an explicit remediation item.

**Exit:** baseline of both binaries and controls, honest denominators, reproducible exporter, visual test actually exercises positive shots/hits, A/A variance measured, no added gameplay changes.

### NCX-02 — Canonical accepted-shot causal ledger and definitive terminal results

**Priority: P0/P1.** Strengthen `NetCombatPayments`, `AcceptedShotLedger`, `NetHitClaims`, `NetFireEvents`, `NetHitPrediction` through one canonical `ShotPaymentKey` and life/epoch provenance. Make stable terminal receipts mandatory for accepted, duplicate, refused, expired, canceled, capacity-exhausted and missing-history claims. Distinguish accepted claim from actual authoritative health payment and confirmed kill. Avoid accidentally marking a source-only accepted event as a paid hit.

Retain earlier-world fire-order arbitration, 1 HP remote-death prediction, score-from-snapshot, spectator lifecycle, posthumous legal shots and same-world trades. Add reordering tests in both arrival orders, duplicate claims before/after authoritative payment, simultaneous splash/components, source respawn, victim respawn, map rotation, late kill ACKs, `ShotId` wrap, and connection incarnation changes. No speculative remote corpse or point inflation.

**Exit:** one and only one damage/effect payment per correct key; all terminal outcomes eventually accounted or explicitly timed out; prior arbitration parity.

### NCX-03 — Shadow-mode verified shooter-first fast path

**Priority: P1; highest payoff.** Introduce the bounded fast-decision function described above. Read exact `NetFireEvents` and source pose from existing retained histories; do not trust a packet's claimed damage, charge, grant, pose, head band or impulse simply because it is well formed. Verify shot source/weapon/variant/fire interval, ownership/life, ammo/charge, Prime/Double funding, source and target at the relevant frame, full ray (including reticle/aim where used), dynamic world/forcefield LOS and exact hit/target geometry. Use native collision proof when available. Reuse `NetTimedShotProof` and dynamic geometry evidence, including explicit Unknown statuses. Preserve world/physics effects and native authoritative shot simulation while Shadow runs.

Generate structured `fastEligible`, `fastWouldPay`, `fastWouldReject`, `missingSource`, `headMismatch`, `wallMismatch`, `orderingDeferred`, `familyUnsupported`, `wouldDoublePay`, and `wouldAlterTrade` counters. Compare *per-shot* to actual legacy outcomes over local, impaired, eight-player and adversarial tests. Refuse to promote a family with unsupported mechanics or disagreements until investigated.

**Exit:** zero unauthorized would-pay, zero predicted double-payment and zero unexplained parity differences in certified subsets; `shadow` may stay deployed with old behavior indefinitely.

### NCX-04 — Enable verified fast settlement for narrow safe classes

**Priority: P1.** Begin with already authoritative-paid duplicate receipts and supported, deterministic single-component direct hits (candidate families: non-ricochet Power Beam and Imperialist, after authoritative native-equivalent historical tracing). **Selection is subject to proof from NCX-03**, not a blanket weapon-name exemption. Correct head/body mapping from native historical hitbox geometry; if exact head proof unavailable, do not elevate an unverified body impact into head damage. Do not let a client-declared endpoint define wall reach or splash falloff.

When evidence is sufficient and no earlier causal dependency remains, settle through existing payment reservation and `CombatAck` as soon as possible, ideally next tick. Otherwise fall back to pending historical/native handling. Continue collecting actual authority events for replay and visuals even when no redundant combat replay is required. Keep server authoritative over victim life, health, status and scores.

**Exit:** fully verified eligible examples p95 <=2 authority ticks after all required evidence arrives, no change to honest hit totals, head classification, trades, self-damage, bots, or scores; measured comparison against default arm.

### NCX-05 — Extend exact weapon-family proof, cautiously

**Priority: P1/P2.** Build a capability table on existing `WeaponLagPolicies` and native weapon facts, not a giant weapon enum switch. Requirements per family:

- **Straight/direct:** original source/ray, historical collision order and dynamic LOS, native direct point, exact head-band and life.
- **Ballistic projectile / Battlehammer / Omega:** originating shot, speed, gravity, swept trajectory and collision time. If no historical continuous flight witness exists, do not fast-accept simply from endpoint distance.
- **Homing Missile / charged Volt:** affinity/variant and fire-time target-generation/life selection; validate lawful curve/decision rather than demanding a straight ray from muzzle to victim.
- **Ricochet Judicator:** concrete ancestor/child shot identity, bounce surface, lineage and exact ricochet path; no simplistic straight-line rule.
- **Splash / Magmaul burn / status effects:** independent radius/occlusion and source-life timed-grant proof; control burn/freeze/disrupt on server and constrain late effects to actual witness. No fabricated double damage.
- **Shock Coil:** authoritative pulse/continuous phase, independent target identity, tick-specific payment and capped healing, no generic rescue duplicates.
- **Weavel turret / bombs / alt attacks:** owner and spawned-entity identity, authoritative birth/retirement, pre/post-morph life and collision, exact component payments; deterministic fallbacks for unsupported paths.
- **Future custom sniper/custom weapons:** registered immutable weapon definition, authenticated server-accepted asset ID, firing/charge/cooldown/ray/penetration/headshot rules, versioned policy. Unrecognized/mismatched custom weapons must be denied or routed to explicit server-native fallback, not awarded client-declared damage.

Promote each independently tested family from `Unsupported/Shadow` to `Eligible`; never globally toggle ambiguous families on because most ordinary bullets passed.

**Exit:** per-family effect, headshot and timing equivalence to the original simulation; all unsupported families explicitly documented and safe.

### NCX-06 — Fast, correct prediction and confirmation presentation

**Priority: P1.** Keep instant local recoil, hit marker, flinch, audio and tentative health, with exact-key pending predictions. Accepting a claim must not imply a kill: only authoritative death/score changes cause remote death animation, kill feed and killcam. Implement definitive terminal outcome retirement for paid/duplicate/refused/expired, including health projection without flicker or indefinite ghost debits. Preserve self-death prediction and bot outcomes. Render camera/mouse aim at local refresh rate while keeping simulation 60 Hz and source shot pose exact; avoid old authoritative transforms rewinding owner morph clips (Fruity's PR #204 is a known comparator).

**Exit:** no fake kill/resurrection, health yo-yo or mismatched killcam; identical world/combat hashes at 60/120/144/240/360+ FPS; local movement unchanged.

### NCX-07 — Confirmed-impact visual parity for shooter, victim and observers

**Priority: P1.** Upgrade the **existing** `NetConfirmedImpacts` 53-byte cosmetic fact instead of inventing another damage carrier. Ensure events refer to the exact `ShotKey`, component/phase, damage-event ID, victim generation/life and weapon. Include truthful source (`NativePoint`, `AcceptedClaimPoint`, `ConfirmedBodyAnchor`), head/body and native effect variant. Match a confirmed event only to the uniquely corresponding visible projectile; wait briefly for a late visual entity, and synthesize the correct impact when the projectile is gone. Do not bend a projectile through hard cover or rotate it implausibly; if association or sightline is ambiguous, show only a standalone verified contact cue or no effect. A blast is one impact; no two cosmetic explosions for one payment. Continuous Shock Coil beam needs a phase/time-specific indicator, not synthetic projectiles. Support actual 3rd-party observer views and spectators.

Retain the isolated **cosmetic queue and non-gameplay ACK path**. Never allow cosmetic packets to affect damage, reliable ACK, RTT, simulation RNG, replays, hitboxes, or player input. Run actual rendered framebuffer and video correlation, not only `CueDrawSubmissions`; verify loss and 8-player effects. Turn `PRIME_CONFIRMED_IMPACTS` on only when acceptance metrics and bandwidth budgets pass.

**Exit:** >=96% same-shot victim and >=95% third-observer view in controlled 250 ms cases, independently measured; zero unconfirmed impact portrayed as actual damage, zero cross-shot association.

### NCX-08 — Continuous fire, multi-entity and death-boundary hardening

**Priority: P1, regardless of whether enabled for fast path.** Reproduce the historical Shock Coil-related disconnects/timeouts, including kills during continuous target selection and spawn traps. Audit input phase zero, source-tick wrap, target generation/life, server-paid tick, ammo/healing ramp, duplicated/malformed continuous claims, burst release near room load, collision against teammate/turret, and posthumous effects. Exercise Weavel alternate forms/turret retirement, morph mid-flight, multi-projectile burst Battlehammer, Omega shot ownership, live bots, spectator admission, late joins and player self-destruction. Never accept a generic rescue when a native continuous tick has already paid.

**Exit:** zero double continuous payment, negative ammo, target stealing from retired life, crashes, idle timeout under configured impairment and stale-match kill; exact terminal accounting.

### NCX-09 — Transport, loss/reorder, queue fairness and connection recovery

**Priority: P1/P2.** Profile current `NetTransport`, `NetReliableChannel`, `NetPacketQueue` and packet budgets before changing them. Keep snapshot-fast replaceable; source shot events and combat verdicts reliable/accountable; cosmetics expendable and isolated. Improve age-aware admission and selective retransmission where measured, cap per-client memory/work, guard against redundant retries and ACK poisoning, and handle transport receive stalls on synchronous loading. Classify packet loss vs missed authoring vs accepted but unpaid vs terminal unacknowledged. Compare intent event redundancy rates before increasing bandwidth. Preserve existing stream/connection-incarnation checks, endpoint-binding policy and bounded MTU.

Test jitter, 5% packet loss, 3% reorder, 1% duplicate, one-client 400 ms stutter, server restart/new connection ID, 8-player long matches, map rotation, team change, join in progress, server-hosted lobby closure, Android sleep/resume, launcher reconnect, and no renderer/device conditions. Reproduce and fix an actual fault before introducing speculative new queueing abstractions.

**Exit:** no unexpected disconnect/timeout in required runs, zero loss of critical accepted-event receipts after bounded recovery, no long-growing queue or unbounded memory; healthy throughput/RTT and p99 unchanged or better.

### NCX-10 — Historical rewind/collision accuracy and fairness

**Priority: P2.** Use existing fractional ACK and dynamic geometry history to test moving targets, jump pads, cover peeks, teleporters, force fields, doors, moving surfaces, biped/alt collision volumes and historical turrets. Record actual requested vs served rewind, clamping and unknown history. Do not indiscriminately increase beyond the current 45-frame default: additional rewind increases how long victims can be hit after cover. Preserve a per-weapon fallback and shadow plausible-depth diagnostics, with transparent refusal when the independent historical record is missing. Fix observed origin/ray mismatches and image-to-hitbox alignment, without adding movement reconstruction or hard validation.

**Exit:** improved head/body agreement and transparent compensation bounds, no false occluded hits, no cross-life historical contamination; default rewind changes require victim-fairness A/B proof.

### NCX-11 — Server CPU, memory and packet-size optimization

**Priority: P2, only after correct fast-path pay results exist.** Profile source-qualified hot paths: projectile catch-up work, native collision/history copies, geometry restore, `NetHitClaims` pending scans, duplicate payment lookup, snapshot serialization, reliable queue, allocations, `NetPerformanceEvidence`, and 8-player bandwidth. Where safe, avoid redundant *human-owned combat damage* resimulation **only for verified fast-pay families**, retaining necessary world projectile visuals and canonical replay events. Never suppress bot/world/self damage or an unverified ballistic flight. Reuse fixed storage/caches only with robust life/match/frame tokens. Avoid lock decomposition or broad pooling without workload evidence. Retain the current simulator and no OpenGL dependency.

Run real UDP, asset-backed headless server and rendered client separately. Paired alternating A/B and identical-binary A/A: 2/4/8 players, unchanged room, actual active roster, exact shot/claim rates, stable CPU frequency/governor, warmup, minimum thousands of measured steps; measure mean/p95/p99/p99.9/worst, allocations, GC, queue drops, bytes/sec, network recoveries, RSS. Do not claim speedups from synthetic runs without claim activity. Rerun against target **Linux dedicated-server hardware** and Windows, then Android device acceptance for client-side costs.

**Exit:** credible >=15% p99 reduction on replay-heavy cases without regressions >5% on other representative scenarios and without sacrificing collision, combat or replay parity. If no credible improvement, ship correctness only and report failure of performance goal.

### NCX-12 — Competitive head-to-head matrix and cross-platform qualification

**Priority: P0 release gate.** Freeze a production candidate and a Fruity master/release candidate from their exact tested heads. Use a common input/observer harness and source-specific adapters, same host type, compatible collision fixtures, timing distribution, mode, hunter, weapon, pickup rules and aim scripts. Mark any mechanically non-equivalent fixture **non-comparable**. Alternate run order, run A/A noise checks, retain raw packet/shot/video artifacts and exact hashes. Separate synthetic-netem simulation, true UDP loopback and independently networked geographic tests. Include Japan/intercontinental conditions where feasible.

**Required matrix:** 2/4/8 players; 0, 50, 150, 250, 320, 400 ms RTT; 0/20/40/80 ms jitter; 0/2/5% loss; 0/3% reorder; 0/1% duplicate; ordinary aiming, jump-pad vertical, lateral strafe, straight/homing/ricochet/continuous/splash, charged headshots, player death during shot, spectator, bot mixes, alt forms/turrets/bombs and live rotation. A reduced declared smoke matrix may run on every PR; the full matrix is a release gate. Run adversarial fake-hit, spoofed lifecycle, forged charge/grant, LOS and rate tests; no forged authoritative damage from these known invalid cases.

Ensure platform/build/test coverage: Windows, Linux, Android arm64 and emulator *plus a physical device*; high-refresh OpenGL clients; Linux VPS with 8-player long soaks; relevant Replay Studio/killcam/campaign no-game-network gates; public hosting/reconnect and RmlUi lobby integration. Require no new regressions in updater, Community maps, 1v1 toggles or custom maps.

**Exit:** signed source/test manifest; publish quantified head-to-head metrics with confidence intervals, per-hunter/weapon breakdown, video/shot event evidence, PASS/FAIL/INCOMPLETE and remaining limits. Do not declare superiority unless Section 3's criterion actually passes.

### NCX-13 — Controlled rollout and rollback

**Priority: P0.** Confirm all feature flags and runtime defaults; preserve baseline mode. If no new packet fields, document same-protocol behavior compatibility, but still run mixed-binary semantic checks; **bump protocol when semantics/layout require matching clients**. Stage an isolated matching server plus opt-in client canary, then progressively larger private matches, then public rollout. Preserve precise package/build ID, map identities, match protocol, server logs and replay version. Never bypass protocol refusal to get a rollout working. Operators must be able to revert fast settlement, verified-impact cues and family policy independently without enabling client movement authority.

Cut over only after strong visual tests, server fairness, long soaks and Windows/Linux/Android evidence. Freeze and rollback both server and client artifacts together for a breaking protocol. Record regressions and reproductions before reverting; do not erase failed evidence.

**Exit:** release playbook, tested rollback, test artifacts, unresolved limitations and no production blocker.

## 6. Required adversarial and gameplay test inventory

These are minimum distinct fixtures, not a license to waive the existing broader suites:

1. Same shot resent 20 times, reordered across several source frames: exactly one authoritative component payment and one stable terminal disposition.
2. Claims arriving before fire event, after fire event, after shooter death, after victim death, during respawn, after room rotation, after new connection incarnation, across 32-bit fire clock wrap.
3. Two shooters kill in the same acknowledged world, strictly earlier/later fire worlds, both packet-arrival orders; trade and scoreboard must match declared policy.
4. Legal Power Beam body/head, scoped Imperialist body/head, alt-form bodies and turret body, head exactly near boundary, false head point near body, obstruction between muzzle and hit, force field opens/closes inside the rewind window.
5. Missile and charged Volt curved homing, Omega long flight, Judicator reflection, Magmaul burn and splash, Battlehammer custom burst and ricochet/children; distinguish supported exact history vs explicit fallback.
6. Shock Coil pulse zero, held cycles, target retarget, negative/overflow pulse, ammo/heal cap, duplicated malformed pulse, late tick after life retirement, 5% loss during kill.
7. Claimed Double Damage/Prime/Death Alt before versus after source grant or expiry, stale pickup contact, unsupported effect restoration; server grants and consumed resources remain authoritative.
8. Local prediction on high-refresh client versus authoritative snap, remote body stays alive until confirmed, kill feed exact, replays/killcam use the same settled facts.
9. Victim/observer sees *matching* projectile/contact in a video capture; no fake ghost hit, no through-wall cosmetic bend, no duplicate impact, no lagging spectator 1-shot mixup.
10. Corrected render framerate from 60 through 360+ while fixed simulation state and shot origin/aim remain consistent.
11. 8-player normal and loss/stall campaigns on ordinary, custom maps and long matches, server restart, join/leave, reconnect and rotation, with nil unexpected timeouts.
12. Malicious client sends forged victim, identity, charge, source ray, damage and headshot, old life, invalid finite values, malformed datagrams and packet floods: zero proven forged damage; state/memory remain bounded. Explicitly do not report protection against owner-position fabrication.

## 7. Build/CI commands and evidence discipline

At minimum, discover accepted command-line routes from the **current** `tools/nettest/Program.cs` and `run-net2.py --help` before execution. The following routes are documented in the source tree and should be used or updated as applicable:

```bash
# Compilation, tests and targeted benchmarks from repository root

dotnet build src/MphRead/MphRead.csproj -c Release

dotnet run --project tools/nettest -- --netcode-performance

dotnet run --project tools/nettest -- --transport-stress

dotnet run --project tools/nettest -- --queue-budget

dotnet run --project tools/nettest -- --claim-stress

dotnet run --project tools/nettest -- --network-benchmark-json network.json --extended

# Discover the exact campaign syntax and available options
python3 tools/nettest/run-net2.py --help
python3 tools/nettest/run-net13-matched.py --help

# Only execute a real asset-backed campaign with verified ROM-derived game
# assets the user is authorized to use and current documented options.
```

Each PR requires: code SHA, build assembly hash, tested game assets/maps hash, SDK/OS/hardware identity, protocol, exact flags, warmup/measurement definitions, seed, active players and hunters, actual shots/hits/claims/payments, process exits, crashed/stalled clients, raw logger artifacts, tests performed, and classified `PASS`/`FAIL`/`INCOMPLETE`/`NOT_RUN` per criterion. Keep historical failures visible. Only compare metrics from equivalent committed sources and identical work; ensure any baseline manifest is captured **before** code changes.

At merge time, use exact final-head hosted CI rather than trusting checks from an outdated pushed commit. Follow repository merge policy; no automatic merge of a failing PR. For source changes impacting protocol, provide compatibility and replay conformance tests. When one PR modifies the branch baseline, update dependent PRs explicitly rather than silently treating stacked test results as merged-main proof.

## 8. Suggested milestone checkpoints

| Milestone | PRs | What is delivered | Ship decision |
|---|---|---|---|
| Baseline and causality | NCX-00–02 | Exact measurement, no duplicate payments, reliable terminal semantics | Safe foundations; gameplay defaults unchanged |
| Faster validated combat | NCX-03–05 | Shadow then gated fast-pay on proven families | Enable only verified family subset after sound fairness evidence |
| Competitive visual consistency | NCX-06–08 | Correct prediction, rendered confirmed shots and special-case reliability | Enable visuals after actual same-shot video gate |
| Performance and network resilience | NCX-09–11 | Fewer faults, responsible server CPU savings | No performance claim without matched realistic workloads |
| Qualification / release | NCX-12–13 | Comparative proof, Linux/Windows/Android/real hardware and rollback | Declare superior only when head-to-head criteria are met |

## 9. Stop-ship and rollback conditions

Immediately disable the new fast family or revert the last independently scoped PR if any of these are observed:

- Damage paid twice, health/score divergence, forged hit accepted, or a shot applied without its required accepted source/physical-historical proof.
- A legal hit lost because its fire event arrived after claim or death; a changed same-world trade result without deliberate fully reviewed rule migration.
- New 1 HP prediction corruption, fake remote corpse/resurrection, off-by-one headshot, visible fake impacts or cross-player shot attribution.
- More connection timeouts, map rotation deadlocks, stale match replay packets, critical ACK starvation or unbounded queues.
- Movement correction, independent movement simulation, movement validation enforcement, Source movement timing changes, local camera lag, or changed owner movement authority.
- Protocol mismatch allowed silently, historical replay broken, or failing Android/Linux/Windows artifact gates.
- Missing gameplay settlement telemetry mistaken for clean results, non-equivalent benchmark workload, or statistically inconclusive p99 presented as an improvement.

**On rollback:** retain immutable source/test manifest and failure artifacts; set proposed `PRIME_COMBAT_FASTPATH=off`, restore existing `PRIME_CONFIRMED_IMPACTS` default where relevant, use matching protocol server/client, preserve existing gameplay defaults, then rerun the original failure reproduction and prior acceptance controls.

## 10. AI agent implementation instructions

> Work in `AntiNotAnti/Project-Prime` at the latest current main. Re-audit the source before each phase because this plan captures the repositories on 2026-10-19. Do not assume documentation from protocol 41–56 describes current protocol 57 without checking code. Compare `liveteklol/Fruity-Prime` native current master, including PRs #109, #116, #126, #204, #205, #206, but do not port code wholesale. Preserve client-owned player movement; never implement server-authoritative movement. Preserve dedicated-server authority over all combat and match consequences. Implement NCX-00 to NCX-13 in order, one independently reviewable PR per slice, with tests, documentation, exact commits, metrics, and a rollback switch. Use original Netcode 2.1 ledgers, fire-event identities, prediction and confirmed-impact systems rather than parallel replacements. Prefer conservative `Unknown/LegacyRequired` to granting an unproven hit. Treat headshot, splash, homing, continuous, turret, alt and custom weapons independently. Do not claim completed performance or superiority without equivalent paired measurements and rendered video evidence. Keep rollout flags off until the scope-specific exit gates pass; never change production defaults merely because unit tests are green. Report exact merges and CI outcomes, not assumed ones.

## 11. Source references (reviewed starting points)

**Project Prime:**

- [Main netcode source](https://github.com/AntiNotAnti/Project-Prime/tree/main/src/MphRead/Mods/Network)
- [Current hit claims](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetHitClaims.cs)
- [Authority payment ledger](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetCombatPayments.cs)
- [Shot proof](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetShotProof.cs)
- [Shot/fire event codec](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetFireEvents.cs)
- [Authoritative 53-byte confirmed impact](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/ConfirmedImpact.cs)
- [Confirmed-impact implementation](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetConfirmedImpacts.cs)
- [Weapon lag policy](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/WeaponLagPolicy.cs)
- [N13 server performance and outstanding issues](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/netcode2-n13.md)
- [N14 release gates and limits](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/netcode2-n14.md)
- [Netcode performance A/B and impairment matrix](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/network/netcode-performance.md)
- [Netcode 2.1 grant and reconciliation acceptance](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/netcode21-a3-reconciliation.md)
- [Movement authority intentionally deferred](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/netcode2-n12.md)

**Fruity Prime (Native C++, not historical C#):**

- [Native netcode source](https://github.com/liveteklol/Fruity-Prime/tree/master/src/MphRead.Native/Mods/Network)
- [Native hit claim verification](https://github.com/liveteklol/Fruity-Prime/blob/master/src/MphRead.Native/Mods/Network/Gameplay/Claims/Hit/NetHitClaims.cpp)
- [Native prediction](https://github.com/liveteklol/Fruity-Prime/blob/master/src/MphRead.Native/Mods/Network/Gameplay/Combat/NetHitPrediction.cpp)
- [Shooter-first and confirmed-impact PR #109](https://github.com/liveteklol/Fruity-Prime/pull/109)
- [Shot events and Weavel/bomb sync PR #116](https://github.com/liveteklol/Fruity-Prime/pull/116)
- [Server combat/powerup validation PR #126](https://github.com/liveteklol/Fruity-Prime/pull/126)
- [Owner animation/transform correction PR #204](https://github.com/liveteklol/Fruity-Prime/pull/204)
- [Remote-death authority PR #205](https://github.com/liveteklol/Fruity-Prime/pull/205)
- [Hit-claim diagnostics PR #206](https://github.com/liveteklol/Fruity-Prime/pull/206)

---

**Final design principle:** *The player owns movement feel. The server owns combat truth. The network makes both agree quickly and visibly.*
