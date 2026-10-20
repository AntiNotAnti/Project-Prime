# Prime USA revision 2 character source imports

This slice admits one independently verified source edition to the existing private authoring importer: GameCube Prime USA, `GM8E01`, revision 2, disc 0. It does not activate Trooper rendering, animation playback, selection, wall cling, drone, blaster, multiplayer or replay behavior. A Ready authoring bundle is not a playable character.

The user-provided RVZ is 1,159,009,496 bytes with SHA256 `3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844`. Source bytes remain local and read-only. Only identifiers, hashes, counts and test logs are retained.

## Source and receipt identity

`ExternalSourceRequirements.EditionFor` selects the new profile only for the exact header above. Export still verifies both complete archive requirements before reading character resources:

| Archive | Revision 2 SHA1 | Original recipe SHA1 |
|---|---|---|
| `Metroid1.pak` | `2147a5ea1ff654d6bbce749d5f7c1170f98a601e` | `1ae13e2325ed8b57462f7a39d9d7fac035745598` |
| `AudioGrp.pak` | `157e0841155cc7b699176dd7afc746c7048dbc28` | `157e0841155cc7b699176dd7afc746c7048dbc28` |

Header recognition alone does not verify an archive. Other headers retain the existing profile and cannot inherit revision 2 compatibility merely because some resources match.

All four canonical IDs retain their original version 1 recipes and the one-argument `BundleSpec` API. Revision 2 uses a distinct version 2 recipe per ID, binding the edition and archive fingerprint `c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0`. The original fingerprint remains `ae1541ffdf70bf87a49cac0f72422d69f32ef7ea`.

Each recipe requires its existing OBJ, skeleton, exact source weights and sparse motion inventory. The actual imports also publish the bound ANCS graph and raw ANCS snapshot: six committed files per appearance, four required authoring components, body contract v1. These do not include a full runtime animation bank, source-material program or gun.

`InspectBundle` first uses the original recipe. Only a structurally valid UnsupportedSource receipt carrying the exact known revision 2 fingerprint selects a second, complete inspection against the revision 2 recipe. That inspection rechecks version, recipe, generation and every component digest. Unknown sources, aliases, malformed receipts, cross-character copies and corrupt components remain refused. Android and desktop Settings Check use this same inspector; publication, cancellation, writer leases and removal remain owned by the existing importer and bundle store.

## Qualification scope

Production was frozen at `fc8d6f2fb2b62466dddf030b864fa0c893d50619`, based on merged PR865 / protocol 74. This slice introduces no wire or replay change. Windows SSH authentication failed with exit 255, so testing uses the user-authorized Mac fallback. Local command receipts are under `artifacts/sh-integration/prime-rev2-import-final-fc8d6f2fb/`.

The source-free edition suite passed 92 checks, including independent literal original-recipe goldens and strict edition/receipt/corruption refusals. The complete existing external-import suite also passed.

All **20 frozen serial commands passed**: desktop and dedicated Release, RML pages, three native-runtime verifications, external importer, generated character writer/cannon, settings archive and game-files suites, character-model validation, actual Prime import, actual Echoes document import, standalone Android Debug, signed Android Release/AOT and APK signature verification. Desktop and dedicated builds reported zero warnings/errors. Both Android builds reported 25 existing warnings and zero errors; Release contains 119 AOT libraries per ABI (`arm64-v8a` and `x86_64`). All **6,154 tracked files in each of two independent source trees** remained unchanged.

| Frozen artifact | SHA256 |
|---|---|
| Source archive | `0197756be8fe5f7390baa47b04a3c70bf1c8d2ad16c8a524fc312cdf694974ce` |
| Desktop core | `af9d471d6a5783a056a0850f2a304463d1cee107516277862d34cf5316b9a672` |
| Dedicated core | `1cd538c665f77892e470d226509b9eb9d966a06eef318b3ad2f14ba2c719ffd1` |
| External import tool | `2d7bd8db1a7e67f4c1a29771115841c99dfe4e32124d8a9e4795699bbe90d2c9` |
| Android Release APK | `a4c823508a0841d1d6e9da4686483390263ba423e8196547093db8461ad976a8` |
| Standalone Android Debug APK | `ab0ad0f2bafddd5c0b53cb744ea10a85f0402e5854845d12b981af8067e39934` |

Test-only follow-up `228c22e44d8664fafb29e1386235f5ff0b7c2385` changes the generic `--probe-resource` diagnostic to use edition-aware requirements too. Its production files are byte-identical to `fc8d6f2fb`. A separate frozen tool build and actual revision 2 ANCS-resource probe passed. Two preceding orchestration attempts failed because the isolated build lacked, respectively, the unchanged NcsfPlay reference assembly and core apphost; no source/compiler behavior failed. Both failed logs are retained alongside the passing `prime-rev2-diagnostic-228c22e44-attempt03` receipts. This test-tool follow-up does not replace the recorded frozen Android binaries.

