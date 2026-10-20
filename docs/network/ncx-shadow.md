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

Native eligibility is limited to distinct living human bipeds in ordinary Battle
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
are incomplete evidence. Later native head corrections are marked and excluded
from the initial certified subset, with an explicit denominator. They cannot
silently preserve an earlier successful comparison.

Export records include the source certificate facts, original emitted segment,
pre-damage gates/profile, actual native witness, decision, committed outcome and
terminal outcome. The Python auditor recomputes counter and outcome consistency,
requires positive activity, and fails closed on contradictions, missing fields or
incomplete joins. It does not independently recreate the engine's entire source
history or collision world. A physics/legal-hit denominator and an early-claim
settlement certificate are not inferred from this audit.

Acceptance is in progress. Raw runs, failed attempts and source/binary manifests
are retained under `artifacts/ncx/NCX-03/`. No family is promoted to early claim
settlement by this slice.
