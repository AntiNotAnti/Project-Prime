# Claim receipts and current-life ordering

The NCX02 receipt change introduced a lifecycle regression: a rejected old-life
claim with ID 20,000 advanced the current-life ordering frontier, causing a
genuine current-life claim with ID 1 to be classified as an old repeat.
The original Linux hosted [lifecycle failure](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39347345802/job/118385301647)
and an independent Windows execution of the unchanged pre-fix binary reproduce
the original `ClaimBoundaries` assertion. The relevant receipt and lifecycle
sources are identical between that preserved Windows baseline and main
`017373d45fd7adbf59c7703b373e65cc25ce464d` before this patch.

`AnswerRequest` now retains the immutable terminal receipt while restoring the
prior ordering frontier when its caller passes `remember:false`. A `finally`
block also preserves that invariant if acknowledgement processing throws.
Existing exact-retry, identity-collision, zero-ID, admission-capacity and
old-life physical-outcome paths keep their existing behavior. Receipt capacity,
retention windows, shot/payment keys and authoritative damage rules are intact.

The original valid-claim and retransmission assertions remain unchanged.
Three additional checks verify that wrong-victim-life and wrong-shooter-life
requests leave the current ordering frontier untouched and that the rejected
request's terminal receipt remains retained.

Validation uses a fresh isolated Windows normal Core/nettest build from the
stated main plus these two changed sources. The first build attempt mistakenly
disabled the broad desktop RmlUi flag and failed before tests; its log remains
retained. The same-source second build uses ordinary desktop defaults with only
the narrow replay-preview-failure fixture disabled. It passes with zero warnings
and errors. Full source and binary hashes, failed controls, commands and results
are recorded with the validation evidence.

All 16 focused cases pass, including 3,738 lifecycle assertions, 698 receipt
checks, Native golden trajectories, rewind/rule checks and both recorded
Native/Momentum scene comparisons. Both actual production durable packet
comparators pass separately. These results are distinct from the fresh real
two-owner fault-combat sessions used to verify the transport path.

Both 60-second two-owner sessions also pass, with configured 200 ms RTT /
20 ms jitter, 1% loss, 5% reorder and 1% duplication. All six server/client
processes exit 0 and each model passes all 22 strict proof checks. Native
records 10 joined payments, 336 damage and 87 rewinds (1,310 frames); Momentum
records 7 joined payments, 202 damage and 69 rewinds (1,051 frames). Both
clients naturally die/respawn in each model and resend receipts; all trace
overflow and disconnect counters are zero. These counts establish functioning
payment/lifecycle transport, not comparative hit-registration balance.

[Sanitized validation evidence](../movement/evidence/pr09-claim-life-ordering.json)
retains every command, exact source/binary/runtime hashes, both model reports,
the unchanged 22-field replay comparisons and failed controls. Hosted CI for
this patch has not yet run; local passes do not establish full green CI.

This repair changes claim-receipt processing, not the movement solver, rule
serialization or replay format. Classic remains the default. The preserved
eight-bot CPU experiment records zero received/applied/refused human claims in
all 32 timing arms, so this path was not exercised there; its old source/binary
lease remains explicit. No new performance acceptance follows from this fix.
