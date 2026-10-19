# Movement advisory report identity

Movement validation remains opt-in observation of accepted owner poses. It cannot
reject input, correct player state, or establish a cheat verdict. Reports always
set `HardRejections = 0`; invalid evidence increments a diagnostic counter only.

Report schema 2 identifies MovementModel and ProfileVersion in samples, anomaly
rows and aggregate grouping keys. Native uses profile 0; Momentum uses the fixed
version-one profile. Unknown combinations are invalid evidence. Model/profile
changes reset the slot's comparison baseline, alongside match, authority,
occupant generation, life and Hunter changes. The fixed 28 aggregate rows retain
the original 14 Native Hunter/form indices, followed by 14 Momentum-v1 rows.
Anomaly storage remains bounded to 128 rows with explicit overflow accounting.

Accepted network observations obtain identity from the player's owning Scene's
match configuration, never from client-authored intent fields or local settings.
Native speed, vertical and acceleration equations remain unchanged. Momentum
horizontal bounds use the larger of those existing bounds and four times the
profile's retention speed / 60, plus 0.1 units per frame. Its acceleration bound
uses the larger of the Native bound and four times GroundAcceleration ×
GroundSpeed / 60², plus 0.2 units per frame². Vertical bounds remain native because
jump/gravity ownership is unchanged. These broad envelopes are diagnostic noise
margins, not permitted movement ceilings or proof of physical correctness.
Existing teleport, platform, jump-pad, impulse, form, climbing, collision and
Prime exemptions remain intact; stable alt form remains explicitly unsupported.

`--movement-validation-shadow` covers grouping/provenance, model baseline changes,
Native threshold preservation, wider Momentum observations, invalid profiles,
lifecycle/order/gaps, exemptions and bounded storage. This document does not claim
those checks have run for the current preparation until execution evidence is
recorded by the PR-09 validation run.

The campaign Python consumer accepts historical schema 1 (including absent Schema)
as exactly fourteen Native/profile-0 groups, and schema 2 as exactly twenty-eight
Native/profile-0 plus Momentum/profile-1 groups. Candidate identities include
model/profile, and each row must agree with its own group's maxima and refusal
count. Unknown versions/profiles and duplicate/incomplete groups fail closed.
Schema-2 metrics expose explicit `perModel` breakdowns; the legacy top-level
`perHunter` view exists only for schema 1. Global totals are labelled as sums of
separated groups, never an implicit same-Hunter Native/Momentum merge.