The real source probe uses the default exporter, not an alternate extraction route. It verifies both archive pins and all six committed component lengths/digests for all four Troopers. Each appearance has 1,936 positions, 1,970 triangles and 35 source bones. Reimport reuses the same immutable generations and receipts; a precancelled import preserves them. Remove withdraws discovery while retaining all previous generation bytes until the disposable test tree is deleted.

| Appearance | Desktop generation |
|---|---|
| `trooper-power` | `fcc378f33949a86e4b2f5eacf802d24ac6106b83650d9970b8bc7b9eccfc0e46` |
| `trooper-ice` | `b7a0df313ac5931c3c637098b8e18f1d1efd9541170b6801e19045f0ce707b22` |
| `trooper-wave` | `6a198a35c0d4d4fdbfcb2a915b12e90b2b212a1ea04b8a63cb78d01c6c1b2b60` |
| `trooper-plasma` | `9daaa692a115413cec40bcff09b2e7edd8d214f200c8b7c6496ac278a72257ea` |

The owned probe reports 7,961 assertions, including repeated per-triangle checks; this is not 7,961 independent test scenarios. Source, tool, core assembly and model caches remain unchanged, and all temporary derived payloads are deleted. The existing owned Echoes document-import probe also passes with the normal recipe unchanged.

## Actual Android group import

The frozen standalone Debug APK ran in the existing API36 arm64 `emulator-5580`, isolated package `com.projectprime.sourceaccesscheck`. The test used the real Profile controls, system document chooser, ContentResolver snapshot and default importer. All four Troopers published bundle v2 and each Check reported **Ready, body v1 Ready**. Both original and emulator-copy RVZ digests matched the source hash above.

Complete current receipts were retained as metadata before cleanup. Every committed component was independently checked on-device for length and SHA256: 24 files after the first group import and the same 24 after reimport. Reimport preserved all four receipt hashes, generations and component hashes; scratch was empty after each operation. Plasma Remove Cancel preserved its receipt. Confirm Remove reported NotInstalled, retained its immutable generation and left the other three receipts unchanged.

The complete receipts also permit a bounded cross-platform comparison: geometry, skeleton, weights, ANCS graph and raw ANCS are byte-identical to desktop for every appearance. Each sparse `motions.json` is 18 bytes longer on Android and has a different hash, so each resulting generation differs. The cause is unknown; **cross-platform motion byte parity remains unproven**. Ready verifies each local container against its own receipt and exact source edition; it is not a runtime animation or network-content parity claim.

After chooser return, the automation sampled previous status text rather than progress or a completion message. Subsequent Check and group controls worked and recovered their enabled state. There was no explicit Settings close/reopen during these imports, but view object identity was not instrumented. This observation neither proves the progress UI nor establishes a lifecycle defect; no progress/completion-text acceptance is claimed.

All 101 ADB commands, UI Check statuses, complete receipts and component comparison metadata are retained under `artifacts/sh-integration/android-prime-rev2-fc8d6f2fb/`. The isolated app's crash buffer was empty. Original source and APK hashes remained unchanged. The unique emulator source, MediaStore entry, test package, internal/external app data, scratch and derived payloads were deleted and their absence verified. No owned-source screenshots were taken and no source meshes or pixels were rendered. The main app and emulator configuration were untouched. Physical devices, remote providers and runtime rendering remain unqualified.

## Next animation adaptation, still unfinished

A separate metadata-only experiment used the existing full MP1 decoder and bank builder, without production changes. All four appearances admit the same 142 unique compressed ANIM leaf resources, including graph/transition references: 3,556 summed keys, 508,624 scalars, 113 declared strict roots and no foreign source-bone channels. The part-bound banks retain distinct source identity. This is decoder/bank admission, not an independent animation oracle or runtime playback proof.

The existing generated GLB writer correctly refuses these Troopers: each has source root bone 3 with external parent 2, whereas its independently qualified Echoes contract requires external parent 97. The first experiment stopped at that precondition; a second retained the GLB refusal while completing the bank inventory. Both refusal logs remain under `artifacts/sh-integration/prime-trooper-full-source-exploratory/`. Supporting that exact Prime hierarchy requires an explicit source-root contract, independent writer/pose evidence and separate runtime admission. Changing the sentinel or fabricating a bone merely to pass the existing guard would not qualify it.

Full runtime animation, material/skin/gun completeness, Trooper abilities, public selection/network/replay behavior and physical-device acceptance remain unfinished. Rollback removes the new source profile and edition-aware discovery path; original recipes and immutable old generations retain their existing identities.
