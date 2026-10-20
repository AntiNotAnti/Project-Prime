# CW-05d evidence — public custom input and local native isolation

Base SHA: `09aba1f57c05a6e9ad46b667b188b1dd99d0f1ac`, including upstream
credits timeline and crouch/sliding changes. Protocol69 is inherited from upstream.
Head SHA: the accompanying CW-05d slice commit. See [contract](../05d-public-input.md).

Environment: macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Main command: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -m:1 -p:UseSharedCompilation=false`.
Integrated main build PASS62.83s, zero warnings/errors. DLL SHA256:
`c8849f01d4b5c57da250f947f7041a4b2634622fd8e2549bd95ea081b1f031ea`.
Main sources were frozen during compilation and acceptance. Tools rebuild with
`BuildProjectReferences=false` against the qualified DLL. Exact commands,
environment, timing, raw exits and main/copied DLL hashes are recorded under
`artifacts/custom-weapons/CW-05d/`.

The earlier candidate records qualify their distinct DLLs, not this integrated
base. Main advanced during acceptance; integration preserved Crouch action33 and
appended custom actions34/35/36. New native crouch and stance assertions are
retained. The integration stash is retained until the completed slice is committed.
Fresh integrated nettest/custom builds PASS3.906s/2.185s, zero warnings/errors;
final fixture framing rebuild PASS2.103s, zero warnings/errors. The27 integrated
nettest commands took142.287s (26exit0 and the exact known fidelity exit1).
All final custom acceptance commands exit0.

## Acceptance

Final integrated results are recorded in `05d-integrated-custom-results.json`,
`05d-integrated-nettest-results.json` and
`05d-integrated-main-input-results.json`. The latter records pointer1790 PASS and
the full `-gamepadcheck` raw exit1: its launcher UI diagnostic reaches
`PrimeUiChecks.CheckStartup:1002` and throws `Sequence contains no elements`
while locating a startup screen. Input checks execute before that failure; the
separate focused controller-enhancement command qualifies their assertions
without claiming the full launcher diagnostic passed. The same failure persists
with operator content paths explicitly configured. No launcher production code or
assertion is changed to accommodate it.

| Integrated acceptance | Result |
| --- | --- |
| Public input client boundary / focused controller enhancements | PASS24/75 |
| Pure custom suite / local keyboard-mouse scene | PASS14311/28 |
| Native selection / client projection | PASS180/145 |
| Existing replica/readiness/admission/source/authority/offline/rewind scenes | PASS173/127/249/318/985/46/156 |
| Native custom-isolation / actual client runtime UDP | PASS680/75 |
| Authority acceptance / reconnect / public replica UDP | PASS1630/371/650 |
| Atomic input / reliable controls / action transport UDP | PASS713/1061/1551 |
| Readiness / source-boundary UDP | PASS134/89 |
| Touch / headless controller / pointer | PASS155/17/1790 |
| Crouch input / stance rules / network | PASS75/193/122 |
| Actual stance / stance authority / seven-Hunter movement | PASS55605/267/7520 |
| Native tables / admitted remote / Imperialist | PASS;343/949 |
| Native fidelity | 46/47; established divergence, raw exit1 |

Native fidelity retains the exact established `missile.splash` tick177
`victim.position4096.z` expected52814/actual52815 divergence. The independently
qualified Imperialist headshotF2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
These native records are in `05d-integrated-nettest-results.json`; fidelity has
operator content paths configured and runs all47 cases.

The local scene fixture drives synthetic keyboard/mouse samples through actual
`Scene.OnSimulationFrame`. It checks current-step movement and gun aim, ordered
optic/fire, native charge/payment/emission suppression, held-fire refusal,
canonical reload, same/different native selection, wheel-release selection and
native fire restoration. A real accepted custom flight survives Unequip and
resolves room/player collision with typed provenance. Typed queue probes qualify
expiry, morph, death and respawn between submission and completion. These probes
are distinct from hardware acceptance.

The public client fixture uses an actual asset-backed ordinary client actor,
`NetHooks.AfterInput`/`AfterSimulation`, canonical request producer and actual
91/93 UDP through a bounded transparent loopback proxy. It proves lifecycle
adoption precedes custom sampling, exact Equip/Optic/Fire/Reload/Unequip ordering,
physical Shoot/Zoom retention, unchanged canonical4/16 resources and health, and
new-life sequence1 after stale-life refusal. Native snapshots, private canonical
state, public baseline, no-source results and control edges are explicit synthetic
facts. No remote engine/source certificate, authoritative damage or dedicated
combat E2E is claimed. Existing engine UDP suites qualify those separate boundaries.
Its first two runs correctly refused malformed fixture transport: Snapshot is a
fast lane and its payload requires native time/health tails. Only fixture framing
was corrected; production validation and adoption assertions remain intact.

Pure controller/projection tests cover full owner/bootstrap/manifest replacement,
readiness loss, exact denials, reordered results, ambiguous Equip chains,
capacity/queue refusal, hold edges and fixed-step cadence. Resources remain
canonical; prediction cannot write ammo or health. Warmed controller/projection
allocation probes retain their recorded0B result.

Initial preflights found inaccessible private player input, two fixture API names
and a fixture map spawn obstructed by a side wall. The implementation uses a typed
player helper; the test uses actual API names and explicitly relocates its body
above that wall before requiring measurable movement. The original strict
postmovement muzzle/current-aim assertions remain. Review repaired offline work
executing before fresh aim, queued old-life work crossing respawn, and stale
native suppression across lifecycle/scene retirement. Failed attempts and
pre-integration results remain in their separate logs.

Physical controller/Android, GL/rendered scope/HUD and WAN tests are NOT_RUN for
this slice. Public magnification/sensitivity, HUD, settings for hold scope and
visual/audio prediction follow in CW-05e. Full CW-05 and release acceptance are
not complete. Developer custom feature flags stay opt-in. CI/review are neither
awaited nor claimed passing, following the user's instruction.
