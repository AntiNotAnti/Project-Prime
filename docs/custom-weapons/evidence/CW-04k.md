# CW-04k evidence — live custom action and result transport

Base SHA: `867389f8e` (latest main integrated before final validation).
Runtime/test source: `1175e4c70`; subsequent changes are evidence only.
Environment: macOS arm64, .NET SDK 10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Scope: [action/result contract](../04k-action-transport.md).

Developer combat remains off by default. This slice adds authenticated action
ingress, bounded pre-effect result reservations, immutable canonical receipt/contact
retry with exact application ACK, and the actual client's intent/result cache.
Native health snapshots remain authoritative. CW-04 remains in progress.

Protocol 63 assigns only new IDs 86–90. Upstream protocol 62 already selects the
Quake3 v2 movement profile; integration preserves that threshold and its native
payloads. Historical 60/61/62 replay/checkpoint/status layouts are retained and
live old-version clients receive the ordinary explicit refusal. No native packet
shape, feature bit, damage attribution identity or replay checkpoint version changes.

Pre-integration production DLL SHA256:
`59c3db7463bd768832efa9848deba468572d1a5d19288deac7de3175b6262bcf`.
The main build passed with zero warnings/errors in 134.16s. Fresh custom/nettest
builds passed with zero warnings/errors in 1.91s / 1.06s. Complete logs are local
under `artifacts/custom-weapons/CW-04k/`.

| Check | Actual pre-integration result | Log / command record |
| --- | --- | --- |
| Pure contracts, codec/outbox/client cases | PASS 6,449 | `final-pure.log` |
| Engine readiness / admission / source / authority | PASS 127 / 249 / 318 / 985 | `final-custom-tool-results.json` |
| Offline / rewind scenes | PASS 46 / 156 | same command record |
| Actual UDP action/trade/respawn/loss | PASS 1,268 | `custom-action-udp-extended.log` |
| Actual NetSession client UDP handlers | PASS 30 | `custom-runtime-udp.log` |
| Readiness / source UDP regressions | PASS 134 / 84 | named extended logs |
| Native table parity and fourteen native contract suites | PASS | `native-results.json` |
| Native accepted / Imperialist scenes | PASS 343 / 949 | `content-results.json` |
| Native fidelity | 46 PASS; recorded single divergence persists | `native-fidelity-final.log` |
| Historical protocol/replay, Quake3 rules, reliability, queue budget, export | PASS | `registry-native-results.json` |

The pure suite covers exact payload lengths, reserved/absent-zero fields, full
bootstrap/manifest/owner identity, health arithmetic, legal wrapped deadlines,
spent/uncertain/no-spawn invariants, old-life exact ACK, capacity 16 without eviction,
contact-before-receipt, application-vs-transport ACK, retry and expiration, strict
contiguous requests, revision ordering and contradictory-token invalidation.
Independent Python goldens use literal schema JSON, UUID bytes, struct packing and
SHA256, not production writers. Receipt/contact/state/ACK widths are 209/116/113/83.
One hundred thousand warmed reader/outbox/client probes allocate zero bytes on the
measuring thread. The outbox's informational constructor measurement is 9,816 bytes;
this is not a scene CPU benchmark.

The action UDP fixture runs an actual dedicated engine with two manual endpoint
clients. Scene load ACKs and initial clear-lane/health/protection setup are explicit
fixtures. Accepted body/head damage is 65/150, protected contact is terminal zero,
duplicates/lost receipts do not repay, wrong endpoint/connection/slot/ACK cannot
release reservations, and full capacity refuses before magazine/HP mutation.
Equal-ACK return fire is submitted after canonical death and kills/scores once;
a strictly later ACK is denied without return damage or ammo debit. Actual native
death/spawn creates one fresh 4/16 next-life grant while preserving the occupant
clock and immutable old-life result/ACK obligations.

The lossy case uses client transport fault workers only (80 ms each direction,
160 ms RTT total, seeded 10% loss). It reads the fire ACK from a snapshot actually
received over UDP, sends fresh input before actions, and retries the exact native
carrier only under loss. The pre-integration run dropped 114 datagrams during
actions, received six receipt packets/one contact, and still paid exactly one round
and one 65 HP contact. This is not a fairness distribution or rendered client test.

The separate actual NetSession test uses a control-plane server with synthetic
native bootstrap/life and authority result facts. It verifies real host filtering,
client action publishing, retained intent until application receipt, duplicates,
state revisions, custom-only poisoning, unchanged native admission/health, and
cleanup. It does not claim its synthetic result is an engine hit; the dedicated
combat fixture supplies that evidence separately.

Fidelity baseline is unchanged: missile splash tick 177,
`victim.position4096.z`, expected 52814 / actual 52815. Imperialist headshot F2:
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No tolerance, baseline or expectation was relaxed.

Known limitations / NOT_RUN: full recipient-bootstrap replication, custom
reconnect/late-join/spectator state, missing/coalesced source recovery, local/bot
source parity and host/remote fairness remain CW-04 gates. Source proof still
requires an actually observed preceding release; missing history is not invented.
Custom replay, public input/loadout/HUD, physical Android, rendered acceptance,
WAN and p50/p95/p99 measurements are not claimed. CI/review are not awaited or
reported PASS. Rollback: both developer readiness/combat flags off; ordinary rooms
create no custom owners. Next dependency: finish CW-04 before CW-05.

## Integrated current-main validation (protocol 63)

Latest main `867389f8e` required a new protocol number because protocol 62 already
owns Quake3 v2 movement. The conflict resolution retains all upstream movement
thresholds, rocket behavior and historical layouts. Combined game build PASS,
zero warnings/errors in 60.76s (`build-integrated63.log`). Fresh tool builds PASS:
custom 1.74s, nettest 2.06s, zero warnings/errors. All three game DLL copies match:
`2a30fa53ccba97593c7d0a1d9edff75c5dc4336f243b81b7afb59c876be85d18`.

Pure 6,449 and all six custom scenes 127/249/318/985/46/156 PASS unchanged
(`integrated63-custom-results.json`). Registry verifies all 78 old names plus
only IDs 80–90/optional flag 2. Architecture, archival protocol/replay 371,
Quake3 rules 181, compatibility 294, protocol17, reliable loss/jitter/reorder/
duplication/capacity/expiry, queue budget, upstream baseline's unchanged three
digests and replay export 49 all PASS (`integrated63-registry-native-results.json`).
The new advanced movement/rocket rules pass 165 checks at protocol 63.

Native table parity and all fourteen native contract suites PASS again
(`integrated63-native-results.json`). Native accepted/Imperialist scenes PASS
343/949. Fidelity again has 46 PASS and only the same recorded missile splash
one-unit divergence (`integrated63-content-results.json`). All commands, source
inputs and complete logs are retained; no assertion was relaxed for integration.

The final integrated UDP combat run PASS 1,262 assertions (packet-arrival-dependent
manifest/retry assertions explain the pre-integration count difference), actual
client runtime PASS 30, readiness PASS 134 and engine source PASS 84. Native lobby
PASS 7,749 assertions. Exact commands/environment/timestamps/exits and clean
shutdown/error scans are in `integrated63-udp-native-results.json` and named logs.

Final native waitlist PASS 309 (56.089s); lobby duration 101.528s. All six final
UDP/native processes exit 0 with unchanged game/tool hashes before and after.
The log error scan identifies only the native lobby's intentional SYNC_TEST_MAP
failed-start fixture; no unexpected exception or simulation failure occurred.
Structured timing/argv/environment and complete shutdown output are retained.
