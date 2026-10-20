# CW-05t evidence — authoritative online pickups

Base SHA: `4b9b3fd4364c29cf5c25f5a6f1d7b491b1a5bd22` (integrated main).
The original slice commit `6ea832b0a9b6224d34099019520649bacfc2688c`
was integrated with concurrent movement/Android/offline-arena changes. The
protocol74 collision with movement PR864 was resolved by assigning pickup
synchronization75 and preserving Traversal v4 at its historical74 threshold.
All results below were rerun on the integrated source; preliminary evidence
and failed diagnostic logs remain separately named in the artifact directory.
Head SHA: completed-slice commit containing this file.
[Implementation and boundaries](../05t-online-pickups.md).
Scope: scene-owned online world/contact/funding, private acquisition admission,
public item state/application ACKs, pickup rendering/selection and real public
combat fixture migration. Live protocol75; native item/weapon IDs unchanged.

Command/environment: .NET10.0.401, macOS arm64, DOTNET_PROCESSOR_COUNT=2,
UseSharedCompilation=false. Desktop `dotnet build src/MphRead/MphRead.csproj
-c Release -m:1`; tools additionally BuildProjectReferences=false. Each asset
process has isolated PROJECT_PRIME_USER_DATA and paths.txt. Main sources were
frozen through final desktop, Android and isolated server compilers. Final game
SHA256, also checked against every tool copy:
`2e1202b536450e5737a8be32b5b40cfe854597421ca72f901b7591025a6a91e0`.

| Acceptance | Actual result |
| --- | --- |
| Desktop Release | PASS114.822s,1 warning,0 errors |
| Isolated headless-server Release | PASS115.523s,1 warning,0 errors; desktop DLL unchanged |
| Android experimental/default Debug | PASS150.162s/140.592s,27/26 warnings,0 errors |
| Custom build/test batch |22/22 PASS |
| Pure custom contracts |18,465 PASS, including1,545 public-item codec/delivery checks |
| Dedicated two-UDP pickup/grant/fire/ACK-loss fixture |22 checks plus strict waits PASS |
| Full public availability UDP suite |916 configuration/client,8,276 action,4,160 control checks PASS;203.216s |
| Network/native archive/party regression batch |22/22 build/test commands PASS, including Traversal v4 and full public UDP |
| Independent developer action/control/input UDP |1,548/1,063/711 checks PASS |
| Shared pickup fallback actual OpenGL |79 checks PASS,3.507s; before/after pictures inspected |
| Native registry/MP/1P tuning tables |PASS, all78 original names and exact stored tuning unchanged |
| Native fidelity |Known46/47, raw exit1; same baseline discrepancy |

The two-UDP fixture uses actual DedicatedServer and ServerSim with all developer
flags off. Both lives first receive empty private state and production public
item baselines. Actual owner-reported UDP positions approach the independently
authored Proving Ground station from native spawn4/floor, producing the real
item-phase contact. There is no direct Grant, funding callback invocation,
source/certificate injection or private-state injection. Native loading ACKs,
initial floor placement, later clear target lane and authored native inputs are
declared fixture seams; this is not a physical-client walking or rendered-online
test. Frozen DisablePowerups remains true: native timed pose refuses while the
ordinary weapon pose permits actual weapon collection.

The real grant names the exact current owner, original contact source+1, actual
grant world and next item incarnation. It supplies4/16, leaves the other life
empty and preserves old native source selection/empty knowledge. Collection
does not allocate a native shot, damage HP or enter custom payment. Withholding
the second recipient's application ACK retains the exact consumed fact and
produces at least two identical retries. A changed digest and another recipient's
actual ACK cannot open the execution barrier; replaying the exact durably
consumed ACK recovers it without another grant or HP/ammo change.

Immediately after ACK recovery, Fire1 with current knowledge and an otherwise
valid pre-grant target ACK is explicitly checked against native bootstrap and
64-world age bounds at send. It yields SourceDenied before a certificate,
emission, debit or payment. Placing this negative first keeps it within the
native age limit; the earlier test ordering expired before custom admission,
and its failed log is retained.

