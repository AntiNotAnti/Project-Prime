# CW-05c evidence — native fire isolation at custom source boundaries

Base SHA: `ab468f6e4ad6400e441a92c77e89ded858c6659c` (latest main at
integration, including PR824 geometry companions and PR825 Traversal/protocol68).
Head SHA: the CW-05c slice commit accompanying this evidence.
Scope: bounded native event staging, source-time selection filtering, actual native
admission confirmation, safe relay/recording and independent native claim guards.
See [contract](../05c-native-fire-isolation.md).

Environment: macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Main command: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -m:1 -p:UseSharedCompilation=false`.
Final integrated main build PASS66.88s, zero warnings/errors
(`main-integrated-build.log`). Final DLL SHA256:
`969e79321833f3e452f326a3abd6c5c58723d753b3f3e25ee53ab05e2529a425`.
Fresh nettest/custom/import builds PASS4.516s/2.130s/2.123s, zero warnings/errors,
with `BuildProjectReferences=false`. All main sources remained frozen during builds.

Exact commands, environment, timings, raw exits and before/after main/copied DLL
hashes live under `artifacts/custom-weapons/CW-05c/`. Pre-integration records are
distinct from the final `05c-integrated-*` records and do not qualify latest main.

| Integrated acceptance | Result | Structured metadata |
| --- | --- | --- |
| New actual UDP native/custom isolation | PASS680 /11.122s | 05c-integrated-nettest-results.json |
| Actual client runtime UDP | PASS75 | same |
| Authority acceptance / reconnect / public replicas UDP | PASS1623/371/655 | same |
| Atomic input / reliable control / action UDP | PASS708/1051/1562 | same |
| Readiness / application-result delivery UDP | PASS134/89 | same |
| Architecture / reliable transport | All PASS | same |
| Native-only lobby / waitlist UDP | PASS7750/309;99.558s/55.090s | 05c-integrated-lobby-results.json |
| Pure custom suite | PASS14204 | 05c-integrated-custom-results.json |
| Targeted actual engine source/native gate | PASS180 | same |
| Client projection / seven existing custom scenes | PASS145;173/127/249/318/985/46/156 | same |
| Native tables / accepted / Imperialist | PASS;343/949 | 05c-integrated-native-results.json |
| Native protocol, claim accounting, source/resources, continuous and turret suites | All PASS | same |
| Upstream Traversal rules / actual self splash | PASS255/671 | same |
| Native fidelity with operator content configured | 46/47; exact known divergence, raw exit1 | 05c-integrated-native-fidelity-content-results.json |
| Complete external-import pure suite | PASS, including source geometry2126 | 05c-integrated-external-results.json |

The gate tests cover immutable full-key identity, source ownership/release races,
owned carrier copies, physical controls, exact membership, lifecycle/connection
fences,64 waiting and2048 remembered entry bounds, time wrap and terminal retries.
Confirmed source authority lasts through1024 worlds independently of optional
proof; unconfirmed/rejected facts retain256. Sustained1025 unique handheld events
and128 conservative turret facts fit without overflow. Two warmed100,000-iteration
duplicate/read/take and novel stage/take/release/confirm probes allocate0B.

The engine fixtures use actual consumed source phases to prove a confirmed-ready
claim waits before any native event or custom life binding, then releases only
after native intake. Later accepted custom Equip and300 real simulation steps do
not revoke that admission, with native and timed proof Off. Actual native occupant
replacement preserves native-only fallback. The turret positive uses the actual
projectile factory/commit and canonical TurretEmitted lookup, with wrong beam and
launch-world rejection. These source authorization checks do not apply claim
damage, represent authenticated sockets or claim rendered/device coverage.

The UDP fixture uses actual endpoint-owned carriers93/native intents, real source
phases, ordered Equip/Unequip, native accepted ledger/playback, recipient relays
and native claim verdicts. Same-carrier Equip/fire rejects the native event without
native ledger/proof/ammo/health effects while preserving movement and physical
Shoot. An independent same-numbered custom action emits and pays65 once; shaped
native Power/Imperialist claims cannot rescue or head-correct it. Proper Unequip
releases native emission; pre-Equip historical native sources survive current
custom selection. Claim-before-event waits and proceeds after actual admission.
Known admitted sources reject different beam/launch stamps in Shadow mode.

The bounded incoming collision is explicit: playback is temporarily held so a real
turret offer1 owns its native bucket; a separate real carrier129 receives selection
permission but native intake refuses it. It remains unadmitted, unrelayed and
unable to authorize a Shadow claim. An old-life carrier cannot authorize a
future-life turret source. Native FireEvents/claims, loading ACKs, actor placement
and proof resources are declared fixture seams; no custom source certificate or
canonical payment is injected. Actual connection retirement, NoBeam positive,
public UI controls and rendered clients are not claimed by this fixture.

Corrections and failed preflights are retained honestly. Initial compilation found
two fixture ReadOnlySpan slice mistakes. A parallel MSBuild child exited with
MSB4166; serial tool retry succeeded. The first claim-before-event fixture sent a
unit aiming vector as impact impulse; authority correctly refused it with
`impact impulse exceeds weapon limit`, HP200 and zero applied claims. The fixture
now declares zero impulse and keeps its strict pending/health assertions. Another
fixture queried a watch row before the simulation owner published it; its wait now
checks row presence before the same decision assertion. A turret engine fixture
needed actual Scene.AddEntity initialization before the native factory, matching
the normal form path. No production geometry, damage, proof, clock, baseline or
assertion tolerance was changed to make those tests pass.

Review also found and repaired premature admission before native intake, a
first-source unbound actor denial, an early pre-event/pre-bind claim fallback,
sticky slot ownership across native-only replacement and admission expiry before
native projectile lifetime. Old257-world expiry is not used for admitted sources.
The first attempted tool build after main advanced correctly failed against the
old DLL's missing Traversal enum; no runtime result is claimed for that mismatch.
Latest main was then integrated cleanly and all final tools rebuilt against it.
The first final fidelity command omitted the isolated user-data paths file and
reported content unavailable after its content-free checks. That raw exit1 is
preserved in the broader native metadata and is not the46/47 result. The separate
`fidelity-content` rerun configured operator paths explicitly, ran all47 cases
and confirmed46 passes plus the exact recorded divergence in3.276s.

Native fidelity still has the exact established missile.splash tick177
victim.position4096.z52814/52815 divergence. Native Imperialist headshotF2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No native IDs, packet layouts, movement rules or stored tuning fields change in
this slice; protocol68 comes from upstream Traversal. Review/CI are not awaited
or claimed passing, as requested by the user.

Rollback: revert this slice; developer custom flags remain off. CW-05 remains in
progress: public selection/input, local native-fire suppression, HUD, optics and
presentation follow next. Physical Android/controller/touch, rendered views,
60/120/240Hz device cadence and WAN acceptance remain NOT_RUN release gates.
