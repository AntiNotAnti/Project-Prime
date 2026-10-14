# NET04 process-owned payment joins

Status: implemented opt-in producer and strict auditor; native/UDP qualification **NOT_RUN** for this change. Python constructed join falsifiers pass; the C# storage route awaits the serial build slot. This change depends on the separate `NetNativeImpactWitness` seam from commit `1a732ba5`. Existing historical smoke data remains unchanged: 44 archived certificates, 8 Granted observations, 3 source/pose-verified funding-coherent observations and 24 component payments were partial evidence, not a completed component settlement audit.

`PRIME_NET2_EVIDENCE` enables value copies at actual authority boundaries. `NetTimedShotProof.Complete` records the complete immutable certificate and every retention disposition, including refused capacity/alias/invalid outcomes. Actual `AddEntity` terminals copy originating full ShotKey, component, room, family and native/accepted-playback context. Authority claim receipt and final Answer preserve exact received identity and terminal outcome. The payment reservation records a row only when the existing payment owner commits; full component/victim/kind, reservation/payment chronology, explicit Native/Admitted lane, claimant correlation and actual body/turret outcome remain separate values. A correction is an ordered later event for the original payment, never an additional payment. Continuous phase/target payments retain actual damage/heal values and remain outside ordinary timed-source completeness.

The explicit row-budget option sets `PRIME_NET2_EVIDENCE_ROW_LIMIT` for legacy evidence and this recorder together, including paid-shot and drain key sets. Those key sets retain their historical 16384 default unless the validated override is explicitly requested. It does not enable soak, alter AI gameplay, or bypass the shared byte ceiling. Default budgets remain unchanged.

The join recorder has no health, grant, admission, source-spend, packet or replay decision. No Player, Beam, Scene or mutable grant reference is retained. Its buffers survive normal match/world teardown so a final empty-room reset cannot erase the campaign denominator. The legacy counters and payment stores retain their current behavior. Ordinary wire protocol 56 and default replay readers/writers 1–5 are unchanged; this recorder adds only an opt-in JSON report field.

Six lazily allocated value families use 1024-row pages. Each family has a hard 262144-row normal budget or 1048576-row soak budget (or an explicit validated `--evidence-row-limit` from 262144 to 1048576), with a shared 256 MiB value-page ceiling; actual row sizes, allocated pages/bytes, observed/retained/dropped counts and event ordinals are exported. This provides room beyond the historical short smoke and derives the soak row ceiling from 8 players × 1800 seconds × 60 input ticks = 864000 canonical origins. Component multiplicity, repeated claims and correction events have independent budgets: that arithmetic is not a guarantee that all conceivable traffic fits. The actual 20–30-minute workload must retain zero dropped evidence; row/page/byte refusal produces **INCOMPLETE**, never overwritten samples or an assumed complete denominator. Containers/page references add bounded bookkeeping overhead; report-time projection and serialization allocate separately.

The optional native witness is read only during the actual collision/payment scope. It must match the exact payment key and weapon and accompany a real body/turret reduction. Point, channel, head flag and segment/origin values come from that scope. Admitted claims do not acquire a physical witness from claimant `HitPoint`; unsupported, child, curved, splash and continuous claims do not become general Enforce cases. Native witness observations are separate from claimed-impact reconstruction and the existing `ImpactExact` capability declaration.

Run the strict auditor on a new genuine campaign report directory:

```sh
python3 tools/nettest/net04_join_evidence.py --campaign CAMPAIGN --out NEW_REPORT.json
```

It joins the full authority funding sequence/spawn incarnation/life/contact/expiry to A1 history; accepted remote sources require A2 exact offered owner claim, authority Grant verdict and captured contact proof, plus A3 owner prediction-to-confirmation chronology and observer confirmation. Certificates require actual originating components. Paid cases require the full component/victim/kind claim and terminal outcome, including health, damage sequence and effects; only actual ordered correction events can supply a later changed outcome. Native authority reservations are reported separately; absence of an owner claim cannot be invented into a component claim proof. Missing, dropped or nonpositive supported source/full-paid denominators remain **INCOMPLETE**. Contradictory funding/lifecycle identity, component or claim aliases, duplicate actual payments, reversed chronology or an unrelated terminal outcome are **FAIL**.

The reported source-verified fraction describes only observed funded ordinary source cases. It does not adjudicate the suggested 99.9% canary readiness gate, sample sufficiency, Off/Shadow gameplay equality, exact impact payout or broader rollout. Those require actual qualified positive campaigns and their independent policy/native/replay gates.

Focused commands:

```sh
python3 -m unittest discover -s tools/nettest -p test_net04_join_evidence.py -v
dotnet run --no-build --project tools/nettest/nettest.csproj -c Release -- --timed-campaign-joins
```

The tests use constructed values to falsify aliases, duplicate payments, missing/overflow/nonpositive evidence and incorrect outcome joins; they contribute no native pickup, emission or payment denominator. Rollback removes the new join report/recorder, its observational hooks, auditor/storage route and their CI invocation together. Existing grant, shot, payment and replay owners remain independent.
