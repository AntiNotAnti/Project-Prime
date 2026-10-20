# CW-06a evidence — original bounded rifle model

Base: `b1cc9a26cdd79481b89b1650ba72fd45beb04ef1` (merged CW-05t).
Head: completed-slice commit containing this file.
[Implementation](../06a-original-rifle-model.md).
Game SHA-256: `b4b54c0927d61187d793cfe33452cc1db5c29b2f8a8da2c4f059e413e521be8e`.
Original resource:2,150 bytes, SHA-256
`3dffd6317ba8fc2d861e045f808aaa4cfdb80b251ff354f7b7fb379bf199e752`.

Commands use .NET10.0.401/macOS arm64, DOTNET_PROCESSOR_COUNT=2,
UseSharedCompilation=false, Release for desktop/server/tools and Android Debug.
Tool builds also use BuildProjectReferences=false. Asset processes have separate
PROJECT_PRIME_USER_DATA/paths.txt directories. Production sources/assets were
frozen during all target builds; Android/server/tools retain the same desktop DLL.

| Check | Result |
| --- | --- |
| Desktop Release |PASS73.372s,1 warning,0 errors |
| Isolated server Release |PASS69.775s,1 warning,0 errors |
| Android default Debug |PASS95.641s,26 warnings,0 errors |
| Custom CPU suite |PASS22,717 assertions including4,252 model;22.205s |
| Actual original model/pickup GL |PASS133 checks;6.433s |
| Existing actual pickup GL |PASS79 checks |
| Acquired wheel / acquisition during native hold GL |PASS143/152 checks;9.408/9.641s |
| Original packet registry / native MP+1P tables |PASS; all78 original IDs/names/tuning exact |
| Native fidelity |Known46/47,raw exit1; unchanged missile-splash baseline |

The4,252 model assertions include independently calculated source length/hash,
16/6 LOD box counts, material palette, rigid groups, muzzle and AABBs; compiled
resource/source parity; digest tamper refusal; every incomplete truncation;
duplicate/unknown/missing fields, external references, depth/size/array limits,
nonfinite/out-of-bounds numeric fields, material/group/socket/LOD refusal;2,000
bounded malformed/mutated inputs; detached decoded values;100,000 warmed queries
allocating0B. Plain decoder whitespace allowance is distinct from exact catalogue
byte identity. This does not qualify arbitrary creator packages or GLB decoding.

The actual GL fixture starts native-only and proves no model attempt. Direct
fallback drawing submits3 boxes without creating a cache. Enabling public M7
availability also does not load until the real fixed-step presentation update;
that update validates/prewarms before the first model draw. Twelve repeated
pictures use the same instance while frame/item/resources/game state/position/
native ammo/gameplay RNG/muzzle/aim stay exact. The declared direct LOD1 seam
submits6 boxes through actual GL. Actual synthetic W movement and native item
contact consumes the pictured16-box station and funds4/16 without firing.
Before/after composite and scene-target captures were inspected.

Actual UnloadGl clears CPU ownership; a subsequent CPU fixed step creates a new
verified instance, and scene cleanup releases it. This is not an Android surface
loss/recreation render test. After every GL owner is disposed, a fresh headless
public scene proves no attempt/cache and the direct builtin loader returns null.
Rendering uses the existing scene Box shader/pool with no new GL handles or
texture decoders. Source inspection confirms online pickups call the same cached
renderer, with no draw-driven authority, loading or simulation advancement.

The acquired-wheel regression first failed because the synthetic controller's
settings were written before selecting its device-owned runtime. The fixture now
activates the device before assigning/checking its exact binding/permutation.
Existing held-order, neutral-cancel, acquisition, resources and fire-suppression
assertions remain intact; failed logs are retained. No production input code
changed. Final test logs/results are separately named final-*.

Source review found no further actionable production defect; no GitHub approval
is implied. Desktop/server's warning is the existing reflection-only ACK discard
fixture field; Android adds existing platform/XML warnings. CI/review NOT_WAITED
per user instruction. No CI pass is claimed. Main's independently merged PR867
changes only other acceptance fixtures; this slice's measured base remains the
SHA above. No native gameplay/network/archive format changes are introduced.

Fidelity remains weapon.missile.splash tick177 victim.position4096.z expected52814
actual52815. Imperialist F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Local logs/JSON/captures: `artifacts/custom-weapons/CW-06a/`; these and private game
assets/toolchains are uncommitted. Only original numeric asset source/provenance/
license is added to Git. No throughput or achieved refresh-rate claim is made.

CW-06 remains incomplete: seven held-rifle mounts, animation/audio/VFX and quality
captures follow. Physical Android/controller/touch, rendered online/WAN and
60/120/240Hz qualification remain release gates. Custom replay remains CW-12.
Rollback restores the retained three-box fallback; no wire/save migration.