Fresh input with stampA plus deliberately empty-stamped Equip2 produces
a terminal SourceDenied receipt with the original empty stamp. Exact request
retry retains the full immutable verdict/token and cannot equip or spend.
Subsequent genuinely stamped Equip3/Optic4/Fire5 succeeds: one accepted emission,
one65-HP body payment, magazine3/reserve16, no native shot identity. Private ammo publication is separately awaited after
the contact/receipt; its quarter-second recovery cadence is not confused with
canonical damage timing.

The old public suite now actually acquires by contact. It retains damage/headshot,
trade, backpressure, duplicate, source/control ordering, retained old-life ACK,
native death/respawn, late-peer and seeded160ms RTT/10% loss assertions. A second
owner and a respawned first owner wait the genuine1800 simulation ticks for the
world item; no clock warp or hidden grant shortens that wait. Old-life results
are verified and acknowledged while the new life is still empty before later
reacquisition. The previous05r automatic-spawn4/16 fixture failure is resolved by
the corrected acquisition fixture, not removed assertions or developer funding.

Pure delivery checks use independently packed134-byte state/138-byte ACK goldens
and SHA256, every truncation, malformed/bounds cases,1,000 fuzz inputs, complete
baseline visibility, stale/conflicting duplicates, immutable incarnation deadlines,
uint wrap, exact sent-result digest ACKs, capacity/serial exhaustion, retries,
expiry, late join and64-item bounds.100,000 warmed codec/receive/ACK/lookups
allocate0B. Source-knowledge foundation/carriage checks remain in the batch.
Protocol tests add75 while retaining historical74/73/older native archives and
refusing old74 live/future76. Quake3 v3 remains71 and Traversal v4 remains74. Public values contain no
owner-resource/private-nonce fields; canonical Actions remain109 bytes.

OpenGL checks exercise the shared original procedural pickup geometry through
actual offline contact: three boxes before collection, none afterward, truthful
M7 HUD4/16 and no draw-driven advancement. Actual composite/world captures were
visually inspected. This qualifies the shared fallback extraction, not rendered
online synchronization, original final rifle art, physical devices or achieved
display Hz. Source inspection also verifies online drawing only borrows cached
synchronized state and cannot allocate/open a registry from the render pass.

Regressions found and corrected: the first actual contact run exposed the timed
powerup permission helper wrongly blocking weapons under the default rule. A
separate weapon pose fixes that while preserving the original timed path. The
new test subsequently needed to read already received common-probe baselines
and separately await private ammo publication; those failed fixture logs remain.
No health/damage assertion was weakened. Mailbox publication serial exhaustion
was added to pre-effect capacity checks and tested. Read-only final production
review found no further actionable defect; no GitHub approval is implied.

Baseline comparison: weapon.missile.splash tick177 retains
victim.position4096.z expected52814/actual52815; Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Desktop/server's single warning is the reflection-driven fixture ACK-withholding
field's compile-time default; Android adds its existing platform/XML warnings.

Artifacts/logs: `artifacts/custom-weapons/CW-05t-online-pickup-world/` contains
integrated build JSON/logs,05t-custom-results.json,network-results.json,
the full public suite network log,final-corrected-results.json,integrated-pickup-gl
captures/results and separately named preliminary failure logs. The public suite has independently recorded elapsed time; no gameplay
throughput, achieved Hz or comparative performance claim is made.
Assets, captures, user data and toolchains remain uncommitted.

CI job URL/status: NOT_WAITED per user instruction; CI is not reported PASS.
Known limitations: physical controller/touch/Android device, achieved60/120/240Hz,
rendered online/WAN and wider fault/performance matrix remain release gates.
Ordinary Hello/bootstrap retry preserves the exact binding; real remove/rejoin
increments generation and starts empty. A future genuine same-life bootstrap
replacement requires an explicit obligation-preserving transition; no such
normal production flow currently exists. Custom replay remains blocked until
CW-12. Final original rifle assets are CW-06; CW-06 throughCW-16 are incomplete.
Rollback: coordinated protocol75/carriage/contact revert or disable the frozen
public availability option for subsequent matches; do not mix live schemas.
Next dependency gate: original rifle model/audio/animation with fallback and
cross-platform/rig validation in CW-06; carry device/refresh gates into CW-16.
