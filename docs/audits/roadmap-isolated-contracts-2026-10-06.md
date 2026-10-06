# Isolated final contract validation — 2026-10-06

The complete `tools/check-engineering-contracts.sh` passed with exit 0 on macOS/M4 Pro from a canonical private Git archive of `c63baa17e7b3663501db457895468127d329f5bc`, tree `dc5a403c3d3e132d9348dd17c2803bcc874b7583`. Archive SHA-256: `bc772ec5b9a95804ad125333ddb8157977db44b742af8f407e598b4dbd17925e`. The archive's tracked file blob IDs and SHA-256 hashes were recorded before the run; all tracked bytes remained unchanged afterward. Build outputs, restores, network test fixtures and writable user data were isolated from shared outputs and the original checkout.

| Validation | Result |
|---|---|
| Python telemetry/dedicated supervision/release policy; Node Edge handler contracts | PASS |
| Release client | PASS, 101 warnings / 0 errors, 56.90s |
| Replay control / replay format / character model / fixed-frame timing | PASS; format 2957 checks |
| Authority / architecture / input edges | PASS; authority 471 assertions |
| Real UDP control plane: lobby lifecycle and exact custom-map readiness | PASS 7736 assertions |
| Weapon timing / health and shot policy | PASS 1620 timing profiles; 3,338,739 assertions |
| Eight mixed-quality UDP peers and client pump stall | PASS; 1720 intents, 128 exactly-once controls |
| Updater publication exceptions, actual killed-child recovery, file/directory shape changes, mapping preservation and containment | PASS |
| Render cleanup, atlas churn, texture transaction/capacity and music ownership | PASS |
| Authored KTX2 RGBA mip decoding/identity/capping/residency | PASS |
| Production frame trace definitions / desktop and Android pacing policy | PASS; trace 9 checks |
| Replay timeline ordering/allocation/pool lifetime | PASS 36 basic + 40 allocation + 43 lifetime checks |
| Map roadmap / detached replay preparation | PASS 108 / 24 checks |
| Release dedicated server | PASS, 37 warnings / 0 errors, 19.66s |
| Actual server missing-assets prerequisite refusal | PASS 8 checks, exit 0 |

The actual executable smoke starts its own directory server on ephemeral loopback UDP, runs the dedicated server with isolated missing-game-data paths, checks replay retention flags, verifies the dedicated process genuinely exits 1 with an actionable refusal, and confirms that the directory answers with no refused server listed. The smoke owns and tears down only its exact process trees.

Evidence directory: `/private/tmp/prime-final-contracts-c63baa17-7gkvg0rc`. It contains `source.tar`, `manifest.json`, `contracts.log`, `dedicated-smoke.log`, and `results.json`. The macOS-only copied native dependency was the existing ignored KTX library, SHA-256 `b51cabe670cfe679d09c25e2eca4e1a0a7d4e379496cd38c34ae6aa4e3ead837`; no game assets were added.

## Final narrow delta

Final source object `1db7079934ce771a079fcaf30ff556a94b038b89`, tree `c2b381992d6016ea631e26c3eb31d1e6d63dba9c`, changes three product/fixture paths after the complete broad pass: `NetAcceptedAttacks.cs`, `NetAltHitCheck.cs`, and `ReplayLegacyRangeChecks.cs`. Remaining changes are documentation. Exact final Git bytes were overlaid into the private archive; every tracked file was then checked against its final Git blob ID with zero mismatches. Final archive SHA-256: `d827753a91f3899760a21edfe35495bb11dc6473554cc79c31b7eb7a29e2b0da`.

The first narrow delta passed the restored client build (101 warnings / 0 errors, 34.84s), authority policy 471, dedicated build (37 warnings / 0 errors, 20.43s), and actual refusal smoke 8. An initial `--no-restore` client attempt had reused server-only conditional restore assets and failed because Avalonia was absent; its log remains `final-client.log`. The ordinary client restore/build resolved the build-order issue without a source change.

## Final transport lifetime delta

Final product/test object `1c6a87c504c41f802c8fbcf300430858bbbcfb3e`, tree `49bff1793cea1a9b0568f0add6df67f176d30f8c`, adds the stable native Socket reference used by receive/send during disposal, 32 immediate real UDP transport disposal cycles, and the native alt-contact fixture's per eligible victim counter expectation. Exact final Git bytes were overlaid into the private source; every tracked file matched its final blob ID before validation, and all tracked SHA-256 hashes remained unchanged afterward. Final archive SHA-256: `fc56bbe4330150e25890ec3ce026a7cf5085d850feb6248182f7cec17c2f5c99`.

| Final isolated delta validation | Result |
|---|---|
| Ordinary Release client restore/build | PASS, 101 warnings / 0 errors, 104.93s |
| Authority/admission policy including rapid disposal | PASS 503 assertions |
| Eight mixed-quality UDP peers +400 ms client pump stall | PASS 1712 intents, 128 exactly-once controls |
| Ordinary dedicated restore/build | PASS, 37 warnings / 0 errors, 23.89s |
| Actual executable missing-assets refusal | PASS 8 checks, exit 0 |

All five commands exited 0. Evidence: `transport-source.tar`, `transport-manifest.json`, `transport-client.log`, `transport-authority.log`, `transport-stress.log`, `transport-server.log`, `transport-dedicated-smoke.log`, and `transport-results.json` in the evidence directory above. Build durations are observations from concurrent local validation; they are not runtime performance benchmarks. Unchanged broad contracts were not repeated after these narrow deltas.

Root separately reported the final clean client rebuild PASS 101 warnings / 0 errors, 54.75s (`/tmp/prime-roadmap-client-final-clean.log`) and all-current Android restore+Compile PASS 117 warnings / 0 errors, 31.32s (`/tmp/prime-roadmap-android-final-transport.log`). Java 27 version detection remains an environment warning; Compile does not execute a physical device. The network owner reported actual native alt fixture 995, three policy 503 runs, and mixed transport 1720 intents/128 exactly-once controls PASS on the final source. Those owner-run logs and wider native geometry/continuous/Spire/protocol evidence remain recorded in the network roadmap validation document.

These checks establish software/build/ownership contracts on the local macOS host. They do not establish physical controller behavior, Android surface rotation/background behavior, Windows/Linux GPU performance, GPU timestamps, display scanout, input-to-photon latency, release signing or external CI completion.
