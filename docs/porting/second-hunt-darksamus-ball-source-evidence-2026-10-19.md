# Dark Samus body and ball authoring source evidence

Research date: 2026-10-19 local / 2026-10-20 UTC. This memo records the
initial metadata-only witness against the earlier body-only exporter.
Implementation and qualification of recipe v2 are tracked separately in
[the body/ball integration evidence](second-hunt-darksamus-parts-evidence-2026-10-19.md).
This research establishes a feasible source
handoff; it does not establish a playable ball, an HD appearance, or runtime
animation/graph acceptance. No commercial model, texture, animation, source
snapshot or derived media is included here.

## Source and parser identities

- Frozen Second Hunt reference: `41452f2d053fcfa679e33c00a73d120a53f59e61`.
  Historical provenance `c16a907b3658c2fd4079e394a73711596a85caee` remains
  distinct and unchanged.
- Project parser witness: isolated immutable Release assembly from
  `3e181df59aacb136e49e7d22d33108cacaef362c`, SHA-256
  `c98919449c1a3dac61aad432da88f6bbd26cf81910e9112f7a66b67bdff8da28`.
  The final metadata-only probe built with zero warnings/errors, referenced
  this assembly, and did not build into shared application outputs.
- Owned media: Echoes USA GameCube `G2ME01`, revision 0; RVZ length
  `1287435724`, SHA-256
  `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
  Full-file SHA-256 before and after the probe matched exactly.
- Verified archive SHA-1 identities: `Metroid1.pak`
  `6b9a47578f41b2c64a12f1a0bf4e5cd783692124`; `Metroid5.pak`
  `b3375e83c5290d35d6c83f35b04b7f60f6fb8ea7`; `Metroid6.pak`
  `b8b92af37dc772ecd8234fb1ce664be0493188f6`; `LogBook.pak`
  `590207df97d73575c7c3c59a821d908438214616`.

All model, rig and sampled-animation reads for the three parts below resolved
from the already-required `Metroid1.pak`. The ball needed six resources:
ANCS, CMDL, CSKR, CINF and two ANIMs. Although the probe also verified
LogBook and the upstream HD ball recipes name it, this geometry/rig authoring
slice does not require adding LogBook to the current source fingerprint.

## Exact part inventory

Both the bounded `RetroCharacterSource.FindCharacter` locator and structured
`Ancs.Parse` agreed on the following character references.

| Part | ANCS / native character | Character index | CMDL | CSKR | CINF | Bones | Positions / skin entries | Triangles | Materials |
| --- | --- | ---: | --- | --- | --- | ---: | ---: | ---: | ---: |
| Normal body | `39900a11` / `Dark_Samus` | 0 | `d2c3805b` | `b9ec2f8d` | `4abc8a50` | 39 | 4245 / 4245 | 6340 | 7 |
| Overcharged body | `39900a11` / `Battle_3` | 2 | `faa12c05` | `918e83d3` | `4abc8a50` | 39 | 4849 / 4849 | 12734 | 9 |
| Shared ball source | `654a3f97` / `Dark_Samus_MorphBall` | 0 | `2f2c1e6f` | `4403b1b9` | `c7dbc44a` | 2 | 1553 / 1553 | 2220 | 1 |

All three ANCS character records are version 10. The body ANCS is 70744
bytes, SHA-256
`7933e22a780b7f2cbee01be3ee5a4d48e6f08eb39df81608d638bee1729a5f7b`.
The ball ANCS is 624 bytes, SHA-256
`46ddcf531037bd4ae3f1949f0ecc7febe6deede70e5d19ddd720e5a7cfa21ad8`.

The ball has exactly one influence per source position, using bone 1; all
skin bone IDs and sampled animation bone IDs belong to its own two-bone
skeleton. Bodies retain their separate 39-bone rig and one-to-two influences.
No body/ball rig substitution or retargeting was performed.

The existing `Cmdl.Load` padding proof handles the ball without another parser
special case: its position section is 18656 bytes, exactly Align32 of
`1553 * 12 = 18636`, with 20 zero trailing bytes and every triangle corner
within the CSKR extent. Overcharged has the same 20-byte alignment case;
the normal body has four padding bytes. Existing OBJ and CSKR sidecar writers
accepted all three in memory. Ball output sizes were 202700 UTF-8 OBJ bytes
and 3227 weights bytes; no asset files were written.

## Motion and raw graph availability

The ball is not an animation-set-absent source. Existing
`RetroMotionPreview.FromLocalSource` sampled both native clips successfully,
using five sparse samples per clip on bones 0 and 1:

| Native clip | ANIM | Encoding / bytes | Keys | Duration seconds |
| --- | --- | --- | ---: | ---: |
| `B_RollForward_Dark_Samus_MorphBall` | `fae15ae4` | type 2 / 128 | 25 | approximately 0.800000012 |
| `B_ready_Dark_Samus_MorphBall` | `3bf596d0` | type 2 / 112 | 2 | approximately 0.0333329998 |

Each animation has two rotation and two translation channels, with no scale
channels. Thus this particular ball source does not encounter the sparse
preview's omission of scale samples. These remain sparse source poses, not
accepted gameplay clips, a full-rate animation bake, morph transitions,
movement behavior, or an evaluated state graph.

`RetroAnimationGraphProof.CaptureOptional` returned `Present`; `Verify`
reproduced the descriptor exactly from the retained in-memory source
snapshot. Its graph has two roots, two nodes and one default transition,
with zero full and half transitions. Raw proof retention does not authorize
graph execution or named gameplay semantics.

Both bodies expose the same ANCS-wide inventory: 76 clips, 12 sparse sampled,
62 indexed but unsampled, and two composites requiring a blend graph. Their
raw graph has 76 roots, 87 nodes and 11 transition nodes. The character-local
used-animation lists contain 50 entries for normal and 58 for `Battle_3`.
The exporter does not filter the 76-entry inventory by character membership;
do not label it 76 accepted motions for either body or copy it to the ball.

## Smallest complete recipe v2

Reuse `RetroCharacterPreviewExporter` and its existing resource resolver,
model/rig/motion readers, source proof, staging, `LocalImportBundle` lease and
sequential publication. Add a small internal part description/writer within
that owner. Do not introduce another importer, appearance row, parent Hunter,
or ball card. Keep the six canonical source appearances: two Echoes cards
and four Prime Troopers.

For each existing Echoes appearance, publish body and ball in one immutable
appearance generation:

- Preserve existing body filenames `<appearance>.obj`, `.skeleton.json`,
  `.weights.json` and `.motions.json` for legacy authoring consumers.
- Add `<appearance>.ball.obj`, `.ball.skeleton.json`, `.ball.weights.json`
  and `.ball.motions.json`. The ball uses its own ANCS/native name/refs and
  skeleton. Both appearances may share the verified ball source without
  aliasing their distinct body sources or claiming different qualified ball
  visuals.
- Require all eight core files in the v2 recipe. Retain each part's optional
  graph/raw pair together when present: this source has twelve files total
  before any explicit part manifest. A failed required ball conversion must
  not publish a body-only generation labeled v2 Ready.
- Hash explicit body and ball recipe identities into v2; preserve receipt
  schema 1 and the current exact source-edition fingerprint unless its actual
  required archives change. Prime Troopers can retain their existing recipe.
  Old v1 generations remain available on failed/canceled updates; they are
  outdated for v2, not silently upgraded by the presence of loose siblings.
- Keep Update selected/all and Remove under the existing appearance owner.
  The current component-prefix grouping already includes dotted `.ball.*`
  siblings. Parse and stage complete requested appearances before publication,
  preserve partial-batch reporting, and never mix body/ball generations.

## Required naming and consumer fixes

Graph proof currently derives `SourceSnapshotFile` from `SourceCharacterId`,
and verification reconstructs that filename. Capturing body and ball with the
same canonical appearance ID would therefore collide. Give capture and
verification a validated part/artifact namespace while retaining canonical
parent identity and compatibility with existing body proofs. The proof's
character-ID validator currently accepts hyphens but not dots; simply passing
`<appearance>.ball` as a character ID is not a valid fix. Any descriptor
format change must have explicit compatibility/negative fixtures. Do not
invent another catalog appearance merely to obtain a unique filename.

Python `build_glb.py` and `source_palette.py` currently allow five canonical
identities, omit Overcharged, and have no part selector. Ball filename stems
would be rejected. Extend consumers using canonical parent ID plus
`--part body|ball`, defaulting to the legacy body behavior. Resolve an explicit
validated file stem for the selected part; preserve the canonical appearance
identity in sidecar checks and bind its source part separately. Carry the
same selection through rig, weights, sparse motion, source palette and graph
proof inputs. Never fall back from a missing ball file to body files.

## Bounds, acceptance and remaining gates

No ball-specific parser repair is needed for the witnessed source. Existing
limits include bounded PAK resources, 1024 CINF bones, two million CSKR
vertices, 65536 weight groups, 64 influences/group, 16 MiB ANIM resources,
12 sampled clips/five samples each, and 4 MiB graph proofs with bounded nodes
and transitions. Some generic CMDL/ANIM counts allocate before later exporter
or motion limits; this successful pinned-source witness does not establish
arbitrary malformed-source parser safety. Do not broaden accepted media
editions as part of this slice.

Before calling v2 usable, add source-free body+ball fixtures for required-part
completeness, distinct refs/rigs, per-part graph names, graph identity tampering,
old-body compatibility, canonical parent/part selection, missing-part refusal,
cancellation before receipt and preserving the old generation. Retain runtime
negative fixtures, including rejection of `MP2DarkSamusFinal` by
`PrivateCharacterAppearance.TryCreate`. Existing Python forty-test and native
character/import/settings suites need new part fixtures; their prior results
do not qualify the unimplemented v2 contract. Then run the owned-source
bundle probe for both Echoes appearances, verify all parts and cleanup every
derived payload, retaining metadata only.

Upstream `recipes/hd/MP2DarkSamusBall.json` marks a static HD recipe with scale
9.72 and core coloring; `MP2DarkSamusFinalBall.json` additionally derives veins
from `Battle_3` at a named body pose and specific material roles. Exporting
the shared native ball mesh/rig does not implement those recipes. Materials,
textures, HD/core/vein appearance, physical-device visual acceptance, cannon
integration, runtime source-rig acceptance, playable morph behavior and
network/replay use remain unqualified. Existing runtime appearance rejection
must remain in force. This memo records research, not completion of SH-03,
SH-05 or ball gameplay.
