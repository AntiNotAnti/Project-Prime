# Generated normal Echoes cannon preview

This slice extends the normal imported Dark Samus diagnostic viewer with a fixed cannon cut. It does not make Dark Samus or the other imported characters verified playable characters.

## Source and owners

The cut and frame follow `Campaign/BodyArmCannon.cs` from Second Hunt at `41452f2d053fcfa679e33c00a73d120a53f59e61` (MIT; the repository license notice is retained). A vertex qualifies when any strictly positive CSKR influence descends from source ID24 `R_elbow`; a triangle qualifies only when all three vertices qualify. Zero influences do not contribute. The fixed source primitive is ordinal32, resource `d12fdfa3`, at time zero. The source ID32 `GUN_Particle_LCTR` defines the muzzle origin and barrel frame after evaluation of the complete 39-bone hierarchy.

The existing transactional importer writes a small source recipe beside the unchanged complete generated body GLB, original CINF, ANCS graph and full ANIM bank. The normal bundle advances to version5/body4 with 19 required components and 23 total files. `characters.json` version2 contains distinct Biped and ViewModel entries sharing the same original geometry/provenance inputs. Final stays bundle4/body3, and all four Prime Trooper recipes stay version1. A missing cannon recipe makes the normal body incomplete; an old normal version4 import is outdated and is repaired transactionally.

`CharacterSourceCannonRecipe` retains its independent legacy format1 contract and pins. The new format2 branch binds the generated normal source profile and exact immutable receipt. `CharacterWeightedModelLoader` derives a whole-triangle index subset at load, preserving attributes, seams, weights, original material IDs, global source triangle IDs, and full joints. Physical palette partitions are not treated as original materials or bone identities. Only the existing issued ViewModel lease can load the fixed original primitive; arbitrary clips and nonzero times are refused.

`HunterPreviewEntity`, `CharacterModelRuntime`, the existing launcher side Scene, Weighted4 palettes, material anchors and context caches continue to own rendering. Body/Cannon controls use the existing preview mode intents. The cannon applies its source-to-cannon basis once, then a separate diagnostic fit computed from only referenced cut corners after skinning. Native gun bounds do not distort that fit. The cannon selects a unique existing opaque Modulate material anchor, preferring a lit material; inactive native glow/flash materials cannot hide it with zero alpha. This does not mutate shared native materials. No second renderer, Scene, gameplay weapon registration, actor state, protocol, replay field or global preference is added. Character replacements remain OFF by default; the existing foreground viewer capability permits this read-only presentation only.

## Owned-source identities

Only metadata is retained. The source RVZ is G2ME01 rev0, SHA256 `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`. The complete normal body GLB remains `38b03806faf22851efd3caeb4fcd22b900556b5fc4b21a2051b075b63f6fb8ca`.

An independent original-PAK/CMDL/CSKR oracle reproduced all 392 retained global triangle IDs: material2=66, material5=326, all other material groups empty. An independent raw ANIM/CINF transform chain reproduced the cannon frame within the explicit numerical tolerance; its arithmetic order differs from the runtime basis conversion, so the exact frame hashes are reported separately.

| Identity | SHA256 |
|---|---|
| Generated canonical cannon receipt | `5f1f479438b5efe1d482ff4565f7782e3175a2aed49b4a33a2953b82e4ac7521` |
| Grouped original global triangle IDs | `846ea5492c6aea45baa5bfe9cfc14a9b7cf8eebb887008339c669d1106bdc7d8` |
| Generated source frame float encoding | `b3cf460d253637c4180f3ae0324cf3e35f567d3db162c8b8c1d4964dc5916256` |
| Independent raw transform-chain frame encoding | `b333943ad3e77d6a7c5891fa2b05e10ca7fbeb816fbf69e4075d57dfc7512603` |

The initial measurement deliberately ran before the runtime pin existed: source cut/frame metadata passed, runtime admission refused, and that red receipt is retained. Subsequent qualification must separately pass after the measured pin is installed. The first actual GPU run also caught a real invisible presentation: the native gun's first unique material was `GlowBlue` with zero alpha, despite correct finite transformed cut corners and live GPU resources. That red evidence is retained; the product route now chooses a solid existing anchor instead. All source-derived payloads are confined to temporary local contexts and deleted; logs contain only hashes, IDs, counts and outcomes.

## Implemented, proven, unproven, disabled and rollback

Implemented: deterministic import recipe and distinct fixed ViewModel capability, original whole-triangle cut, complete original zero-phase pose, Body/Cannon viewer controls, and existing render/context ownership.

Proven: the exact owned-source cut and independent source-frame comparison described above. The final frozen build/runtime evidence below qualifies this diagnostic slice.

