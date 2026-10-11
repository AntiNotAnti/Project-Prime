# Netcode 2.0 — N5 combat reconciliation

N5 builds on merged N4/main `9a1bf3f83a37364363c9ab2b05bc23624852a953`.
Branch: `codex/net2-05-hit-reconciliation`.
Live protocol remains **45**; packet and replay layouts are unchanged.

## Behavior

Remote predictions retire only through their own claim and shot acknowledgement,
with the existing stream and lifecycle checks. A snapshot event no longer consumes
the oldest prediction or an unrelated victim-level confirmation credit. An accepted
ACK's damage sequence identifies the exact event whose feedback can be suppressed.
Rejects retire their debit even when the victim is already dead or prediction has
been disabled. Expiry releases each debit independently; continued firing cannot
keep an older damage estimate on the health bar.

Local hit markers, flinch and predicted nonlethal outgoing health remain immediate.
Remote lethal predictions and lethal ACK health remain clamped to one until an
actual authority death snapshot. Canonical health, score, death, inventory and
native effects remain owned by the authority. Early settlement and visual predicted
kills are not enabled. The existing **24–72-frame arbitration grace** and firing
order are unchanged.

A separate authority payment ledger reserves an exact shot/victim life/component/
damage-kind identity before native damage, affliction or impulse can run. It commits
only after a real body or turret health reduction and releases reservations when
normal gameplay rules refuse damage. A paid identity stays protected after its first
late native arrival. Direct, splash and child components remain independent. The
ledger is bounded to 4,096 entries per shooter, retained for 1,024 simulation frames;
capacity exhaustion refuses another payment instead of overwriting live evidence.
The protection operates independently of N4's optional proof mode.

Already-paid component claims return the authority outcome without paying again.
Shadow-mode Imperialist body/head correction retains its existing validated
partial-damage behavior and can promote the payment once. Retained terminal claim
answers are immutable through shooter/victim respawns. The 128-cell receipt cache
uses full ID lookup and never evicts an outstanding claim merely because another
ID shares its modulo bucket. The per-shooter pending limit remains 64. Reused legacy
native-ledger cells clear their component/kind metadata.

## Validation

All execution was on this Mac arm64. No Windows-specific change required a Windows
run. No Linux/Android test, GitHub Actions run or long soak was used.

- Release builds: zero errors, 41 existing warnings.
- 50 exact reconciliation assertions; 3,338,739 health/shot assertions across 7,776
  fault profiles; claim-stress and architecture checks passed.
- Final asset suite: 94 assertions covering real rescue-first and physical-first
  payments, distinct splash/child hits, repeated afflictions/impulse, native mutual
  lethal trades, posthumous missiles, Imperialist correction, lifecycle retries,
  receipt-ID collisions and evidence counters surviving room teardown.
- Existing shot-proof and combat scene suites: 324 and 204 assertions passed.
- 110 Python evidence/drain tests passed. Previous production DLLs fail the new
  unrelated-event, repeated rescued-component and pending receipt-collision tests
  at their intended behavioral assertions.
- Two-player LAN duel: 20 seconds active, 18 accepted/committed/fired shots, two
  exact confirmed predictions, 914 health and 474 slow-lane comparisons, zero
  health mismatches or outstanding predictions. The original audit reported a
  terminal score failure; source-time-corrected audit of the same raw run passes.
- Eight-player missile control: 25 seconds active at 400 ±80 ms RTT, 5% loss,
  3% reorder and 1% duplication, with bounded drain. All 109 shots were accepted,
  committed and fired; 18,821 health and 2,481 slow-lane comparisons passed. There
  were no timeouts, health mismatches, shot gaps or outstanding predictions. Twelve
  predictions ended as eleven confirmations and one bounded prediction expiry;
  final receipts are audited separately from that shorter presentation deadline.

The LAN run recorded two `AlreadyResolved` terminal claims. The impaired run
recorded eleven `AlreadyResolved` claims and one `Applied` rescue. Each terminal
claim matched exactly one recorded authority result. One 1,783 ms late receipt
applied zero presentation-health correction after its prediction had expired.
The impaired run has twelve verdict log entries, ten ACK telemetry events and
eleven settled-prediction ACK counter increments; these are separate populations,
not interchangeable delivery denominators. Component-level duplicate suppression
is established by the executable asset tests; authority telemetry lacks the
component/kind fields needed to establish that exhaustively from these campaigns.
The UDP candidate also reset its current payment counters at room teardown; the
final candidate retains process-lifetime counters, verified in the asset suite.

The gameplay candidate used by both UDP controls is
`472c6614eb9f0930f635e61a0ef04095300517ac`, engine SHA-256
`7277716163b7827d4d79414272d5e340afd2a4be232f807e2eed61c5d02e51d5`.
The final source candidate `f1ccc01f9ca1c97aa91ca1b5b52d09d6bdeaab21` adds only
process-lifetime evidence counters, the score auditor correction and their tests;
its 94-assertion asset suite and Python suite passed. Gameplay decisions are the
same as the UDP-tested candidate.

The LAN audit had compared a fast snapshot containing both players with a later,
valid slow payload sent after one player disconnected. The corrected comparison
joins that payload to its authority emission frame. Older receipts cannot move the
comparison backwards and hide a missed score change. Both the original false
failure and a false-pass counterexample fail under the saved old auditor. The
original raw FAIL summary is retained alongside the corrected derived audit;
there was no unchanged campaign rerun to obtain green output.

Failed development attempts remain under `artifacts/net2-n5`. They exposed a
lifecycle cancellation loop answering inactive entries and stale component metadata
in reused native-ledger cells. Two older real-projectile test fixtures also needed
to carry the component and damage kind used by live claims. These repairs and each
immutable tested binary are recorded in `tools/nettest/baselines/net2-n5-20261010.json`.
The raw evidence is archived locally as `artifacts/net2-n5-evidence.tar.gz`.

## Boundaries and follow-up

N4 remains in shadow mode; this does not establish strict admission readiness or
honest-player rejection rates. Continuous tick/heal ownership, self-hit claims,
bombs and specialized turret/hunter behavior retain their existing paths for
N6–N9. This slice guards discrete components with nonzero identities, not unidentified
legacy effects or enhanced bonus paths.

A damage snapshot does not carry the shot/component identity. If it arrives before
the exact ACK, its feedback remains eligible to replay rather than guessing which
prediction to consume. This can repeat presentation feedback, but does not confirm
an unrelated debit or pay authority damage twice. Rendered presentation agreement
and before/after performance gains were not measured here.

Focused commands:

```text
nettest --combat-reconciliation
nettest --combat-arbitration-scene PROFILE "MP1 SANCTORUS"
nettest --shot-proof-scene PROFILE "MP1 SANCTORUS"
nettest --combat-scene PROFILE "MP1 SANCTORUS"
nettest --claim-stress
```

N6 can build on this slice after review and merge. It should preserve these exact
receipts and component payments while addressing continuous ticks and healing.
