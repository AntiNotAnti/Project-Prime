# Momentum v1 implementation closeout

The operator requested completion of the implementation goal and a separate
Quake III / Quake Live tuning pass. PR-00 through PR-09 are committed and merged;
Classic remains the default and Momentum remains an explicit match-wide opt-in.
This closes delivery of the existing v1 implementation with the qualifications
below. **Final hosted CI is unverified; this is not a claim of green CI or
universal release/platform acceptance.**

## Definition-of-done audit

The plan's section 12 permits items to be completed or explicitly marked
blocked/not verified. The audit keeps that distinction rather than turning
missing hardware or pending jobs into passes.

| Required item | Evidence and disposition |
| --- | --- |
| Ordered, independently reviewable PRs and per-slice results | PR-00…08: #704, #705, #707, #710, #713, #715, #721, #724, #730. PR-09 validation/hardening ends with #769. Git/GitHub checks verify every phase merged in ancestral order; [exact ledger](evidence/pr09-closeout.json). Per-slice commands/results retain their original source leases. |
| Independent implementation and dependencies | [Provenance](reference-attribution.md), pure math/profile source and project references: independent C#/OpenTK mathematics, no copied Unity/GPL/unlicensed implementation or new runtime dependency. |
| Native parity and Classic default | 56 Native golden trajectories, preserved jump/Y bits and rules/default/archive fixtures. [PR-00](pr00-results.md), [PR-03](pr03-results.md), [current compatibility](evidence/pr09-current-paid-compatibility.json). |
| Once-only fixed 60 Hz integration and native collision | One eligible horizontal seam, native acceleration/damping bypass only while it owns that step, unchanged Y and native collision. [PR-02](pr02-results.md), [PR-03](pr03-results.md), [PR-04](pr04-results.md), [PR-09i](pr09i-results.md). |
| Seven Hunters, alternate forms and special impulses | Positive actual form/climb/ledge/carrier/pad/teleport/damage/ability activations and bypass/resumption fixtures; all-Hunter jump/cap trials. [PR-04](pr04-results.md), [PR-07](pr07-results.md), [PR-09i](pr09i-results.md). |
| Authoritative lobby/offline/server rule and lifecycle freeze | Host-only, stale/invalid/mixed-protocol refusal; mode adopted before gameplay, frozen at start, Native campaign/old settings defaults. [PR-05](pr05-results.md), [PR-08](pr08-results.md). |
| Versioning and old recordings | Protocol 59 movement semantics and immutable profile 1; archival conversion/refusal, actual Native/Momentum recording/seek/export/killcam and exact durable packet comparison. [Replay validation](replay-validation.md), [PR-06](pr06-results.md), [repair](https://github.com/AntiNotAnti/Project-Prime/pull/752). |
| Spectators, join-in-progress, bots and transitions | Genuine human/bot/spectator ownership, joins/reuse/respawn, rematches and different-map changes. [Network evidence](evidence/pr09-network-draft.json), [PR-07](pr07-results.md), [owner combat](evidence/pr09-owner-combat-draft.json). |
| Analog, keyboard and touch input | Signed partial analog/diagonal/mixed precedence/release barriers, native bot consumers, controlled keyboard/pointer and Android touch/emulator boundary evidence. [PR-07](pr07-results.md). Physical/focus limits remain unverified below. |
| Latency/loss, refresh and performance evidence | Actual impaired two-/eight-owner combat and unique authority payments/rewinds; virtual cadence evidence distinguishes requested scheduling from measured display Hz. Full performance investigation retained; operator accepts the tradeoff and defers optimization. [PR-09h](pr09h-results.md). |
| Full relevant hosted CI | **NOT VERIFIED.** [Code-source build](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39352134943) is queued at closeout; local builds/tests do not substitute for its final outcome. Other hosted workflow outcomes remain pending. |
| Existing authority and advisory-only validation | Owner-reported human motion/authority bots preserved, no prediction/reconciliation migration, `HardRejections = 0`. NCX paid receipts remain independently guarded; current lifecycle/receipt integration passes. |
| User guide, profile, evidence, limits and attribution | [Movement modes](movement-modes.md), [tuning v1](tuning-v1.md), [acceptance matrix](acceptance-matrix.md), per-phase results and [attribution](reference-attribution.md). |
| Explicit rollback | Editable Native→Momentum→Native, start freeze and new-authority reset fixtures, actual UI persistence/ownership tests; select Classic before the next match or restart with Native. Historical decoders remain intact. [Server guide](../../SERVER.md#movement-rules). |

## Retained qualification limits

Windows hardware exists, but focus loss/reacquisition and achieved physical
refresh remain unverified. Virtual 30/60/120/144/240 schedules and a driver-reported
239 Hz mode are not achieved-refresh measurements. macOS OS-event input is
blocked by actual TCC event-post/accessibility denial. Physical controllers,
physical Android, current Linux asset-backed runtime and precise current Android
movement/jump coverage remain explicitly unverified. Prior OS-input/emulator
results keep their original source/scope; no fresh physical-device claim follows.

The seven earlier impaired05 position advisories and prior final-Y sample remain
unclassified. Later zero-overflow owner traces distinguish reproduced position
corrections from explosive native Speed; they do not identify every correction
subtype. Advisory WouldRefuse is not universally zero. The additional Native
Nodes control did not finish a capture; representative DefenderTeams gameplay
does prove positive objective/scoring. These disclosures are retained rather
than rewritten as passes or evidence of an unproven physics defect.

## Follow-on tuning

The current cap/air settings intentionally favor a conservative Source-inspired
envelope. The requested next direction is Quake III / Quake Live strafe-jump speed
building. Profile 1 and its archived semantics remain immutable. A new tuning
profile must be tested and explicitly versioned before it changes online or
recorded movement; the prior performance decision is not approval to overwrite
historical behavior.
