# CW-04l evidence — reliable primary controls and real source binding

Base SHA: `651f5e53d` (CW-04k); latest main `4e1779ba1` integrated before final validation.
Production source: `a88af84c7`; integration `a6fb6e462`; fixture correction `0e1316183`;
new fixture/tool source committed `d3ce11d61` without changing tested content.
Scope: [reliable control contract](../04l-reliable-controls.md).
Environment: macOS arm64, .NET SDK 10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Game and both tool-copy DLL SHA256:
`2881d3ec72de7530ae53c66c194a8b0990c6703d261d75ec8b2cea00c9b0a0a8`.

The developer-only lane adds protocol 64 packets 91/92 with 76-byte primary
requests and honest 89-byte no-source outcomes. Native packet shapes, optional
flags and existing custom 86–90 stay unchanged. Ordinary rooms create no custom
owners. Trigger provenance here means an explicit authenticated request bound to
a fresh actual engine source, not proof of the hardware press or original tap-time
pose. Raw source actions preserve their physical-level proof.

| Check | Actual result | Local log under `artifacts/custom-weapons/CW-04l/` |
| --- | --- | --- |
| Final combined main Release build | PASS, zero warnings/errors, 89.17s | `build-control-udp-owner.log` |
| Fresh custom / nettest builds | PASS, zero warnings/errors, 2.38s / 3.68s | `l-control-tool-build.log`, `l-nettest-build.log` |
| Pure contracts | PASS 8,758, 1.838s | `l-control-pure.log` |
| Actual control UDP / engine | PASS 881, 21.555s | `l-control-udp.log` |
| Actual NetSession publisher/result handlers | PASS 44, 1.451s | `l-control-runtime-udp.log` |
| Readiness / admission / source / authority scenes | PASS 127 / 249 / 318 / 985 | `l-custom-scene-results.json` |
| Offline / rewind | PASS 46 / 156 | same command record |
| Registry / protocol / replay / movement / transport | PASS all eleven modes | `l-registry-native-results.json` |
| Native table parity and fourteen native combat contracts | PASS | `l-native-results.json` |
| Native accepted / Imperialist real scenes | PASS 343 / 949 | `l-content-results.json` |
| Native fidelity | 46 PASS; single known divergence persists | `native-fidelity-final.log` |

Full exact argv, environment, timestamps, elapsed times, hashes, exits and
complete process logs are retained in `l-control-results.json`,
`l-custom-scene-results.json`, `l-registry-native-results.json`,
`l-native-results.json` and `l-content-results.json`.

Pure tests include independent Python-packed byte goldens; every truncated/extra
length; full bootstrap/manifest/owner joins; kind/optic bounds; exact no-source ACK;
fixed capacity; duplicate/conflict/time/fence immutability; binding to one canonical
Action; no-source journal tombstones with no resource/source-time advance; sequence
progression and retention; client canonical-response matching, modes/lives,
revision/dedupe/token contradictions; and immutable 64-tick / 64/60-second source
wait limits with exact boundaries, uint wrap and a frozen-world wall-clock stall.
One hundred thousand warmed codec/duplicate/fence probes allocate zero bytes on
the measuring thread. This does not claim zero allocation for transport or scene
launches.

The new actual UDP fixture pauses the engine owner at an existing snapshot sink,
then receives both down and up native input carriers before releasing the next
simulation pass. The down source row is absent; the released up row is actually
observed. An admitted reliable Fire binds to that up row, returns its canonical
source (observed world strictly before processing world), pays exactly one round
and one 65 HP contact, and does not write native shot history. No source
certificate, synthetic source row or movement replay is injected. Native load ACKs
and initial clear-lane/health/respawn setup are explicit fixtures inherited from K.

Other real-engine cases verify seq2-before1 cannot select an early row; each head
needs a new consumed input after its predecessor settles; a first rejected
placement yields typed no-source denial with magazine4/HP200/no contact and seq4
can then proceed in the same life; actual native respawn retires unbound old-life
requests, grants the next life once at4/16 and accepts exact old-life ACK; sixteen
settled/unACKed controls fill the outbox and refuse seq17 Fire before bind/ammo/HP;
only an exact ACK plus retry plus another fresh source permits that Fire; and a
source arriving after no-source expiry cannot revive the old request.

