# NCX-04 exact paid receipts

`PRIME_COMBAT_FASTPATH=paid` opts the authority into a narrow receipt shortcut.
The default and unknown values remain `off`; `shadow` retains the default
settlement policy with read-only evidence. Clients use the existing protocol59
claims and CombatAck packets. No new unpaid hit, damage amount, head classification,
world effect, movement rule or causal-ordering decision is authorized here.

The first admitted request for an already authoritative-paid, primary, direct
Power Beam or Imperialist component may return that exact immutable outcome before
`Judge` and `NetShotProof.Observe`. It uses the existing `Answer` and terminal cache.
Every preceding stream, life, generation, ID, capacity, match-ending and continuous
check keeps its existing place. An exact cached retry still exits before the new
branch. Missing, reserved or expired payments, ClaimId zero, wrong weapons, child
components, splash, turrets and continuous claims retain legacy handling. Both
head mismatch directions fall back, including the existing Imperialist body-to-head
correction. This is a key/material exemption, not a weapon-name exemption.

`PaidReceipts` counts actual first-request shortcuts and `PaidBypass` identifies
the exact retained Ready row. Skipped Judge and proof observations change validation
telemetry denominators: their previous refusal/radius/status counters must not be
compared as identical workloads. Damage, score, world effects, payment reservation,
source admission and replay capture retain their existing owners.

The previous generic paid branch already decides in the same Receive call.
The new branch removes redundant validation work. It makes no grace-delay, RTT,
CPU or latency superiority claim. `ncx_paid_receipts.py` joins an exact Ready
payment/request to authority-local claim-received and terminal-decided events;
it requires positive material parity and a nonempty cohort with p95 at most two
authority ticks. The Ready stamp is sufficient because its payment already exists.
It measures decision time before buffered verdict flush, not client delivery.
Missing, ambiguous, corrected, overflowing or malformed observations are incomplete;
contradictions fail. Native fresh-contact rows cannot substitute for receipt samples.

The existing runner accepts `--combat-fastpath paid` only with
`--shot-timeline --claim-receipts` and automatically requires the paid latency audit.
A default-policy comparison uses `--combat-fastpath shadow --paid-receipt-latency`
with those same evidence flags, preserving the old settlement route while collecting
matching evidence. Inherited fast-path flags are scrubbed, and only the authority
receives the explicitly selected mode.

Acceptance completed on source `6cbca98b42169d1896a05b6b571f047ded33b272`,
integrated with main `42bac699ee31d74f3d9476026f47b68df86efafc`.
The [exact-source manifest](../../tools/nettest/baselines/ncx-paid-receipts.json)
retains source/input/assembly and evidence hashes. Release build: zero warnings
and errors. All 51 focused routes pass (17 in each Off/Shadow/Paid mode), including
481 constructed Receive/Answer assertions and 304 actual native assertions per
invocation. All 109 Python checks pass. Native fidelity retains the same 46 passes
and characterized one-unit missile failure in all three modes; custom rewind
passes 156 assertions per mode, with unchanged goldens.

Six controlled native cases compare Off/Paid exact ACKs, first responses, cached
retries, health, sequences, single component payment, both RNG words, scores,
semantic events and nonempty replay hashes. Four matching body/head cases shortcut
once; both head mismatches stay legacy. Body-to-head still performs exactly one
legacy difference correction without another component payment. Three executions
of those same six controls yield 18 passing material comparisons; capture audits
are 15 PASS and three INCOMPLETE because corrected predictions are explicitly
excluded. A head-to-body capture's native comparison cannot certify its unsupported
claim. These are six controls, not eighteen families.

The same frozen binary completed two real loopback UDP campaigns with eight
players, 400 ms configured RTT, 80 ms jitter, 5% loss, 3% reorder, 1% duplicate,
90 seconds and a bounded drain. Both campaigns, receipt accounting, scoped parity
and drain pass. Default-policy Shadow: 189 exact eligible samples, zero shortcuts.
Paid: 172 exact eligible samples and 172 shortcuts. Both p95 authority decision
latencies are zero ticks; material disagreement and double-payment counts are zero.
This is the normal Power Beam Volley workload; Imperialist live UDP, deterministic
whole-match hit totals, CPU gains, client delivery and geographic latency are not
inferred from it. The controlled native pairs supply deterministic material parity.

The initial constructed route passed 481 assertions. The first native attempt
failed its diagnostic export on a raw Vector3 JSON cycle after the first valid
pair; its log remains retained. The projected export fixed that reporting fault
without changing gameplay assertions or goldens. The earlier NCX-03 graceful-close
failure remains open for NCX-09; these passing campaigns do not establish a fix.
Fresh claims, whole-family effects, mixed-binary and physical release qualification
remain subsequent gates. Raw evidence lives under `artifacts/ncx/NCX-04/`.
