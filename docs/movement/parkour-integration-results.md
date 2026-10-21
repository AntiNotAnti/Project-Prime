# Parkour integration and remaining acceptance — 2026-10-21

The authorized mantle and grapple implementation sequence is delivered in separate merged PRs: [#942 static mantle](https://github.com/AntiNotAnti/Project-Prime/pull/942) and [#947 static grapple](https://github.com/AntiNotAnti/Project-Prime/pull/947). This audit closes implementation and documentation; the hardware, rendering and performance gaps below remain explicitly unqualified. Earlier PR-00–09 and later movement results retain their original source leases.

## Current behavior and integration

Classic remains the default in ordinary practice, host rules and missing/old settings. Parkour is the existing optional match-wide choice, model4/modifier bit19. Its mantle uses fresh Forward+Jump, conservative complete standing-body route and landing support, native velocity/collision, bounded rise1.25/travel2.5/duration24ticks, with safe abort. Dedicated G requests a safe static grapple with authority-selected geometry online, owner forces only after matching lifecycle/nonce acceptance, immediate release and at most180ticks. Both preserve native ability, form, gravity, Jump, pads, teleport and lifecycle priority. No controller writes Position/PrevPosition; no replacement collision or owner-trajectory reconciliation is introduced.

Simulation remains fixed60Hz. Human owners report movement; authority owns bots and grants grapple anchors from jointly accepted current-life pose/aim. Rejected poses do not refresh accepted-input lifetime. Validation shadow remains advisory. Remote and passive replay consume accepted state without grappling queries/forces. Conservative query saturation and actor blockers refuse admission. These static guarantees are supported by the named pure/native/authority/network tests, not a universal map/timing proof.

Current identity is protocol82/Parkourv5, decoder checkpoint14/world11. Protocol80/81 retain v4,79 v3,78 v2,77 v1. Protocol81 independently introduced exact weapon-package preparation; its bit20/packet IDs99–101 are preserved. Custom/package recordings remain refused. Fast snapshots retain their138-byte player prefix; accepted rope state uses the19-byte canonical/slow append and request nonce2-byte intent trailer. Full packet ceilings and literal historical widths are checked in the [v5 contract](../network/parkour-v5-version-contract.md).

## Evidence and limits of each lease

| Lease | Qualified scope | Boundaries |
| --- | --- | --- |
| [Mantle results](parkour-mantle-results.md), full `1c8f9806abe6060cff38ede79502e6a425303a67`; integration `994902416c2a8f4a9b86dfcd4ce7caa3f1ce27fa` | Normal Windows client/nettest/server;18 pure gates;7 native gates;56 unchanged Classic trajectories; full clean/impaired two-owner/native-bot sessions; retained80 and replacement77–79 playback | Full source incident and failed impaired attempt retained; selected retry fenced. Earlier controls are not refreshed by later builds. |
| [Grapple results](parkour-grapple-results.md), full `c82b1cf0df6c8dd57815adb24c45244a50fc7a57` | Normal Windows builds;27 pure gates;9 native gates/558,773 assertions;56 unchanged Classic trajectories; separate9-check coordinate-boundary helper; clean/impaired authority+two-owner+native-bot sessions; active/released world capture, restore and seek | CPU replay and synthetic inputs; no new GL raster or physical-device acceptance. |
| Grapple integration `0dacb2fd95bdfd7f4001148dbc8189e57a4d53b3`, main `123df20e3737f672048309310f18c8c7f9428cd9` | Same movement/network/replay bytes; new normal builds;27 pure and9 native gates; all 15 replay-workflow contracts;12 exact archive comparisons; supporting tools | Main's character-source rendering changes are integrated and compiled; their own GPU evidence is separate. No second integration UDP cohort is claimed. |

Exact source/binary/archive hashes and selected facts are published in [mantle selection](evidence/parkour-mantle-selection.json) and [grapple selection](evidence/parkour-grapple-selection.json). All runs use committed source with empty overlays and separate candidate/scratch directories. No game assets or raw recordings are published; full publication closures pass the no-game-assets guard.

Ten old77–81 archives compare300 exact semantic fields. Grapple adds exactly524bytes/world capsule; both size fields are intentionally different and checked separately. Two actual current82 archives compare37 exact fields each, including accepted-grapple facts and unchanged sizes. The original six77–79 archives disappeared externally; replacements from exact historical producers prove decoder compatibility only. Newly captured81 fixtures have the same explicit status. Lost original archive continuity remains unverified. Existing80/82 recordings and provenance are backed up and fenced. There is no durable-seek claim for these fixtures.

## Hosted CI report

CI is reported at exact heads and merge time, without waiting for completion under operator authorization. No all-green claim is made. See the [CI receipt](evidence/parkour-integration-ci.json) for timestamped status and links.

Mantle publication `7d9b4027bca1f988a23daa93683bb0936d2f013d` was queued at merge. Subsequent failures exposed a stale Traversal profile assertion and missing source-linked Android capability dependency. Main separately repaired the profile assertion; grapple retains the currentv5 expectation and fixes the tool dependency. Source-only Android capability checks now pass735.

Grapple's earlier `2a4f008af59dd167e4824672bf330356a2306f19` failed an incorrect package-recording acceptance fixture; correction preserves production refusal. Head `ac7b49749eac0af9acc96055923a440536e457a0` passed hosted Windows/Linux math and the no-game-assets job but failed Windows/Linux archive contracts because timed-shot tests still expected current writer12/13. Integration `0dacb2fd95bdfd7f4001148dbc8189e57a4d53b3` repairs that fixture while explicitly restoring old12/13 layouts; all 15 workflow gates pass locally. A successful local workflow is not a claim of successful hosted CI on another SHA.

The hosted build job named “no game assets” subsequently fails later in native Settings acceptance on the integration head: its fixture still expects the Trooper preview to be unsupported after main#948 added support. The actual asset check is not the failing step. This final integration PR changes only that fixture's missing-bundle expectation, including deferred adoption, and retains its no-render-request, ownership and retirement assertions. Isolated Settings verification is separately attributed below; full live-menu remains blocked by the Windows theatre symlink fixture. Production code and qualified movement/network/replay bytes do not change.

## Final fixture qualification

Exact committed producer `9868bfeec772e75dbf33a9ffd34e70670879853d` contains 6,520 files with an empty overlay, based on grapple merge `830fa8407530a003b8eecd3c7eec8b7ac86c31ee`. All 2,228 production `src/` files match the qualified integration producer byte-for-byte. The repaired tool builds from a separate exact source scratch copy against a byte-identical copy of that normal engine, without rebuilding project references. Its supported `--native-settings-acceptance-isolated` child exits0 with 263 logged assertions: 152 Settings and111 character preview, including both missing-Trooper-bundle refusal and deferred adoption. Frozen/scratch source fences pass; all16,650 files in the original producer05 candidate remain unchanged. Exact archive, manifest, tool/engine hashes and log hashes are in the [CI/qualification receipt](evidence/parkour-integration-ci.json). No scratch attempt failed. Publication adds this verification record only.

This isolated Settings result qualifies the repaired fixture. The complete live-menu invocation remains blocked by the unrelated Windows theatre symbolic-link requirement; no complete-menu pass or GL/device coverage follows. [PR #949](https://github.com/AntiNotAnti/Project-Prime/pull/949) contains this final audit and fixture repair; its hosted results are separate from the local qualification and earlier PR heads.

## Remaining acceptance ledger

| Item | Status and next acceptance action |
| --- | --- |
| Physical controller, touch, mixed input and focus recovery | UNVERIFIED full device matrix. Current binding IDs/defaults and controlled input boundaries pass; exercise real devices and focus loss/recovery separately. |
| Rope GL raster, theatre/export/killcam presentation | NOT RUN for this slice. CPU accepted state and world capture/seek pass; exercise normal production GL and presentation paths with actual accepted ropes. |
| Full live-menu tool | BLOCKED by Windows symbolic-link privilege in its theatre fixture. Earlier checks pass; complete tool does not. Run in an environment with the required privilege, preserving ordinary menu behavior. |
| Android APK/current runtime/physical device | BLOCKED local APK build: android workload absent (NETSDK1147). Source-only735 passes. No workload or device acceptance inferred. |
| Current Linux asset-backed runtime | UNVERIFIED. Hosted content-free compilation/contracts are a separate scope. |
| Achieved display refresh and sustained budgets | UNVERIFIED; synthetic cadence and policy tests do not measure achieved refresh. |
| Performance threshold / optimization | OPERATOR-DEFERRED. Preserve original5% threshold failures, inconclusive controls and unequal-work diagnostic timings. Do not relabel as a pass or repeat timings for favorable results. |
| Hosted final green CI | OPEN wherever the exact-head receipt is queued/in progress or failed. Known movement-related fixture failures are repaired and locally verified; no waiting or blanket green claim. |
| Original77–79 archive continuity | UNRECOVERED. Replacement fixtures are clearly labeled and cannot prove the lost hashes/digests. |
| Every map, population, mode and simultaneous online event | UNVERIFIED universal coverage. Named authored/native/UDP controls pass; earlier Nodes capture, advisory displacement and other historical limitations remain in the [acceptance matrix](acceptance-matrix.md). |

## Rollback

Select Classic for the next match through normal rules to disable optional Parkour gameplay; match rules remain frozen during play. A code rollback must be coordinated across live peers because exact protocol identity is required. Keep protocol77–82 profile mappings and historical decoder/world readers, or explicitly refuse unsupported archives. Never reuse82 for another meaning, reinterpret82 as v4, or reinterpret81 package archives as the unpublished grapple draft. Preserve recordings and receipts before reverting. Reverting optional movement code does not remove unrelated character/material/package work merged on main.
