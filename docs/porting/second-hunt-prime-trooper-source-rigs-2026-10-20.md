# Prime Trooper full-source authoring bundles

This continues the separately verified Prime USA Rev 2 import from PR873. It gives the existing private importer a complete temporal ANIM bank, exact CINF and generated source-space GLB for each of the four Trooper appearances. These are authoring outputs with diagnostic colors. They do not activate the runtime viewer, native actor, source materials, first-person gun, wall cling, drone, blaster, selection, multiplayer or replay.

## Existing owners and exact source contract

The change reuses `RetroCharacterAnimationArchive.Build/Read` with its existing MP1 decoder (`echoes: false`), the existing source graph proof, `RetroCharacterSourceGlbWriter`, and transactional `LocalImportBundle` publication. It introduces no second animation decoder, renderer, clock, asset store or publication protocol.

The owned-source inventory established a real distinction from Echoes: each Trooper CINF has **35 bones**, sole source root **3**, external parent **2**. The external parent is not a real retained joint to invent or remap. The new explicit `WritePrimeTrooper` entry point requires CMDL version 2 and exactly that hierarchy contract, then uses the same detached-input, finite-value, cycle, complete-influence, corner/material and output-budget checks as the existing writer. It retains all source joints and positive weights without a 32-joint truncation. The GLB has the 35 real joints and a separate identity mesh node, with source-root metadata naming this contract.

The original `Write` API retains its external-parent-97 contract. Its default JSON structure, property order, diagnostic pixels and geometry encoding are unchanged. No generic caller can select the Prime contract by changing a runtime manifest or appearance flag.

## Versioned publication and migration

Only the exact Prime USA Rev 2 archive profile advances to **bundle v3, body v2**. The original Prime v1 recipe/API and all Echoes recipes remain unchanged. `PartsFor(id)` still describes the original edition; its new edition overload and receipt-aware part inspection distinguish the richer Rev 2 body.

All ten files are required in one committed generation:

| Output | Purpose |
|---|---|
| `.obj`, `.skeleton.json`, `.weights.json`, `.motions.json` | Existing authoring geometry, source hierarchy, unmodified source weights and sparse motion inventory |
| `.cinf.bin` | Exact retained source skeleton |
| `.animations.bin` | Part-bound complete raw ANIM resource bank, preserving full temporal keys |
| `.source.glb` | Complete source-space hierarchy, bind data, geometry, weights and diagnostic material ordinals |
| `.source.json` | Source-only writer, root, resource and generated-output provenance |
| `.ancs-graph.json`, `.ancs.bin` | Required exact graph proof and raw source snapshot |

An existing Rev 2 v2 receipt reports Outdated against the new recipe, with expected body v2. Inspecting it explicitly against its original v2 specification still reports Ready; Check does not rewrite it. Import stages all selected parts before publication. Cancellation and failures retain the prior current receipts and immutable generations. Successful update publishes a new complete generation; retained old generations remain available to existing consumers.

The source-only provenance deliberately omits runtime `characterId`, `skinSource`, `nativeHunter` and `presentationPart`. It sets `runtimeInstall: false`, binds the exact source edition/fingerprint, CMDL/CSKR/CINF/ANCS/graph/bank/GLB hashes and root contract, and names unfinished material, first-person, gameplay, selection/network/replay qualifications. No `characters.json`, cannon recipe or runtime capability is generated for Troopers. Existing runtime source-selection and qualified Echoes admission remain closed to them.

## Acceptance boundary

Frozen production/test source is `c66f444a4ce8daca6bd55a3b8fb82addae9e130c`, based on merged PR873 / inherited protocol 75. This slice adds no wire or replay fields. Windows remains the preferred test host, with user-authorized Mac fallback when authentication is unavailable.

Full resource admission is not original-hardware animation fidelity: named composite/transition/event behavior, source-space runtime pose composition and actual rendering still need their own acceptance. The bank retains all graph-referenced ANIM leaves; it does not execute a named composite by substituting one leaf. Diagnostic GLB colors do not represent source skins/materials. Full-source authoring does not qualify playable characters.

