# CW-04c evidence — explicit timing and private rewind pools

Base SHA: `f908c7e534bce67e07a3776b21a658790034544d`.
Tested implementation SHA: `de458e2294f9d84c64ecbee5c61ebb435cde5075`;
following commit adds evidence/ledger only. Environment: macOS arm64,
.NET SDK 10.0.401; `DN=/Users/jarrett/.dotnet/dotnet`.

The final build/tests ran on the exact implementation sources atop CW-04b main
`9b334d8215f0f9042a86180e4fe8d6a3d85948dc`. Before committing, the branch took
the latest two main test fixes (#732/#733) by fast-forward. A checked empty diff
confirms neither fix changes `src/MphRead`, custom checks/baseline or nettest sources.

Scope: internal already-admitted custom rewind adapter; explicit requested/effective
timing and retained private pool; native `acceptedEquip` pool selection; complete
abort/reset/publication context cleanup; real-scene scope checks and contract docs.
The adapter establishes no source admission, canonical trade or payment authority.
No live custom Spawn, transport, protocol, native packet or replay change is enabled.
CW-04 remains incomplete; [remaining boundary](../04c-rewind-context.md).

| Command / check | Actual result | Log under `artifacts/custom-weapons/CW-04c/` |
| --- | --- | --- |
| `$DN build src/MphRead -c Release -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 96.54 s final build | `build-inactive-slots.log` |
| `$DN build tools/custom-weapons-check -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false` | PASS; zero warnings/errors, 2.78 s | `tool-final-build.log` |
| Custom check binary `--rewind-scene <isolated CW-03 content-data>` | PASS; 104 asset-backed scope checks | `rewind-scene.log` |
| Custom check binary without arguments | PASS; 2,093 contract checks | `contracts.log` |
| Same binary `--scene <isolated CW-03 content-data>` | PASS; all 45 rifle scene checks | `scene-regression.log` |
| Native tuning snapshot against CW-00 | PASS | `native-tables.log` |
| Fresh nettest build and `--architecture`, `--protocol58-replay`, `--shot-source-journal`, `--weapon-policy`, `--shot-inputs`, `--shot-lifecycle`, `--timed-shot-damage`, `--instagib-ion-fracture` | PASS each | Named logs and `native-results.json` |
| Nettest `--timed-shot-accepted <isolated content-data>` | PASS; 343 admitted-remote native assertions | `native-accepted-scene-final.log` |
| Nettest `--timed-imperialist-impact-scene <isolated content-data>` | PASS; 949 native impact/profile assertions | `native-imperialist-scene-final.log` |
| Game `-fidelityoracle verify all -allow-content -presentation-hz 60 -baselines <unchanged baselines>` | 46 PASS / one existing failure unchanged; exit 1 | `native-fidelity-final.log` |
| `git diff --check` | PASS | No whitespace errors |

The new fixture starts an actual asset-backed ServerSim with genuine remote owner
lives. Explicitly seeded certificates/resources test the scope seam only. It checks
integer/fractional/clamped timing independently of a deliberately different latest
remote ACK and with no native FireEvent. Actual target collision poses and mutable
geometry restore exactly. Missing initial/intermediate/fractional-upper player or
geometry history refuses before mutation, including an unpublished upper current
frame. Nested entry, malformed launch, wrong identities/resources and native-pool
aliases fail closed. Real native Spawn/Process demonstrates catch-up of only the
supplied private pool, leaving an existing native-pool projectile unchanged.

Abort, EndShot, Reset and Record restore/release context, including Record with
rewind disabled. Native aliases include a distinct eight-object subset of another
player's pool, within custom capacity. Enemy/platform native pool alias probes are
explicitly SKIPPED because that room has no such pools; their guards are present,
but those branches are not claimed exercised by this fixture.

Review/testing exposed and fixed three issues: disabled Record initially returned
before releasing accepted timing; alias checking initially covered only the shooter;
the expanded scan initially dereferenced inactive empty player slots. Regression
checks now pass. No native fixture or tolerance was changed. Missile splash remains
tick 177, `victim.position4096.z` expected 52814, actual 52815. Imperialist headshot F2
remains `6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Performance: no new whole-game percentile/device benchmark claimed. Pool masks grow
only when a larger supplied pool is first captured and are retained/reused; the scope
preflight is bounded by existing rewind limits and definition capacity. Existing
100,000-action zero-allocation journal checks still pass; that does not measure this
scene scope. CI: not awaited or reported PASS under the user's instruction.

NOT_RUN / remaining CW-04: real custom live emission, authenticated UDP/readiness,
accepted source pose/history recovery, mandatory canonical trade admission, distinct
payment/outcome identity through all native joins, snapshot/lifecycle/reconnect and
actual host/remote/dedicated loss/latency scenarios. Existing native scene fixtures
also explicitly exclude authenticated UDP and GL/audio. Physical/rendered platform
acceptance and custom replay remain later release blockers. Rollback: custom adapter
has no live caller; ordinary native equipment remains the default pool. Next gate:
complete actual CW-04 integration before CW-05.
