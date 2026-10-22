# SH-09: Beyond Sylux feasibility (research only)

Decision at Project `5bc952147ce45f87d777ff0de4b2927f0c27ef9e` and Second Hunt
`41452f2d053fcfa679e33c00a73d120a53f59e61`: **no production import toggle or
parser integration**. Code inspection identifies a possible future private import
boundary, but does not establish lawful source access, bounded conversion,
material fidelity, playable parts or performance. Complete MP1/MP2/MP3 work first.

## Source inventory

The Beta 3 commit `b6fc4861adba0175ead5ac6a255493896f8e2b70` introduces the
Beyond stack. `aa92ada16cf4103bea65b9f8156be070e84e6ff0` changes trimmed-XCI
handling; `54edafaa1159180172b6eb6575db0a52f87e8035` combines the Sylux import
card. All references below are source metadata at the frozen upstream reference.

| Layer | Upstream owner / evidence | Project feasibility boundary |
| --- | --- | --- |
| Disc/title container | `src/MphRecomp.Core/Import/Switch/{SwitchGame,SwitchKeys,PartitionFs,Nca,Bktr,RomFs}.cs` | NSP/PFS0, XCI/HFS0, NCA and patch/title-key handling form a separate source stack. Project's bounded GameCube/RVZ readers are not compatible. No keys or decryptor added; user-provided extracted files would be the narrowest future research input |
| Archive / compression | `Import/Beyond/{BeyondForm,BeyondArchive,BeyondUnpack}.cs` | RFRM/PACK, TOCC/ADIR/META/STRG GUID inventory and stored/expanded sizes differ from Retro PAK. Upstream reads a seekable table up front and retrieves selected entries. Existing offset checks do not prove acceptable Project budgets or all integer bounds |
| Models / skeleton / motion | `Import/Beyond/{BeyondModelFile,BeyondModelMesh,BeyondSkinTable,ChprCharacter,ChprSkeleton,ChprAnimCodec}.cs`, `Import/Bodies/BeyondBody.cs` | SMDL/CHPR and their skin/animation layouts require new independent validation and source coordinate/bind mapping; no silent truncation to Project's 32-joint draw palette |
| Textures / materials | `Import/Beyond/{BeyondTexture,BeyondMaterialInstance,TegraBlockLinear,AstcBlockDecoder,Bc7BlockDecoder}.cs` | ASTC/BC7, swizzle/layout and material conversion require bounded decoded sizes, semantic comparison and ES/Desktop measurements; code existence is not renderer acceptance |
| Body | `recipes/bodies/MP4Sylux.json`, `Campaign/BeyondSyluxBody.cs` | Body recipe locates source GUIDs and pins an aggregate SHA-1. Upstream combines source idle/flourish and DS movement/shot retargeting; not full native source animation coverage |
| First person | `recipes/guns/MP4Sylux.json`, `Import/Guns/Gun4Builder.cs`, `Gun4Sources.cs` | Source arm subset, clip role remaps and `GunFX` muzzle require Project ViewModel part, camera/muzzle/reticle, charge/missile timing and material acceptance |
| Alternate form | `recipes/bodies/MP4SyluxLockjaw.json`, `Campaign/BeyondLockjaw.cs` | Source Lockjaw clips are separate; Project's existing native Sylux movement and collision must remain authoritative. Neither the body nor grouped card proves alternate-form gameplay/visual alignment |
| Logical bundle | `Frontend/ImportGames.cs` grouped card | Existing SH-03 receipts should later describe body/HD/cannon/alt/rig/material/clip components separately. A grouped display does not license atomic Ready status from only one installed part |

Upstream recipes select from `Preload/DuplicateData.pak` and two Chrono Tower
world archives. Their paths, GUIDs and source checksums are source-identification
metadata, not redistributable media. Recipe SHA-1 consistency is not licensing
or source ownership proof. No game source, keys, texture/model payload or runtime
cache was opened, copied or generated for this memo.

## Reuse and unresolved costs

Project already owns canonical character/skin IDs, private provenance witnesses,
source leases, staged exports, immutable appearance cache keys, full source
hierarchy evaluation and per-draw palettes with at most 32 matrices. Those owners
are the only intended consumer. The synthetic 39-bone palette/GPU result documents
that mechanism, not support for Beyond rigs or their clip codec. No eighth Hunter,
new renderer, duplicate importer service, Community upload or network asset transfer
is justified by this research. Sylux's cosmetic identity should remain distinct
from Dark Samus/Trooper gameplay identities.

There is no measured cost here for complete source skeletons, clip conversion,
texture decoding, upload, per-seat memory or 8-player drawing on desktop/Android.
Large archive comments in upstream are not trusted allocation limits. Before any
parser port, define explicit caps for table bytes and entries, names, offsets and
checked arithmetic, stored/expanded byte ratios and totals, nesting/recursion,
vertices/triangles/bones/clips/keyframes, materials/images, CPU time and cancellation.
Reject malformed/unsupported source before publication and preserve the last good
bundle. Workers own detached bytes; scene owners publish and own GL resources.

## Research gates and test plan

A separately scoped proposal must first define an authorized local source boundary
and verify the legal/source-access constraints relevant to that boundary. This memo
makes no legal determination and provides no keys, acquisition or circumvention
procedure. Extracted tree support, if considered, must avoid arbitrary remote paths,
symlink escapes and title-container key handling.

No synthetic parser tests are added now: Project has no approved Beyond parser or
source input contract to test, and a new parser solely to satisfy a research test
would expand this audit's scope. The useful future asset-free suite would construct
minimal source forms and check truncation, overlapping/out-of-range sections,
negative/overflow lengths, duplicate GUID policy, expansion bombs, bad indices,
hierarchy cycles, excessive influences, invalid float values, unsupported material
and clip versions, cancellation, path containment and publication rollback. An
independent CPU skin oracle should validate full-hierarchy/per-batch equivalence.

Real-source gates then require known source/edition receipts, complete required
parts, correct bind/axes/materials and clips, first-person muzzle alignment,
Lockjaw morph/unmorph and collision parity, source mutation/tamper rejection,
missing-local-asset native visual fallback, scene teardown and Mac/Windows/Linux /
Android ES performance/captures. Network/replay identity requires a separate
approved capability/version contract; this cosmetic research cannot activate it.

Outcome: source architecture inventoried; implementation, real media, hardware,
performance, lawful provisioning and product qualification **not verified**. This
work has no production toggle and is not a dependency of SH-01 through SH-08.