The source-free suites pass **123 recipe/migration checks** and **966 new Prime writer checks**, alongside the existing 421 default writer and 50 generated cannon checks. The Prime fixtures independently read GLB/BIN and verify every joint, local/global rest bind, inverse bind, corner attribute, weight, material and source triangle ID. They also test cross-contract refusal, malformed rigs/geometry, dictionary-order determinism, detached inputs and cancellation. Runtime model admission is not part of those fixtures.

The actual owned Prime probe passes **182,511 assertions**, including repeated per-corner/key checks rather than that many independent scenarios. For each appearance it independently verifies all 1,970 source triangles / 5,910 emitted corners, 35 real joints plus identity mesh, all positive source weights, and exact resource/provenance bindings. Each bank retains 142 unique ANIM leaves and 167 roots, of which 130 are declared by the selected character and 113 are strict zero-time primitives. Across the leaves it verifies all 3,556 temporal keys plus off-grid/end samples (3,840 sampled poses per appearance), 508,624 scalars and zero foreign bone channels. Composite/nonzero roots remain refused by primitive playback.

| Appearance | Generated diagnostic source GLB SHA256 (373,960 bytes each) |
|---|---|
| `trooper-power` | `54f298805409fa12f34c34203cf7db2f5085f5b5ec4410ac04b3d329da71f3e8` |
| `trooper-ice` | `c3f7c8f9c77824937522db1199970a5f9d0be94e8dbfdbe35a66b7c5adfac307` |
| `trooper-wave` | `6a307e91a5bfdd82a0f1ae904b3179917fc7f3da70e2c3546ae2feffec32653b` |
| `trooper-plasma` | `7877c9f7b7a27dc8c804c04ea5dc03e364504e71f3c7e744d58e95e688e4e96f` |

The migration probe seeds four valid previous-v2 receipts, then cancels a real full-source Power export at `BeforeCommit`. Every old current receipt and component digest survives. A subsequent actual four-appearance import upgrades all four to v3 and retains every old immutable generation. Reimport reuses all new receipts/generations/components; precancellation preserves them, and removal withdraws only discovery. The actual runtime selector still refuses all four Troopers, with model caches unchanged. No runtime manifest is published. Source/core/tool hashes remain unchanged and all disposable derived data is deleted.

The complete owned Echoes body/ball suite also passes. Twelve emitted proofs from its update/repair/cancel/remove cases retain all four prior GLB profiles exactly: normal body `38b03806faf22851efd3caeb4fcd22b900556b5fc4b21a2051b075b63f6fb8ca`, Overcharged body `ff1866fd7c92c7aad0326381e059119febd0993d63aacc85b45400b019af5164`, and shared ball `eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9`. The initial comparison wrapper incorrectly expected only four emitted proof rows; it was corrected to verify every repeated row and all four distinct profiles. This was a metadata-count assumption, not a writer or source-suite failure.

All **21 frozen serial qualification commands passed**: three native-runtime verifications, desktop and dedicated Release, RML pages, importer/writer/cannon/settings/game-files suites, character-model checks, actual four-Trooper import, both owned Echoes body/ball source rigs, owned Echoes document import, standalone Android Debug, signed Android Release/AOT and signature verification. Desktop/dedicated each report one inherited unassigned test-field warning; Android each reports 26 inherited warnings. All builds have zero errors. Release contains 119 AOT libraries per ABI and passed APK signature verification. Windows SSH failed with exit255/authentication denied, so these use the authorized Mac fallback.

All **6,165 tracked files in each of two fresh archive trees** remain unchanged. Source archive SHA256 is `e2dd1522537f90f360e9bef5371540edf2dc1d6d06267590da4aa7cd5f830e5c`. Full local command/environment/exit/source/binary receipts are under `artifacts/sh-integration/prime-full-source-final-c66f444a4/`; build root `/private/tmp/sh-prime-full-source-final-c66f444a4-foyh5p4u`.

