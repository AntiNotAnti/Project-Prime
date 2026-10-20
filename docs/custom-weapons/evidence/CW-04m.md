# CW-04m evidence — atomic native input carrier

Base: CW-04l `1a113f20b`; latest main `e976574c6` integrated before final validation.
Production `90cf3413d`, registry `bd3290b99`, integration `c8afd23c7`, tests
`f86716bfc` and `f83eddfb2`. Scope: [carrier contract](../04m-atomic-input-carrier.md).
Environment: macOS arm64, .NET SDK10.0.401, DOTNET_PROCESSOR_COUNT=2.
Final game and both tool-copy DLL SHA256:
`b5edce2c73b2e6977845430018b9f15a3e5e48c0eaaf540535b20f314278f563`.

Protocol65 introduces unreliable packet93 containing one validated control and
one complete, unchanged native CompactIntent. The full datagram remains bounded
by1200 bytes. If it cannot fit, the original native intent is sent intact and the
control remains pending on reliable91. Ordinary native packets and old custom
result shapes remain unchanged. The authority captures the previous source fence
before native admission, admits unseen controls only with new current carriers,
and preserves native recovery for older inputs. Known contradictory identities
fail before native mutation. Duplicate retained controls do not consume semantic
offer budget at native input frequency.

| Check | Result | Local evidence under artifacts/custom-weapons/CW-04m |
| --- | --- | --- |
| Final integrated main Release build | PASS63.30s, zero warnings/errors | build-integrated-main.log |
| Fresh custom / nettest builds | PASS1.40s /2.56s | m-input-integrated-results.json |
| Pure contracts | PASS9025,2.615s | same |
| Actual carrier UDP / engine | PASS604,13.466s | same |
| Actual NetSession publisher/result handlers | PASS51,1.482s | same |
| L/K/I/J UDP regressions | PASS870/1273/134/84 | m-regression-udp-results.json |
| Native lobby / waitlist | PASS7750/309 | same |
| All six custom scenes | PASS127/249/318/985/46/156 | m-custom-scene-results.json |
| Protocol / replay / movement / transport / created lobby | All twelve modes PASS | m-registry-native-results.json |
| Native tables and fourteen combat modes | PASS | m-native-results.json |
| Native accepted / Imperialist scenes | PASS343/949 | m-content-results.json |
| Native fidelity | 46 PASS; known single divergence | native-fidelity-final.log |

The pure cases cover framing, every truncation, atomic defaults, full owner join,
failed-write canaries, overlapping spans, all fourteen native fire events,
unchanged oversized fallback bytes, pending sequence order despite array reuse,
and packet classifications. One hundred thousand warmed codec/lookup probes
allocate zero bytes on the measuring thread; transport/scene allocation is not
claimed.

The real engine test captures a positive source in world W, canonically kills the
shooter in W, then accepts the deferred shot once using that original source and
an eligible target ACK. One round pays one65HP contact. A late reliable retry
repeats the immutable receipt/contact without repaying. Other cases prove an
invalid first source cannot be promoted by retry; already consumed native inputs
cannot acquire unseen controls retroactively; coalesced carriers bind only the
actual consumed row; and invalid identity/slot/framing does not mutate native
input. Native load ACK and clear-lane/respawn setup are explicit fixtures. No
source certificate is injected. Client runtime checks exercise the actual encoder
in an admitted NetSession; their lifecycle/result facts are synthetic and are not
claimed as another engine instance.

The K regression uses client-only160ms RTT/10% seeded datagram loss. It records72
dropped action datagrams, five receipt packets and two contact packets with one
payment. Every UDP suite exits zero without simulation failures or unexpected
diagnostics. Exact commands, hashes, timestamps and logs are in the listed JSON
records. Retry-dependent assertion counts varied in the earlier preintegration
runs (carrier609); those records retain their original DLL hash. Earlier build
failures were test-only nullable access/import issues, plus a pure test that had
left its second pending request due. Those fixtures were corrected without
relaxing production policy or assertions.

Fidelity remains only missile splash tick177 victim.position4096.z,
expected52814/actual52815. No baseline/tolerance changed. Imperialist headshot F2
remains `6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Remaining CW-04 gates: recipient-owned public replication, late join/reconnect/
spectator baselines, local/bot source parity and final host/remote fairness. This
carrier proves an actual retained custom witness at its consumed source; it does
not invent the timing of a skipped hardware tap. Public input/HUD, replay, physical
Android/rendered devices, WAN and p50/p95/p99 are NOT_RUN. CI/review are neither
awaited nor reported PASS. Rollback: both developer flags off. CW-05 stays gated
on completing CW-04.