Unproven: original GX materials, source textures/TEV, first-person source camera/size/light parity, shot/muzzle effects, gameplay firing states, animation transitions and morph, Final cannon rules, Trooper abilities, public selection, multiplayer/replay integration, physical Android/Windows/Linux rendering and device/crash/soak acceptance. Diagnostic source colors are not faithful original materials. This slice has no source first-person gameplay qualification.

Disabled: all extra-character gameplay/public selection/network/replay activation and global replacement defaults remain unchanged. Final and ball source data do not acquire a cannon capability. Existing vanilla seven Hunter identities remain intact.

Rollback: revert this slice to restore the prior normal source-preview recipe. Existing immutable generations remain on disk; reopen/update discovery explicitly. Source imports are never installed as a live default character pack or transferred to peers.


## Final frozen qualification

All **43/43 serial commands passed** at `ac3b79fb1b7382294669b687bd294f71a371ad4a`, incorporating current main through `b9ff89604` (PR858). Protocol72 is inherited from that main change; this feature introduces no protocol or replay fields. Both archived source trees retained all **6,128 tracked files unchanged**. The source archive SHA256 is `31802238565966279754c6bf4f30bc16d6834340f817876c705e6926d5ff33ac`.

| Gate | Frozen result |
|---|---|
| Desktop and dedicated Release | PASS, zero warnings/errors each |
| Android Release/AOT | PASS, 25 existing warnings, zero errors;119 AOT libraries each arm64-v8a/x86_64 |
| Signed APK | PASS, v2/v3 signatures verified |
| Native macOS/Android runtimes | PASS, exact16-file native source closure and pinned dependencies verified; existing native artifacts reused without changing ABI |
| RML assets/native Home/native canvas | PASS |
| Eight existing import/menu/settings/campaign tool suites | PASS |
| Python character authoring / character-model checks | PASS,48 Python tests and910 model checks |
| Generated geometry and cut synthetic checks | PASS,421 writer and50 cannon checks |
| Owned normal/Final body and ball bundles | PASS, original attributes/full rigs and all16 legacy component hashes unchanged; missing-cannon repair and version4→5 migration passed |
| Owned full animation runtime | PASS,281 checks/55 clips/2,979 authored-key samples/165 off-grid samples; no sampling file reads |
| Owned generated cannon | PASS,33 checks and28 refusal cases;392 exact original triangles in2 physical batches; complete rig and unchanged attributes/palettes; fixed phase only |
| Owned generated body profile | PASS,35 checks and32 refusal cases; complete6,340-triangle body and retained generation behavior |
| Shared-stage actual Mac GPU | PASS,428 checks/28 frames/two contexts; body→cannon→body, opaque anchor, visible rotation, stable fixed pose/resources, immediate native clear, owner close and teardown |
| Actual native source page | PASS,144 checks and10 translated clicks each actual960×600 and640×480; mode controls, disabled cannon clip controls, body clock resume and Back |
| Historical replay/map/timing checks | PASS, protocol58/Quake3/pickup/grant replays and campaign map/timing |

Windows SSH reached the host but noninteractive authentication failed (exit255); the explicitly authorized Mac fallback was used. This is not Windows runtime or physical Android qualification. The Android build/signature/AOT checks do not establish on-device source rendering, file-provider lifecycle or crash/soak acceptance.

| Frozen output | SHA256 |
|---|---|
| Desktop DLL | `4cce9737a04387752eada8649de5712f48ccced2bf0b6e72e2e2df3558327007` |
| Import acceptance tool | `61bacd0d45eccd246f2c153640d13ac12cca538c608b5d21aebf6e9ea44c5335` |
| Dedicated DLL | `cc151e36eec3c5ade4cca5f63242a9436469d14e0424d8696a3f63600c049a06` |
| Signed APK | `71b178df092368a4a7df2fc841c36c7829e0f643b3faf13e89aa7238d22a3ef4` |

Metadata receipts, commands, environments, logs, source integrity and APK inventory are retained locally at `artifacts/sh-integration/character-cannon-final-ac3b79fb1/`; exploratory and red receipts remain under `character-generated-cannon-exploratory/` and the prior frozen-attempt directories. Final qualification corrected an added assertion's enum ambiguity, an old Settings fixture's version-based character lookup, and a tamper harness exception filter. Corrupted graph JSON was already refused by the product parser; the harness now counts that refusal correctly. No red run is represented as a passing qualification. All owned-source probes deleted their temporary derivatives and left source archives/native inventory/assemblies unchanged.

This evidence applies to the frozen production/test source above. The subsequent documentation commit adds only this record and does not add feature behavior. A later merge that incorporates unrelated main changes requires its own scoped follow-up evidence before making a claim about the whole merged binary.