| Artifact | SHA256 |
|---|---|
| Desktop core | `9971265df1f2a4b3bc7d8feb0888992322ae9c571dd20bfb990c9eed804c4797` |
| Dedicated core | `238d4519552044517505337fa4b0560415b9ddc78302cd926aa40db6423f00f4` |
| External import tool | `d4fd6202b25b09e5ea2f33651010c09406f2a3ef15abf9031b6728500c3d0596` |
| Android Release APK | `7fea1d14d35d3f9f05a1dc8bbd6438b9b6f481acaa25137f0a79ac83e68dcea4` |
| Standalone Android Debug APK | `8966a279ec1f6623e12b5f7bd50f8acae955b8fbafdaa743e98c6d9381c0b83e` |

Rollback restores the prior Rev 2 v2 authoring recipe and removes the explicit Prime writer route. Old immutable generations are retained; no source or user save is deleted. The existing original Prime and Echoes recipe identities remain their own rollback boundaries.

## Actual Android application and bundle upgrade

The existing API36 arm64 emulator ran only `com.projectprime.sourceaccesscheck`. The frozen old v2 APK and new c66 APK have the same debug certificate. The test imported all four appearances with the old app, installed the new app using `install -r` and verified that all old receipts and components survived unchanged. All four new-app Checks then reported **Outdated, expected body v2**, without rewriting the previous bundles.

The actual system chooser/ContentResolver/default-importer group update published all four **bundle v3/body v2 Ready**, with ten required files each. Repeating the same group import preserved every receipt hash, generation and component record. Complete receipts plus **128 independent on-device length/SHA256 checks across four stages** are retained. These were exactly three group imports: old v2, v3 upgrade, and v3 repeat. Plasma Remove Cancel preserved its receipt; Confirm withdrew its current selection and left the other three receipts unchanged. Old/new immutable generations remained until isolated-app cleanup.

Nine of ten components per appearance match the frozen desktop bytes exactly, including all four newly added files: full animation bank, raw CINF, source GLB and provenance. Only the existing sparse `motions.json` differs, by 18 extra bytes on Android, as in the preceding v2 slice. Its cause remains unknown. Whole-generation/motion-byte and decoded-pose parity are not established by the matching source containers.

One temporary capture contained only our Profile/calibration UI, with no native/source game model or texture visible. It directly confirmed **“Imported 4 private appearance bundle(s). These are not playable characters.”** and was immediately deleted. The repeat completion was also observed through UI automation. Earlier stale accessibility text did not represent the visible completion state; there is no confirmed view-lifecycle defect from that observation. Progress text during copying was not directly observed. Two premature receipt-collection attempts ran while copying was still active and remain recorded as harness-timing attempts; final collection waited for scratch cleanup and complete receipts.

The original and emulator-copy source SHA256 remained `3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844`. Both host APK hashes remained unchanged, and the installed new APK matched the frozen Debug hash above. The isolated UID crash buffer was empty. The unique RVZ, MediaStore entry, test package, internal/external app data, scratch and all derived payloads were deleted and absence verified. No source screenshots were taken; the single own-UI capture was deleted and no PNG remains. Main-app data and emulator configuration were untouched. The 156 primary ADB commands, receipt/component checks, UI observations and cleanup are retained in `artifacts/sh-integration/android-prime-full-source-c66f444a4/summary.json` and adjacent metadata logs.

This qualifies this emulator import/upgrade path. It does not qualify physical devices, cloud/SD providers, broader crash scenarios, source rendering, gameplay or source-animation runtime pose composition.

## Merge identity

[PR878](https://github.com/AntiNotAnti/Project-Prime/pull/878) merged as `2ed2da6862ee63761e4cae84f7320bd0933fc63f` at2026-10-20T18:07:04Z after the scoped local qualification above. CI/review was not awaited, as authorized. This merge records source authoring; source materials, runtime Trooper animation/gameplay and selection/network/replay remain unfinished.
