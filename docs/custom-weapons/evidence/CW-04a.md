# CW-04a evidence — dormant network contracts

Base: `2f169d9d1e9eb0c1d2cb6edeb04837820a760a01` (CW-03 merged main).
Tested implementation: `374e63a1d2fc833d3061a6c7da46c7640d206772`;
following commit adds evidence/ledger only. Environment: macOS 27.0.1 arm64,
.NET SDK 10.0.401; `DN=/Users/jarrett/.dotnet/dotnet`.

This independently reviewable CW-04 sub-slice supplies immutable mechanics-manifest
hashing, bounded page codecs/reconstruction, readiness/action payloads and a nominal
custom action identity. **CW-04 is still in progress.** No live PacketType, protocol,
sender/receiver, authority damage, resource journal, replay or capability-advertisement
entry point is added. Live protocol remains 59. Runtime authorization and real
multiplayer acceptance remain mandatory; see `04-network-contracts.md`.

| Check | Actual result | Local log under `artifacts/custom-weapons/CW-04a/` |
| --- | --- | --- |
| `$DN build src/MphRead -c Release -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 134.74 s | `build.log` |
| `$DN build tools/custom-weapons-check -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 2.15 s final tool build | `tool-final-build.log` |
| New custom check binary | PASS; 1,269 checks | `contracts-final.log` |
| Same binary `--scene <isolated-content-data>` | PASS; all 45 CW-03 scene checks unchanged | `scene-regression.log` |
| Native stored tuning snapshot against CW-00 | PASS | `native-tables.log` |
| Fresh nettest build and `--architecture`, `--protocol58-replay`, `--shot-source-journal`, `--weapon-policy`, `--shot-inputs`, `--shot-lifecycle`, `--timed-shot-damage` | PASS | Named logs and `native-results.json` |
| New nettest `--instagib-ion-fracture` | PASS; 275 assertions | `instagib-ion-fracture.log` |
| Native game fidelity all 47, content enabled, presentation 60 Hz | 46 PASS / existing CW-00 failure unchanged | `native-fidelity.log` |
| `git diff --check` | PASS | No whitespace errors |

Golden vectors were independently calculated using Python `struct`, `uuid.bytes`
and `hashlib`, then frozen in tests; production writers do not generate expectations.
One-M7 manifest SHA-256 is
`a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9`.
The independent 64-row fixture is
`35434230efd8e80aeadec9dfc692e8a095b48f2f391015210b5d964de327d539`.
Full action, readiness, page and canonical-row byte vectors are asserted.

Coverage includes all page boundaries through 64 rows; reverse/missing/identical/
conflicting pages; corrupt full digest; cross-page duplicate identity; exact/missing/
wrong-revision/extra/disposed registry; every world-bootstrap component; native-ID,
unknown-version/schema/behavior/capability/action rejection; source ray shape and
bounds; exact lengths and empty failure outputs; immutable/detached storage; unsigned
GUID ordering; and nominal separation from native payment keys. The latter is a type
contract, **not** a claim that future shared runtime payment joins are already adapted.

10,000 seeded malformed/fuzz inputs terminated without parser exceptions. A warmed
100,000-iteration batch of valid/invalid action/readiness reads (four reads per
iteration) measured **0 bytes** allocated on its test thread. Bounded manifest pages
may allocate detached storage, at most 16 rows each and four pages per assembly.
This is not a whole-game/network performance percentile or flood-admission result.

The first golden action test exposed positive-zero versus C# unary-negated negative-
zero fixture input. The C# fixture now matches the independent Python inputs exactly;
a separate test verifies signed-zero bit-preserving round trips. Production payload
code and independent expected vectors were not changed to accommodate the test.

Native fidelity remains the same missile splash divergence at tick 177:
`victim.position4096.z` 52814 expected, 52815 actual. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baseline changed. No custom UDP peers, source-time admission, mixed-domain runtime
payment, host/dedicated/remote combat, loss/latency matrix, custom replay, physical
Android or rendered device matrix is claimed. Those are pending CW-04/later gates.
CI is not awaited or reported as passed. Rollback: these codecs have no live caller.
