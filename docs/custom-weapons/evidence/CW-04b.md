# CW-04b evidence — ordered source-time resources

Base SHA: `73d7586bd9b8560bce699f032241dfc320fd6799`.
Tested implementation SHA: `8300da2b8331399f3c8ba8c54939d579398fa263`;
the following commit adds evidence/ledger only. Environment: macOS arm64,
.NET SDK 10.0.401, `DN=/Users/jarrett/.dotnet/dotnet`.

Scope: dormant `CustomSourceActionJournal` and occupant clock, pure runtime preview,
contract tests and [integration contract](../04b-source-journal.md). No scene or
transport caller, live protocol/packet change or custom multiplayer advertisement.
CW-04 remains in progress. Trusted admission/emission test stubs do not implement
historical pose/geometry, canonical trade eligibility or actual network payment.

| Command / check | Actual result | Log under `artifacts/custom-weapons/CW-04b/` |
| --- | --- | --- |
| `$DN build src/MphRead -c Release -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 101.75 s final build | `build-final.log` |
| `$DN build tools/custom-weapons-check -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 1.54 s | `tool-build.log` |
| `$DN tools/custom-weapons-check/bin/Release/net10.0/custom-weapons-check.dll` | PASS; 2,093 checks | `contracts.log` |
| Same binary `--scene <isolated CW-03 content-data>` | PASS; all 45 existing real-scene rifle checks | `scene-regression.log` |
| Native tuning snapshot against CW-00 | PASS | `native-tables.log` |
| Fresh nettest build; `--architecture`, `--protocol58-replay`, `--shot-source-journal`, `--weapon-policy`, `--shot-inputs`, `--shot-lifecycle`, `--timed-shot-damage`, `--instagib-ion-fracture` | PASS each | Named logs and `native-results.json` |
| Game `-fidelityoracle verify all -allow-content -presentation-hz 60 -baselines <unchanged baselines>` | 46 PASS / one existing failure unchanged; exit 1 | `native-fidelity.log` |
| `git diff --check` | PASS | No whitespace errors |

New checks cover contiguous sequence ordering across weapon/action kinds, equal-tick
unequip/fire, changed-payload conflicts and immutable duplicate receipts; exact
owner/life/manifest/loadout fences; source clock acceleration and uint wrap; same-life
reconstruction refusal, life wrap and invalid-loadout binding; exact source permission,
stale/future world evidence, malformed certificates and denied optic disagreement;
offer/drain budgets, pending work, missing-sequence timeout and exact retention
saturation; legitimate reload transfer with capacity/no-spawn denial; uncertain and
throwing dependencies; reentrant retirement; form interruptions surviving ring
eviction; late restrictions and contradictory trusted observations; and non-mutating
reload/bolt previews with request identities preserved.

After 20,000 warmup actions, 100,000 successful observed/queued/processed optic actions
measured **0 bytes** allocated on the test thread. This loop uses an allocation-free
admission stub, preconstructed registry/manifest/clock/loadout, and two ticks per
operation to allow normal terminal expiry. One journal with one owned weapon measured
**72,664 bytes** construction allocation excluding those prebuilt dependencies. These
are narrow managed-core measurements, not game frame percentiles, network flood
results or Android memory/performance acceptance.

Test review found two gaps before this commit: an old novel restriction outside the
permission ring could escape retirement, and contradictory exact permission evidence
could leave the life active. Both now fail closed; regression checks pass. No baseline
or native gameplay was changed. Native missile splash remains tick 177,
`victim.position4096.z` expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

CI job/status: not awaited or claimed PASS; local acceptance above was run before
merge under the user's explicit instruction. Real custom UDP peers, authenticated
ingress/readiness, historical pose/trade adapter, rewind/private-pool integration,
indexed mixed-domain runtime payment, typed outcomes, snapshots/reconnect/late join,
loss/latency and host/remote/dedicated scenarios remain NOT_RUN / pending CW-04.
Custom replay and physical/rendered device matrices remain later release blockers.
Rollback: no live caller exists; remove the dormant journal and retain prior preview
callers' existing snapshot semantics. Next dependency gate: complete actual CW-04
authority/network integration and acceptance before CW-05.
