# NCX-02 canonical claim identity and terminal receipts

Status: complete scoped NCX-02 acceptance; NCX-01 merged in PR #754 at `33cb131a3`.
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

The original 128-ID admission reorder window remains separate from the 4096-cell receipt
retention capacity. Enlarging reply storage does not expand rescue eligibility. Normal
cache probes stop at their first empty cell; entries are cleared only for an entire slot,
so collision chains remain intact. Matching IDs with expired, different full identities
can reach fresh judgment after the delivery window instead of borrowing old outcomes.

Every outgoing receipt is retired once as Delivered, TimedOut, CapacityEvicted, LifeRetired,
OccupantRetired, MatchRetired or SessionRetired. Process-lifetime counts survive ordinary
world resets; opt-in keyed `receipt-retired` timeline rows retain exact source, component,
phase, victim and claim. Delivered includes refused terminal results and does not mean
paid damage. Local life/occupant changes cancel old receipts explicitly; late old-life
ACKs remain inert and cannot restore speculative health, deaths, effects or scores.
Match/session disposal also records pending authority cancellation before clearing it.
No old-life outcome is applied to the replacement body.

Use the existing `run-net2.py --shot-timeline --claim-receipts` to require final receipt
accounting. `ncx-receipts.json` checks process-lifetime Created = every typed retirement +
Outstanding, and joins each declaration to exactly one full source/component/phase/kind/
victim/claim retirement. Missing/overflowed rows and outstanding final receipts remain
INCOMPLETE; duplicate or contradictory accounting FAILS the requested campaign gate.
This output contains no raw source keys or player/account identifiers. A zero-claim
observer can satisfy accounting but supplies no gameplay-activity denominator. The native
Fruity adapter rejects this unsupported option rather than fabricating an equivalent.

`AuthorityPendingCancelled` specifically counts bulk match/session cancellation. Pending
life/occupant retirement follows the existing explicit WrongLife terminal path instead.

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

Final qualified build source is `4284731bd`, engine SHA256
`1852fd39e31080781f4d3fc7b45461bc207b200174e63259cc4f7781503c95c5`.
Build05 has zero warnings/errors. The preceding unchanged combat implementation passes
41 off/on routes; final affected off/on checks pass 698 receipt, 1157 arbitration and
667 continuous assertions per mode, plus protocol and custom-isolation controls. Native
fidelity retains 46 identical passing hashes and the known missile tick-177 Z divergence;
raw exit 1 remains and no golden is regenerated. Python exporter tests51 and comparator
tests23 pass. Failed compile, malformed-shape allocation and headless-observer attempts
remain in the manifest; none is retroactively promoted.

All three serial three-peer UDP controls pass gameplay, bounded drain and keyed receipt
accounting: LAN97, moderate90 and poor128 declarations, exactly315 deliveries in total,
zero outstanding or duplicate retirement identities. Authority component-payment rows
are103/90/129, each unique; these are separate denominators from declarations. The poor
control also observes two claim-rescue decisions. Fault profiles are configured simulator
settings; this does not certify an actual loss rate, performance or full fault matrix.
The initial headless observer failure used a firing requirement; the corrected harness
uses the same existing no-fire lifecycle/source evidence guard as rendered clients.

Main `747676025` is integrated after qualification. Its latest UI-test/notes changes are
outside the compiled closure: `code-stability-05.json` verifies every captured source,
project and shader input remains identical. Earlier measurements keep their original
build/source identity. Full records and retained limitations are in the
[validation manifest](../../tools/nettest/baselines/ncx-receipts.json).