The latency case uses actual client transport faults only: 80ms each direction,
160ms total RTT, seeded 10% datagram loss. All native input levels are released;
there is no fabricated native Shoot. A snapshot actually received over UDP supplies
the native ACK; one accepted round/contact still removes65 HP. Positive datagram
loss is asserted, but its exact numeric count is not logged and is not invented.
All seven scenarios report zero simulation step failures and clean shutdown.

The separate runtime fixture uses actual NetSession socket/readiness/publisher/
result handlers with synthetic native lifecycle and authority facts. It verifies
new-life mode reset, reliable request retry, canonical authority-selected source
acceptance and typed no-source ACK, plus the previous transport-vs-application,
revision, contradiction and native health-isolation checks. These synthetic facts
are not represented as a second engine combat instance.

The eleven protocol/native modes include unchanged 78 registry names and only
IDs80–92/optional flag2; historical protocol/replay414, Quake3 rules183,
compatibility299, protocol17, reliable, queue-budget, upstream three unchanged
digests, export49, advanced movement/rocket165 and current owner-track51. Protocol
62 movement thresholds remain unchanged. Historical63 packets/checkpoints/metadata
retain their shapes; live63 clients refuse explicitly; unknown future65 refuses.

Auditing found and fixed two bugs before final acceptance: peer-wide mode state
survived respawn, and accepting a request before custom life binding could skip
an unusable first input and wait for a favorable birth. Mode now joins the full
current owner; ingress requires an already bound active life. Source wait also
checks directly sampled monotonic wall time, so world-time stalls cannot extend
it. The first combined fixture build caught a read-only owner-property assignment;
the test now uses its explicit constructor. No assertion, baseline or policy was
relaxed to pass. Earlier preflight builds and logs remain available.

Fidelity remains exactly the recorded missile splash tick177 divergence,
`victim.position4096.z`, expected52814/actual52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No tolerances or baseline files changed.

NOT_RUN / remaining: full recipient-bootstrap state/event replication, custom
late join/reconnect/spectator baseline, local/bot source parity and final host/
remote fairness remain CW-04 gates. This next-source control policy deliberately
does not retroactively invent a pre-death trigger; original-tap timing/parity needs
an actual retained custom input witness. A historical source hint alone cannot
prove one. Custom replay belongs to CW-12; public control/HUD, physical Android,
rendered input, WAN and p50/p95/p99 acceptance are not claimed. CI/review are neither
awaited nor reported PASS. Rollback: both developer flags off. CW-05 remains gated
on completing the rest of CW-04.

Final unchanged server/native regressions PASS: K action UDP1,286 (30.049s),
I readiness134 (5.140s), J source84 (2.601s), full lobby7,751 (99.868s),
waitlist309 (53.674s). All exit0, clean shutdown and no unexpected diagnostics.
Exact commands/hash/times: `l-udp-native-results.json`. The K impaired run records
78 dropped datagrams during actions, five receipt packets and two contact packets;
application payment remains one. These runs use the above `2881...0a8` DLL.

A final client-only audit found that a valid-looking runtime73 under the frozen
one-row M7 manifest could enter the local publisher or unsolicited no-source cache.
The server already refused it. New tests reproduced RED against the prior DLL
(`l-control-runtime-guard-red.log`, exit134 at the new submit assertion). Three
client guards now require the canonical/frozen runtime. No server, source,
resource, native or codec logic changed. The final game build PASS67.51s with zero
warnings/errors (`build-client-mapping.log`); fresh tools PASS1.70s/2.53s.
Final pure PASS8,760 and actual client runtime UDP PASS44 with zero diagnostics.
Exact command/hash/timing records: `l-client-mapping-final-results.json`.
Final game/tool SHA256:
`1943406dea2b820240763c0e80728c86ae6c2262ee9a5ab8be900ace82fbc117`.
The two added regression checks are committed as `8c506e5cf`; the client guard
commit follows this evidence update. Earlier server/native results retain their
actual hash and are not relabelled as executions of the later client-only DLL.
