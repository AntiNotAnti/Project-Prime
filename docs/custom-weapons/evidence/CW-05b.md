# CW-05b evidence — bounded source-time selection history

Base SHA: `4e8a2a05d36e4eb82cd2804026be7949166ed4cd` (latest main, including
CW-05a and upstream PR821 source-coordinate/material import changes).
Head SHA: the CW-05b slice commit accompanying this evidence.
Scope: bounded ownership policy, ordered custom journal integration, actual source
phase sealing and retired-life history, plus pure/engine acceptance fixtures.
See [ownership contract](../05b-source-selection-history.md).

Environment: macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Main command: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -p:UseSharedCompilation=false`.
Final main build: PASS78.72s, zero warnings/errors (`main-final-build.log`).
Final DLL SHA256: `e93da7c719839cd6d6e6952c241ccdd868fa1ed0c0aa887ef906270a9e6e3332`.
An earlier preflight build preceded the emission-fence/retention repairs; its log
is retained separately and does not qualify the final source.

All command/environment/timing/raw-exit and before/after game/copied DLL hashes
are saved under `artifacts/custom-weapons/CW-05b/`. Fresh nettest build passed
3.068s with zero warnings/errors.

| Final check | Result | Structured metadata |
| --- | --- | --- |
| Actual client runtime UDP | PASS75 | 05b-final-nettest-results.json |
| Authority acceptance / spectator reconnect / public replicas UDP | PASS1631/371/648 | same |
| Atomic input / reliable control / action UDP | PASS707/1034/1538 | same |
| Architecture / reliability | All PASS | same |
| Fresh custom tool / pure suite | PASS1.878s /10833, including191 new checks | 05b-verified-custom-results.json |
| New actual engine selection scene | PASS124 | same |
| Client projection / seven existing custom engine scenes | PASS145;173/127/249/318/985/46/156 | same |
| Native tables / accepted / Imperialist | PASS;343/949 | 05b-final-native-results.json |
| Fidelity | 46/47, exact known single divergence, raw exit1 | same |
| Fresh texture import tool / complete synthetic pure suite | PASS, including latest material-bounds952 | 05b-final-external-results.json |

The new pure policy/journal checks cover retained native recovery after Equip,
unsealed/pending selectors, denied controls, release fences in both directions,
same-source order, full identity, source wrap, expiry, ring compaction and retired
historical answers. Their source oracle is explicitly synthetic. Real engine
scenes use actual native accepted inputs and placement/control phases, trusted
readiness configuration, manual action ingress, production source admission and
the actual custom projectile/contact adapter. They do not claim authenticated UDP
selection ingress, native event filtering, public input or rendering.
The100,000 warmed observe/commit/seal/read/release cycles allocate0B with the
bounded ownership history fully populated.

The first engine selection scene failed an assumed pending-flight/HP200 assertion.
Diagnostic-only rerun captured real Fire Accepted/Spawned, magazine3, contacts1,
payments1, HP135 and native shared damage ID1 immediately after emission: ACK1000
at source/world1001 had already advanced the real projectile during rewind
catch-up. Same-source Unequip was correctly SourceDenied/InvalidLife/NotAttempted;
the full resource snapshot, health and payment/contact counters were unchanged.
The original failed log and `05b-selection-diagnostic-results.json` preserve those
values. The strict in-flight test now explicitly captures ACK1001 and submits the
matching action against actual sampled world1001, avoiding that one-step catch-up.
It still requires HP200/payment0 before the later actual projectile contact and
one canonical65HP payment afterward. No production physics, range, speed, health,
clock, source proof or assertion tolerance was changed for this fixture correction.
The final verified run passed124 checks and captured emissions1/contacts0/payments0,
owned flight, HP200 and damage ID0 before and after the denied Unequip; the later
actual contact then dealt65 once. The green11-command custom group is preserved
separately from the initial red and diagnostic groups.

Baseline comparison: no native packet shape/ID or gameplay rule changed. Native
fidelity's existing missile-splash tick177 position4096.z52814/52815 mismatch remains
an explicit release limitation. Review/CI are not awaited or claimed passing.

Rollback: revert this slice; developer custom flags remain off. Next gate: CW-05
bounded native event staging/filtering and release through existing native recovery,
then public controls, HUD/optics and presentation. Physical Android, rendered
views,60/120/240Hz device cadence and WAN acceptance remain NOT_RUN.
