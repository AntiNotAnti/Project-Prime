# CW-05r evidence — empty public online life

Base SHA: `c913f74b3a1c85d092aedf1f756439b387bbf716` (CW-05q merge,
including independent source-preview PR856). Head SHA: the completed-slice commit
containing this file. [Behavior and open gates](../05r-empty-online-lives.md).
Scope: public-only empty journal binding, private state ownership, client absence
handling, protocol72 compatibility allowlists and targeted checks.

Desktop Release PASS138.415s, zero warnings/errors. Android experimental/default
Debug PASS140.094s/95.493s,26/25 warnings, zero errors. Isolated headless-server
compile PASS74.732s, zero warnings/errors and stable desktop DLL. .NET10.0.401,
macOS arm64, DOTNET_PROCESSOR_COUNT=2, UseSharedCompilation=false. Source frozen
through all compilers. Final desktop SHA256:
`c0a959c77e4a1a0c5fe7c30a6336c234dd41fce1d9993ec4da67972ab02533e1`.
Tool references build with BuildProjectReferences=false and matching copies.

| Acceptance | Result |
| --- | --- |
| Custom tool build/test commands |19/19 PASS |
| Pure custom contracts |15,826 PASS, including422 new empty-state checks |
| Actual public empty-life server fixture |9 checks plus strict wait gates PASS |
| Protocol/status/native replay regression batch |16/16 build/test commands PASS |
| Developer actual action transport |1,548 PASS,29.865s |
| Developer actual control transport |1,034 PASS,22.227s |
| Native registry/tuning tables |78 old names and exact MP/1P tables PASS |
| Native fidelity |Known46/47, raw exit1 |
| Old public full combat suite |FAIL at obsolete automatic4/16 expectation; release blocker |

The new actual public fixture uses two real UDP endpoints, DedicatedServer and
ServerSim with all developer flags off. Both receive114-byte private empty state
after acknowledged public baselines. Actual source observation and sealed native
selection remain present. A detached production client consumes the exact received
state and refuses Equip/Fire/Reload; manual protocol91 attempts create no inventory,
settled funded action, custom emission or payment. An authored native FireEvent
carried over actual UDP enters the native ledger/Fired verdict, timed proof,
physical projectile pool and recipient SlotIntent relay. Canonical authority death
and native Spawn followed by fresh UDP input produce a new empty life; zero engine
step failures. Reflection reuses the existing fixture; native scene ACKs, actor
lane/health setup, authored native event and Shadow proof modes are declared seams.
No custom resources, source certificate or private state is injected into authority.
This proves empty-life/native functionality, not online pickup acquisition or a
rendered client. Developer action/control suites separately preserve source,
health/trade, reload/control ordering, ACK/backpressure/respawn and one seeded
160ms RTT/10% datagram-loss profile; no WAN/fairness/device claim.

Pure checks cover exact114-byte state, all truncations including old113, trailing
data, all nonboolean flags, normalized absence, exhausted-owned distinction,
client refusal/reacquisition/new-life sequences, late result ACK without selection
resurrection and explicit absence clearing uncertainty. The independent state
golden vector appends01. Protocol tests keep historical71 fixtures/profile-v3
thresholds, add72 native-body equivalence, refuse old71 live envelopes/QueueHello,
and reject future73 discovery/checkpoints. No movement policy change.

The old `--custom-public-availability-udp` suite was actually run: raw exit1 after
17.243s at `ActionTransportFixture.WaitInitialStates`, waiting for automatic initial
four/sixteen unequipped state in `CustomPublicLateJoinControls`. That behavior was
explicitly removed by the user's pickup correction. The test is retained unchanged;
no fake grant or disabled combat assertion masks the failure. Its full public
combat acceptance remains BLOCKED until actual world pickup acquisition is wired
and the fixture acquires by contact. Public lives now truthfully remain empty;
online pickups are not activated in this slice. CW-05/CW-16 remain incomplete.

Native fidelity retains weapon.missile.splash tick177, victim.position4096.z
expected52814/actual52815; Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Raw evidence: `artifacts/custom-weapons/CW-05r-empty-online-life/`. Preliminary
pre-allowlist build and initial stale protocol71 policy-assertion failure remain
separate. Final checks above use the final hash. No new UI/GL/device/Hz/profile
qualification; compilation is not Android gameplay acceptance. New private state
adds one byte; no broad performance improvement is claimed.

CI/review: NOT_WAITED per user request, never reported PASS. Rollback: coordinated
revert of this protocol72/state/public-binding commit; do not mix live schemas.
Next dependency: private creation-time knowledge and genuine ingress capture,
then world contact/funding plus reliable item baselines/delivery and public combat
requalification. Native archives remain unchanged; custom replay stays blocked.
