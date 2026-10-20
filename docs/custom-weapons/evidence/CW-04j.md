# CW-04j evidence — live peer to engine source boundary

Base/latest main: `5bc952147` (CW-04i plus upstream baseline tooling).
Tested production source: `160f3b2cb`; `c194471b5` adds UDP fixtures/documentation.
Environment: macOS arm64, .NET SDK10.0.401.
Scope: [source-boundary contract](../04j-live-source-boundary.md).

The real dedicated server now supplies engine readiness, instead of an injected
readiness fixture, under a separate default-off developer combat flag. No custom
action/outcome packet, public activation or full CW-04 completion is claimed.

| Check | Actual result | Local log under `artifacts/custom-weapons/CW-04j/` |
| --- | --- | --- |
| Main Release build, final observer retirement guard | PASS, zero warnings/errors, 65.51s | `build-udp-observer-retirement.log` |
| Pure contracts before final test-only observer guard | PASS, 4,053 checks | `pure-final.log` |
| Automatic readiness/source-boundary scene | PASS, 127 checks | `engine-readiness-scene-final.log` |
| Engine admission / source / authority scenes | PASS, 249 / 318 / 985 checks | named final logs |
| Offline / rewind scenes | PASS, 46 / 156 checks | named final logs |

The source-boundary pure tests cover pre-ready ingress, held/duplicate input,
exact latest raw fields, source/life ordering and uint wrap, owner/stream/bootstrap
and generation mismatches, immutable boundary across newer lives, serial exhaustion,
readiness invalidation and observed equal-value publication replacement. Confirm,
read and open paths all refuse restoration after an observed replacement. Clock
preflight tests prove reads neither consume the life grant nor advance the arrival
frontier; original32-frame lead and backdated-world fences remain enforced.
100k warmed boundary/preflight probes allocate zero bytes on the measuring thread.

The new real-scene fixture skips manual BindLife entirely. It publishes explicitly
trusted accepted-input receipts and lets actual NetHooks placement/control phases
create the source boundary and unequipped4/16 inventory. It verifies pre-ready and
reconsumed receipts grant nothing; real equip/optic/fire reaches canonical65 health
removal; malformed/rejected source cannot open a boundary; spectator retirement
followed by otherwise-usable same-life restoration cannot grant another loadout.
An actual next native life binds once with the same retained clock; accelerated
source input fails that original clock. Warmed denied same-life BindLife probes
allocate zero bytes. Existing H/G/F fixtures and expectations are unchanged.

A separate actual UDP fixture uses a running dedicated native scene and real
ready/input handlers. Its two manual clients acknowledge native scene loading.
The test does not write actors from its coordination thread: an existing snapshot
sink is wrapped to read engine-owned state on its own thread and publish an
immutable observation. A pre-ready input and a duplicate remain unbound even when
reconsumed after readiness; a genuinely new post-ready input establishes the exact
source boundary and initial resource grant. A test observer initially attempted to
construct a full owner during disconnect cleanup; the final test-only guard skips
retired/zero identities. No production admission relaxation was needed.

NOT_RUN / remaining: custom action/result/snapshot transport, complete source
coverage and reliable missing/coalesced-input recovery, local/bot parity, combat
latency/loss/reorder/host fairness, custom reconnect/late-join/spectator state,
custom replay and physical/rendered platforms. The preceding-frame release proof
remains strict; missing evidence is not synthesized. CI/review are neither awaited
nor reported PASS. Protocol61, existing packet bytes and replay schemas do not
change in this slice.

Final-retirement verification: fresh custom tool build PASS, zero warnings/errors
in2.35s, then pure4,053 and all six scene modes127/249/318/985/46/156 PASS
against the final guarded game DLL. Exact commands, timings and logs are in
`final-retirement-results.json` / `final-retirement-*.log`. Game/tool SHA256:
`d325e85f5868f4363e4cb047af23dfa148e9d722109d6d638668cb6c147e0a82`.

Final real UDP-to-engine source test PASS84 and readiness UDP PASS134, with no
simulation/cleanup errors (`custom-source-udp-final.log`,
`custom-readiness-udp-final.log`). The number of repeated manifest assertions
depends on actual retry arrival; no branch or assertion was removed. Reliable
and queue-budget checks PASS; full native lobby PASS7,751.

Native table parity and fourteen contract suites PASS unchanged
(`native-results.json`); native accepted/Imperialist scenes PASS343/949.
Fidelity retains46PASS and only the unchanged missile splash divergence at
tick177, `victim.position4096.z`, expected52814/actual52815. Imperialist
headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baselines/tolerances changed. Full commands/output: `content-results.json`
and named logs. The fourteen suites are architecture, protocol58-replay,
shot-source-journal, weapon-policy, shot-inputs, shot-lifecycle,
timed-shot-damage, instagib-ion-fracture, combat-fast-damage,
combat-fast-decisions, combat-fast-receipts, claim-receipts, timed-grants
and timed-shot-certificates.

Latest main was integrated by clean rebase. Its only new files/edits are upstream
baseline documentation and nettest tooling; no game production source changed.
Full waitlist PASS309 (`waitlist-final.log`); all six UDP/transport checks exit0
in `udp-transport-results.json`. After integrating the tooling-only upstream
change, fresh nettest build PASS, zero warnings/errors in3.22s, and its new
`--upstream-baseline` two-run synthetic multi-life recorder check PASS
(`nettest-integrated-build.log`, `upstream-baseline.log`). Whitespace PASS.
