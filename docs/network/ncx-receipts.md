# NCX-02 canonical claim identity and terminal receipts

Status: implementation under acceptance; NCX-01 merged in PR #754 at `33cb131a3`.
This slice extends the existing payment ledger, claim cache and prediction outbox. It
introduces no combat controller, client-owned damage, movement enforcement or fast path.

`ShotPaymentKey` retains epoch/match/slot/generation/life/ShotId, victim generation/life,
component and direct/splash/turret kind. Its explicit optional Phase is zero for discrete
sources and the submitted continuous phase for continuous claims. Cache correlation now
preserves that dimension. The continuous authority transaction still reserves the original
owner-tick exclusivity across targets and healing; replacing its phase with emitted ShotId
would permit duplicate pulse payment and is prohibited. AcceptedShotLedger.Fired remains
source emission, not health payment. Only existing committed outcomes carry actual damage.

Every authenticated, well-formed refused claim has exact request shot/victim correlation.
Early wrong-life/retired-source and evicted-history refusals no longer depend on an absent
seen-cell to infer ShotId. Matching terminal replies are immutable. Unrelated claim IDs,
components, phases, generations and lives cannot borrow a cached accepted result.

The existing bounded authority receipt table grows to 4096 cells per shooter and pins
pending claims and terminal answers through the full 424-frame delivery deadline. Only
expired terminal cells may be reused. On exhaustion it preserves every retained receipt
and sends an exact capacity refusal; new IDs remain refused until a complete quiet retry
window elapses. Each further overflow extends that window, preventing an uncached refusal
from becoming an accepted rescue during its still-live retry window. Exact retained
terminal retries continue to replay during capacity refusal. No damage or effect is paid
by receipt storage or retirement.

Every outgoing receipt is retired once as Delivered, TimedOut, CapacityEvicted, LifeRetired,
OccupantRetired, MatchRetired or SessionRetired. Process-lifetime counts survive ordinary
world resets; opt-in keyed `receipt-retired` timeline rows retain exact source, component,
phase, victim and claim. Delivered includes refused terminal results and does not mean
paid damage. Local life/occupant changes cancel old receipts explicitly; late old-life
ACKs remain inert and cannot restore speculative health, deaths, effects or scores.
Match/session disposal also records pending authority cancellation before clearing it.
No old-life outcome is applied to the replacement body.

Wire protocol remains 59: no packet field/result encoding or replay layout changes.
Existing safe wire refusal codes remain; missing history is TooOld/refused, not guessed
payment. Lifecycle cancellation and bounded timeout are local typed dispositions.
The production-default arbitration, grace order, native simulation, posthumous flight,
same-world trades, remote 1HP prediction clamp and snapshot-owned scores are unchanged.

Acceptance must include exact packet refusal correlation, terminal cache churn and full
capacity, wrong/duplicate/late ACKs, lifecycle and map/epoch retirement, ID wrap, component
and continuous-phase separation, native/claim payment order, trades and all existing
arbitration/continuous fixtures. Constructed packet/scene checks stay distinct from real
UDP. The original N13 live settlement telemetry remediation remains open until an equivalent
live accepted-source/claim/payment/receipt workload demonstrates its required accounting.

Rollback is a matched-source revert of this slice. No new rollout flag or gameplay default
is enabled. Retained failure artifacts and old evidence remain source-qualified.
