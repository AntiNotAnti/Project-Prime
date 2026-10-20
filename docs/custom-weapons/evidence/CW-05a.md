# CW-05a evidence — canonical client request producer

Base SHA: `fda773840a0a883ccb1d9c12849806e51c5d1fbe` (CW-04q, latest main).
Head SHA: the independently committed CW-05a slice accompanying this evidence.
Scope: `CustomClientCombat`, its typed `NetSession` producer wrapper, pure client
submission checks, actual client-runtime UDP fixture and documentation. See the
[producer contract](../05a-client-request-producer.md).

Environment: macOS arm64/.NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Main command: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -p:UseSharedCompilation=false`.
Result: PASS59.23s, zero warnings/errors (`CW-05a/main-build.log`).
Final DLL SHA256: `eadad203cbabe531bf6108b9030449a694f5ae61f6766c3676e2ff035da203a0`.

Commands, isolated user-data environment, timing, raw exits and game/copied DLL
hashes before and after each run are retained under artifacts/custom-weapons/CW-05a.

| Check | Result | Structured metadata |
| --- | --- | --- |
| Fresh custom tool | PASS1.864s, zero warnings/errors | 05a-final-custom-results.json |
| Pure suite | PASS10642, including56 new producer checks | same |
| Actual client projection and seven custom engine scenes | PASS145;173/127/249/318/985/46/156 | same |
| Fresh nettest / actual client-runtime producer UDP | PASS3.524s /75 checks,1.557s | 05a-final-nettest-results.json |
| Native tables, accepted and Imperialist scenes | PASS;343/949 checks | 05a-final-native-results.json |
| Native fidelity | 46/47, known single divergence, raw exit1 | same;05a-final-native-fidelity.log |
| Authority acceptance / spectator reconnect UDP | PASS1586/371 | 05a-final-nettest-results.json |
| Atomic input / reliable controls / action UDP | PASS708/1045/1550 | same |
| Architecture / native transport / reliability | All PASS; includes configured loss/jitter/reorder/duplicate recovery | same |

The new pure suite verifies canonical identity, pending frontier versus delayed
state, all16 queue entries, refusal without nonce consumption, exact retry bytes,
no-source settlement, old/new life and stale revision fencing, raw-mode isolation,
uint exhaustion and invalidated cache refusal. The100,000 warmed automatic
submit/typed-denial cycles allocate0B and leave canonical state and a real runtime
resource snapshot unchanged. This is pure publication evidence, not source admission.

The actual UDP runtime fixture uses the real client, authenticated authority
transport, readiness/result handlers and carrier encoder. Its native lifecycle,
public baseline and authority result facts remain explicit synthetic fixtures.
The new helper cannot publish before the public baseline, in a raw-action life,
against stale canonical life, after poison or after Stop. Kind/optic validation
cannot consume nonce1; a canonical receipt followed by unchanged state produces
nonce2; an honest no-source denial then permits nonce3. It never grants ammo,
creates a source certificate or applies health.

Baseline comparison: no native packet layout, ID, resource rule or custom codec
changed. Native fidelity's known missile-splash tick177 position4096.z mismatch
(52814/52815) remains an explicit release limitation. No tolerance/baseline changed.
CI and review are not awaited or claimed passing, as requested.

Regressions found: none in completed local checks. Rollback: revert this slice;
developer custom capability/combat flags remain off by default. Next gate: CW-05
source-time native/custom selection ownership, public controls, HUD and optics.
Keyboard/pad/touch, rendered prediction,60/120/240Hz cadence and physical Android
acceptance are NOT_RUN and this slice does not close CW-05.
