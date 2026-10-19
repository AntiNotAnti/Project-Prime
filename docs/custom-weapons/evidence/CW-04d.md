# CW-04d evidence — nominal provenance and native join isolation

Base: `a3a73617e` (latest main fetched and fast-forwarded before the final build).
Tested implementation: `9d0ac3f1a`; the following commit adds only this evidence
and the ledger entry. Environment: macOS arm64, .NET SDK 10.0.401;
`DN=/Users/jarrett/.dotnet/dotnet`.

Scope: [nominal provenance boundary](../04d-provenance-boundary.md). Custom sources
retain full persistent weapon/owner/sequence identity and, for accepted-launch
metadata, the exact action and certificate. Native shot identity fields remain zero.
Explicit guards prevent custom sources entering native proof/payment/replay/report
and witness joins. Live custom damage remains denied; this is not custom network
combat and CW-04 remains incomplete. No wire, protocol, native table or replay layout
changes occur.

| Check / command | Actual result | Log in `artifacts/custom-weapons/CW-04d/` |
| --- | --- | --- |
| `DOTNET_PROCESSOR_COUNT=2 $DN build src/MphRead -c Release -p:UseSharedCompilation=false` | PASS, zero warnings/errors; 137.32 s against latest main | `build-latest-main.log` |
| `$DN build tools/custom-weapons-check -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false` | PASS, zero warnings/errors; 3.03 s | `tool-final-build.log` |
| Fresh custom check binary without arguments | PASS, 2,105 contract checks | `contracts-final.log` |
| Same binary `--scene <isolated CW-03 content-data>` | PASS, 46 real offline rifle checks | `scene-final.log` |
| Same binary `--rewind-scene <isolated CW-03 content-data>` | PASS, 155 asset-backed scope/isolation checks | `rewind-boundary-final.log` |
| Native tuning snapshot against unchanged CW-00 | PASS | `native-tables.log` |
| Fresh nettest build; architecture, protocol58-replay, shot-source-journal, weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage, instagib-ion-fracture | PASS each | Named logs, `native-results.json` |
| Nettest `--timed-shot-accepted <content-data>` | PASS, 343 native assertions | `native-accepted-scene-final.log` |
| Nettest `--timed-imperialist-impact-scene <content-data>` | PASS, 949 native assertions | `native-imperialist-scene-final.log` |
| Game fidelity oracle, all cases, unchanged baselines, content enabled, 60 Hz | 46 PASS; one existing divergence unchanged, exit 1 | `native-fidelity-final.log` |
| `git diff --check` | PASS | No whitespace errors |

The offline scene proves a real emitted custom beam carries typed provenance while
all native identity fields stay cleared, with existing collision/health checks
preserved. The ServerSim isolation additions deliberately forge colliding native
metadata on a custom beam, seed explicit native reservations and nest witness scopes.
They demonstrate rejection, immutable native outcomes and context restoration with
optional proof both on and off. They do not emit or pay a live custom shot. Existing
real rewind restoration/private-pool processing checks remain intact. Enemy/platform
pool alias branches remain explicitly skipped in this room, as in CW-04c.

Review exposed a witness boundary bug when optional native policy was off: custom
segments must still clear any outer native segment. The final source fixes that
case and its regression passes. An intermediate run against the older binary failed
that new check; it is not represented as final evidence. One superseded local build
was intentionally stopped before changing the source layout. The final reference
layout built successfully on the preceding main (933.92 s during heavy local load)
and again on the latest main as reported above.

Measured layout: nullable custom provenance occupies **8 bytes** per native carrier
on this 64-bit runtime. The rejected inline struct layout measured 328 bytes.
Accepted custom source construction costs **336 bytes/source**, measured across
1,024 retained objects with the holding array excluded. It occurs per accepted
custom emission, not ordinary native fire. **100,000 warmed existing-source matches
allocate zero bytes**; the existing 100,000-action source-journal allocation check
also passes. These are narrow measurements, not whole-game percentile/device claims.

Native fidelity remains exactly the known CW-00 issue: missile splash tick 177,
`victim.position4096.z`, expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No baseline was regenerated or tolerance changed. CI was not awaited or claimed PASS,
per the user's instruction.

NOT_RUN: live custom authority/emission/payment, authenticated custom UDP/readiness,
source-history recovery, mandatory custom trade/lifecycle integration, custom
snapshot/reconnect/spectator outcomes, custom replay, GL/audio, WAN/device acceptance
and whole-game performance. Next: implement and exercise the separate custom payment
and authority path before CW-05. Rollback: no live caller is enabled; offline developer
admission remains explicit and native gameplay remains the default.
