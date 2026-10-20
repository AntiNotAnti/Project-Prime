# CW-04p evidence — live recipient delivery

Base main3933d9ea2; current upstreamd7141505c (texture decoder) integrated in
facf73a5e. Initial implementation commit5aa666c39; the remaining lifecycle/fixture
repairs accompany this evidence. macOS arm64, .NET10.0.401, processor count2.
The final settled game DLL SHA256 is
`fb0365ed6b2575ede5685d05d301954ac7fa4ab1636b9331f8c49ee695b063f0`.
The [live contract](../04p-live-recipient-replicas.md) describes the scope.

Protocol67 activates94–96 without changing their protocol66 shapes or native
payloads. Public observation has an independent recipient bootstrap and actual
transport connection; an initial spectator can receive and ACK its baseline at
Life0 without a source boundary or weapon grant. Fire reserves the fixed audience
before resource settlement. Actual launch and terminal facts fan out independently
of mandatory original-owner result retention. Client facts enter a bounded FIFO
before consumption ACKs; duplicate baselines cannot restore old presentation.

Review found and fixed a reservation leak when a prepared, un-emitted source was
retired. Canonical cleanup receipts now finalize the audience; source bootstrap
invalidation cancels only un-emitted work for the exact slot. A same-valued
bootstrap from a different slot cannot cancel another source. Already launched
work retains its original recipient objects and deadline. Client resets eagerly
close queued replicas. Selected custom scenes also refuse native recording and
killcam reconstruction until CW-12; active/pending recording prevents activation.
The scene marker survives readiness loss, match reset while the old scene remains,
and disconnect, while a fresh native scene recovers recording admission.

Final checks and exact commands are recorded under artifacts/custom-weapons/CW-04p.
The p-HEARTBEAT-* files cover the final DLL. Earlier p-INTEGRATED-*, p-FINAL-*, p-SETTLED-* and
preflight files retain their own hashes and are not relabelled as final runs.

- Release build: PASS86.12s, zero warnings/errors (main-heartbeat-build.log).
- Fresh nettest build: PASS3.49s, zero warnings/errors (nettest-heartbeat-build.log).
- Fresh custom tool: PASS2.015s; pure10586 checks PASS2.25s
  (p-HEARTBEAT-custom-results.json).

| Final check | Actual result | Local metadata/log |
| --- | --- | --- |
| Seven custom engine scenes | PASS173/127/249/318/985/46/156 | p-HEARTBEAT-custom-scenes.json |
| Sixteen native/protocol modes, static registry | All PASS, protocol67; native packet IDs unchanged | p-HEARTBEAT-registry-native-results.json, p-HEARTBEAT-registry-static.json |
| Live P replica / actual client runtime | PASS640 /67 | p-heartbeat-udp-results.json |
| Existing M/L/K/I/J real UDP | PASS703/1024/1555/134/89 | same |
| Native lobby / waitlist | PASS7750/309,99.686s/54.948s | same |
| Native tables +12 combat modes | All PASS | p-heartbeat-native-results.json |
| Native accepted / Imperialist scenes | PASS343/949 | same |
| Fidelity | 46PASS, only known missile-splash divergence; exit1 | heartbeat-native-fidelity.log |

The final K impairment run reports102 dropped client datagrams, six receipt
packets and one contact packet, with one canonical payment. Faults are160ms RTT,
10% loss and seed431 on client transports. These are measured fixture results,
not WAN or human latency percentile measurements. The fidelity divergence remains
missile splash tick177 victim.position4096.z, expected52814/actual52815; no new
divergence, tolerance or baseline change. Imperialist headshot F2 remains
6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351.

The pure suite adds durable FIFO capacity/refusal-before-dedupe, retry after drain,
state delivery while full, eager closure, actual-mailbox cohort replacement,
terminal retained during baseline, bounded128-group admission, and source-slot/
bootstrap cancellation without a ghost observer timeout. Recording tests exercise
actual recorder/server/clip entry refusal, all three pending-writer races, disabled
opt-in, retained scene versus new scene, and both killcam policies. These are pure
contracts/synthetic admission fixtures; they are not network or rendered evidence.

Live fixture corrections are explicit. A spectator Hello must be followed by the
role-aware loaded packet; the old test helper's legacy loaded packet cleared its
spectator role. Only that spectator helper now sends the role-aware native shape;
ordinary legacy fixture bytes remain unchanged. Control-result probes also forward
94/95 to their existing consumer instead of swallowing those ACK obligations.

An initial observer-disconnect run failed a strict HP assertion although private
receipt/contact bytes and custom payment counters remained unchanged. Diagnostic
repeat1 on DLL3be3ca2744926d132e32fec8cf3c7443691609fd2a9dfc627e6f32b5972f47d0
captured a separate native environmental result at world98: source255, weapon255,
no shot, damage14, HP135→121, native damage counter1→2. The custom contact remained
at world49 with one payment and one terminal. The extracted event is preserved in
p-disconnect-red-authority-results.json and full trace in p-disconnect-repeat-1.log.
A one-time refresh passed three repeats but still allowed an environmental hit
in the earlier launch-ACK wait under load. The fixture now sends both players'
neutral movement through real UDP every100ms throughout all ACK waits, keeping
them within the native stale-input budget.
No native damage/physics code or strict health assertion was changed.
Three final repetitions under concurrent regression load all passed640 checks.
See p-HEARTBEAT-replica-repeats.json; earlier failures remain preserved.

Public input/HUD and device cadence remain CW-05. Package replay remains CW-12.
Physical Android/rendered views, WAN and performance percentile acceptance are
NOT_RUN. Review and CI are neither awaited nor reported as passing.
