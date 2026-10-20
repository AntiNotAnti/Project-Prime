# CW-05s evidence — private acquisition knowledge carriage

Base SHA: `8cbc4c0779b87a5f802e57c64a03b8c4f1a5cfd5`, latest fetched main,
including independent PR859/860. Head SHA: completed-slice commit containing
this file. [Behavior and remaining dependencies](../05s-private-knowledge-carriage.md).
Scope: owner envelopes, creation-time input/control knowledge, authenticated
accepted input metadata, retained journal/outbox result binding, protocol73
allowlists and focused tests. No online pickup activation in this slice.

Command/environment: .NET10.0.401 on macOS arm64, DOTNET_PROCESSOR_COUNT=2,
UseSharedCompilation=false. Desktop uses `dotnet build src/MphRead/MphRead.csproj
-c Release -m:1`; tool builds additionally use BuildProjectReferences=false.
Each asset-dependent process uses its own PROJECT_PRIME_USER_DATA and paths.txt.
Source remained frozen through final compilers. Desktop and every tool's copied
game assembly use SHA256:
`a9e0561bc2595944730427c5c50ed0fe09cc9ec2a580c7a577f7d6b3a322a22e`.

| Acceptance | Result |
| --- | --- |
| Desktop Release | PASS103.022s, zero warnings/errors |
| Isolated headless-server Release | PASS116.124s, zero warnings/errors; desktop DLL unchanged |
| Android experimental Debug | PASS170.923s,26 warnings, zero errors |
| Android default Debug | PASS88.238s,25 warnings, zero errors |
| Custom build/test batch |21/21 PASS |
| Pure custom contracts |16,892 PASS, including813 private-carriage and457 empty-inventory checks |
| Actual client capture/send UDP boundary |46 checks PASS |
| Actual dedicated accepted-input/engine-source UDP boundary |6 checks plus strict wait gates PASS |
| Actual public empty-life/native-fire fixture |9 checks plus strict wait gates PASS |
| Network/native archive/party batch |19/19 build/test commands PASS |
| Developer action UDP/engine |1,571 checks PASS |
| Developer control UDP/engine |1,040 checks PASS |
| Native registry/tables |78 old names and exact stored MP/1P weapon tables PASS |
| Native fidelity |Known46/47, raw exit1 |
| Full public pickup combat |BLOCKED; actual acquisition/delivery not active |

The client fixture uses an actual asset-loaded player, normal C input routing,
NetHooks capture/send and authenticated packet91/93 traffic. Synthetic native
baseline, private owned state with stampA/B and typed denial are declared
authority seams. StateB arrives between original source capture and send; sourceA
still carries A, later input carries B alongside the retained controlA, exact
denialA settles that request, and input-only B remains independent. Explicit
client source-clock seams are302/304/306. Codec-valid native event histories
exercise the production encoder's192-byte/92-byte MTU boundaries without being
sent or fired. Mismatched native life/frame, malformed bytes and stale captured
scope refuse. No pickup funding, canonical damage or physical-device claim.

The authority fixture uses actual DedicatedServer, ServerSim, two UDP endpoints
and owner-thread observations. Its existing explicit developer4/16 loadout,
native load ACK/body/aim placement and synthetic stamps are declared seams.
Input-only93 enters the ordinary native acceptance path. Engine observations
recover original accepted source knowledge and ingress world; a changed-stamp
duplicate cannot replace that evidence or advance source/clock frontiers. A fresh
source carries B while retained A remains immutable. The second endpoint receives
exactly the original CompactIntent bytes. No custom action, grant, selection,
emission, payment or health change occurs. This tests carriage, not the separate
pickup-knowledge admission fence.

Pure checks cover exact133/100/113/233/146-byte shapes; independent92/192-byte
input prefixes; every truncation, malformed flags/partial knowledge, bounds,
overlap copying and uint-wrap funding floors; immutable creation stamps; no
private nonce in canonical109-byte actions or application ACK identities; and
zero allocations across100,000 warmed codec interactions. A real source journal
and explicit stub emitter prove pending/terminal stamp conflicts, exactly one
ammo debit/emission, retained original receipt after outbox ACK/flight expiry,
explicit publication stamp matching and retired terminal retry behavior. Empty
inventory decoding rejects historical113/114-byte live state. Protocol suites
retain historical72 and older archive fixtures, refuse old72 live connections,
accept73 and reject future74; movement-v3 selection remains71.

Developer UDP suites separately cover canonical health/trade, native respawn,
ACK/backpressure, control/source ordering and one160ms RTT/10% seeded datagram-loss
profile. They do not qualify public pickup combat or broad latency fairness.
The unchanged public full-combat fixture's automatic4/16 expectation actually
failed in05r; its evidence is retained there. It must acquire through real world
contact before that matrix can qualify the corrected public behavior. Public
lives remain empty; no fake grant or disabled assertion hides this gate.

Regressions found: the pre-fix client regression actually failed at independently
encoded native life/frame substitution during input-only MTU fallback. The final
encoder joins native match/epoch/generation/life/frame before encoding; the exact
negative checks now pass. The failing pre-join log and preliminary builds remain
separate from final evidence. Latest-main integration also occurred before all
final tests. Read-only production review found no further actionable defect;
this is local review, not GitHub approval.

Baseline comparison: native fidelity retains only weapon.missile.splash tick177,
victim.position4096.z expected52814/actual52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No native enum, CompactIntent/SlotIntent, public replica action or native archive
body changed. Private protocol73 envelopes intentionally change widths.

Artifacts/logs: `artifacts/custom-weapons/CW-05s-private-knowledge-carriage/`:
main/android/server integrated build JSON/logs;05s-custom-results.json;
network-results.json;final-corrected-results.json;pre-join-client.log and separately
prefixed preliminary evidence. Assets/toolchains/user data remain uncommitted.
CI job URL/status: NOT_WAITED per explicit user instruction; no CI PASS claimed.
Known limitations: no rendered/device/Hz/WAN qualification; actual online world
funding, reliable item state, knowledge admission and trusted rebind remain open.
Custom replay remains blocked untilCW-12. CW-05 throughCW-16 are incomplete.
Rollback: coordinated revert of protocol73/carriage commit; do not mix schemas.
Next dependency gate: authoritative frozen pickup world/contact, bounded reliable
item and owner publication, then private knowledge admission/rebind and real
public pickup combat acceptance.
