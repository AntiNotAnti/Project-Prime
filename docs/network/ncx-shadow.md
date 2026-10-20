# NCX-03 bounded shadow decisions

`PRIME_COMBAT_FASTPATH=shadow` enables authority-only observations in the existing
combat pipeline. The default and unknown values select `off`; there is no
settlement-enforcement mode. `run-net2.py --combat-fastpath shadow` explicitly
selects the authority observation. Clients retain their existing behavior.

The decision function returns `LegacyRequired`, `PendingDependencies`,
`VerifiedPaid`, `VerifiedNative`, or `Reject`. It never reserves or applies damage,
changes source admission, writes a packet, advances a clock, restores geometry,
or replaces the existing controller. Owner movement and the 60 Hz simulation are
unchanged. Each observation retains the full payment identity, including both
lives, component, kind and continuous phase. Claim observations additionally
retain the exact claim ID, flags, damage, clocks, point and impulse; a changed
request cannot borrow a prior comparison.

The initial observed subsets are:

- Exact already-paid primary direct Power Beam/Imperialist receipts after normal
  request admission. They predict an immutable reply, never another payment.
  Reservations and head mismatches remain with the existing pipeline.
- Stock Imperialist first-segment contacts at the existing native pre-damage
  seam. Source input, resources, modifier funding and completed projectile
  emission must be known. Native collision provides the actual obstacle and
  target ordering. Both actors' legal gates and the victim damage profile are
  frozen immediately before `TakeDamage`; the post-step history is not used as
  a pre-hit health/immunity snapshot.

Accepted remote shots can collide during catch-up before their completed timed
certificate is published. A temporary value scope binds the existing admitted
source attempt to the successfully completed single-component emission before
catch-up. It checks exact source identity, cost and ammo type, rejects partial or
conflicting emission, and restores its prior value on exit. It does not publish
an earlier certificate, journal entry, shot acceptance or payment.

Native eligibility requires both live and historical biped form for distinct living humans in ordinary Battle
or Battle Teams, with complete matching lifecycle, pre-damage frame, team and
susceptibility facts. Status, Enhanced, Balanced, special lethal rules, bots,
alt/transitional bodies, self damage, unsupported effects and missing evidence
fall back. Spawn immunity and friendly-fire blocks are predicted only when all
other facts are complete. Ordinary damage invulnerability is deliberately ignored
because the native beam contact derives `NoDmgInvuln`.

Native geometry time and current legal-gate time are separate exported stamps.
A native-ordered contact proves its existing execution turn. It does not establish
a closed cross-shooter frontier for a fresh claim. All fresh claim settlement
keeps the existing historical/death/trade ordering barrier. The
`wouldAlterTrade` counter currently detects immediate lethal-result disagreement;
existing arbitration tests separately cover cross-shooter trade behavior.

Storage is fixed at 1,024 rows per shooter, allocated only when observed, and
never silently recycled across room/session cleanup. Overflow and missing joins
are incomplete evidence. Observations made before a later native head correction are marked and excluded
from the initial certified subset, with an explicit denominator. They cannot
silently preserve an earlier successful comparison. An already-completed corrected
outcome can subsequently be observed as an exact paid duplicate; it predicts no
new damage.

Export records include the source certificate facts, original emitted segment,
pre-damage gates/profile, actual native witness, decision, committed outcome and
terminal outcome. The Python auditor recomputes counter and outcome consistency,
requires positive activity, and fails closed on contradictions, missing fields or
incomplete joins. It does not independently recreate the engine's entire source
history or collision world. A physics/legal-hit denominator and an early-claim
settlement certificate are not inferred from this audit.

The current-alt-after-history control retains an actual biped snapshot, changes
the live player to alt form before the accepted native drain, and requires verified
source/geometry, real native payment, and fast-path fallback. Rewound collision
form cannot hide an unsupported current actor state.

Scoped shadow acceptance completed on source
`75c32ac79794acc29534780888c64ce468125ff6`, integrated with main
`017373d45fd7adbf59c7703b373e65cc25ce464d`. The committed
[`ncx-shadow.json`](../../tools/nettest/baselines/ncx-shadow.json) records exact
source, input closure, assemblies and raw artifact hashes. The Release rebuild
has zero warnings/errors; 30 focused routes pass across Off/Shadow, including
receipt, arbitration, continuous, source, native witness and actual asset controls.
The 95 Python checks pass. The 46 passing native fidelity hashes remain identical
to NCX-01, with the same retained one-unit missile splash divergence; custom
rewind checks pass 156 assertions in each mode. Goldens were not regenerated.

Ten controlled asset cases run twice, each execution comparing internal
Off/Shadow arms with exact health, payments, scores, semantic events, both RNG
words and canonical replay metadata/bytes. Four controls positively predict native
damage; one predicts a native spawn-immunity block; five require fallback.
Independent capture audits report eight PASS and twelve INCOMPLETE across those
two executions, with no FAIL. Reject-only and unsupported-only captures deliberately
do not become positive eligible certifications. This is ten controls, not twenty
distinct families. It does not qualify fresh claim settlement or close a
cross-shooter causal frontier.

The final-source eight-player Power Beam Volley campaign uses actual loopback
UDP with configured 400 ms RTT, 80 ms jitter, 5% loss, 3% reorder and 1% duplicate
faults. Campaign, bounded drain, receipt accounting and scoped shadow audit pass:
131 exact already-paid comparisons, zero unexplained disagreements, zero
predicted double payments, zero missing eligible comparisons and zero overflow.
Earlier source08 LAN/impaired/eight-player captures retain 262 additional paid
comparisons with passing scoped payment audits; those runs remain attributed to
their original source and assembly. These are immutable receipt comparisons,
not newly fast-paid hits or geographic, pixel, CPU or superiority evidence.

Failures remain visible. One source08 impaired campaign failed its post-drain
graceful-close handshake despite passing payment and drain audits; the matched
same-binary Off control passed, so causality is undetermined and NCX-09 remains
open. A source09 impaired capture had no hits/claims and remains INCOMPLETE
(raw campaign FAIL); a subsequent attempt exhausted disk during staging before
any game processes. Neither is counted as a positive qualification. Original
N13 settlement/performance, physical platform and release gates remain incomplete.
Raw runs and failed attempts are retained under `artifacts/ncx/NCX-03/`.

This slice enables no early claim settlement. NCX-04 may promote only exact
already-paid receipts proven here; fresh claims and unsupported contexts retain
legacy handling. The existing paid branch already answers in the same Receive
call, so a narrower receipt route can reduce validation work but supplies no
new grace-delay or RTT improvement claim.
