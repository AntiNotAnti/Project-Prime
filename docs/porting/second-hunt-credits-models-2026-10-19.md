# Original credits model and animation admission

This SH-08B slice extends the accepted detached source document and pure
ending timeline. It admits a deliberately narrow native credits asset profile
and decodes exact admitted bytes through the existing `Read`/`Model` owners.
It does not register a frontend route, activate original credits, build a new
renderer, change save/audio ownership or qualify GPU fidelity.

Target main is `08afb86f0ebebcc014fcc9b7cc3234575037c504` (including
merged PR830, concurrent Traversal PR829 and custom-input PR831). Reference Second Hunt remains
`41452f2d053fcfa679e33c00a73d120a53f59e61`. Protocol69 and historical replay
formats remain unchanged.

## Implemented boundary

`CampaignCreditsModelSource` copies before validating and retains private
bytes. Public snapshots expose hashes and counts, with no mutable model,
name collections or payload buffers. Inputs are capped at4MiB; nodes/materials/
textures/palettes at64, meshes/display lists at128, matrix slots at31,
aggregate pixels262144, command vertices65536, triangles32768, commands32768
and graph depth16. Matrix slots use the existing native model limit.

Every count/range precedes generic decoder allocation. Names are bounded,
terminated, unique printable ASCII. The single rooted graph must be acyclic,
fully reachable and parent/child/sibling consistent. Every mesh has one node
owner and each display list has one mesh owner. Static scale is nonzero with
absolute value at most16; header scale base is1 and exponent at most12.
Runtime/skin pointer graphs, billboard/disabled nodes and unsupported
material modes are refused. One unused declared texture matrix is allowed,
with absent runtime pointer and TexgenNone/MatrixId0. Material TranslateT
remains data; it is not mistaken for a runtime animation ID.

Only observed texture formats0/1/2/6, power-of-two dimensions8..512, exact
encoded size and complete bounded palettes are admitted. Actual encoded
indices are checked against every referenced texture/palette pair. Short
valid palettes remain valid. The exact untextured(-1,-1) material pair is
admitted; mixed/other negative texture/palette IDs are refused. Alpha0 materials retain geometry for subsequent
animation. Shared display-list argument admission is reused; only the ten
observed commands, balanced quads and quad strips are accepted. Declared
source primitive and vertex counts must match decoded commands. Expanded
triangle corners require their own `TriangleCount*3` consumer allocation,
not the command vertex count.

`CampaignCreditsAnimationBinding` independently re-admits detached bytes and
matches the exact duration hash. It binds one nonempty equal-duration node/
material slot to the model identity, requiring exact node ordinals and
unique existing material names. Texture/UV groups are refused. Blend1 only,
node flags0, bounded scale/color LUTs, nonempty declared channel spans and
all runtime sample indices are proved. A length2 channel cannot borrow a
third frame from adjacent data merely because the larger pool contains it.
Length1 remains a constant channel. Unanimated static materials need no
invented animation record.

`Read.ReadCreditsModel` is internal and accepts only these admissions. It
uses a generated reserved identity, one equal-path recolor, no shared
animation, no metadata lookup and no cache entry. Supplied model/animation
bytes are selected before global roots or file IO. Generic legacy callers
retain their defaults. Credits decoding explicitly materializes instructions
and texels even in headless mode, without changing legacy headless decoding.
Each call returns independent mutable model/node/material/matrix state.

## Evidence and limits

Frozen source `0951d04d649da36cfcf7e68f69736c25d719dc44` passed all32
serial commands. Two independent git archives each retained all5,982 tracked
files unchanged. Desktop, dedicated and credits-tool Release builds have zero
warnings/errors. Android Release has25 pre-existing warnings, zero errors,
119 AOT libraries on each arm64-v8a/x86_64 ABI and valid v2/v3 APK signatures.

| Check | Frozen result |
| --- | --- |
| Source, duration and timeline fixtures | 239 +87 +89 passed |
| New model, binding and exact decoder fixtures | 158 +139 +48 passed |
| Seeded malformed-input trials | 2,000 passed:1,398 bounded refusals,411 admitted models and191 admitted bindings decoded successfully; cache unchanged |
| Owned source and both ending timelines | Passed, exact pinned hashes unchanged |
| Owned models/animations | 2,826 checks;8 models/18 animations;26 separate mutable model owners;1,156 animation frames +8 still frames |
| Owned CPU sample bounds | 14,072 node matrices,6,126 material samples,96,256 stack floats;86 texture/palette decodes totaling615,936 pixel samples |
| Existing legacy room GL clock | 2,465 checks on normal shipping DLL, entire owned root hash unchanged, no media/save written |
| Import, character model, map editor, custom weapons, historical replay and campaign suites | Passed |

Unique model texture inventory is226,048pixels/904,192RGBA bytes; models emit
226 command vertices and140 expanded triangles (138 widget +2 font).
Five existing Read caches remain unchanged. Admission/evaluation evidence
contains no GPU/original-frontend fidelity claim.

Exploratory owned testing caught a valid untextured logo material that the
initial predicate refused; exact(-1,-1) admission and generated regressions
fixed it before freeze. A probe initially expected absent UV/texture group
lists; generic Read correctly supplies one Empty group per advertised slot.
The probe now checks these placeholders, preserving existing SetAnimation
indexing. Initial partial failures preserve all actually read source hashes;
complete inventory remains a separate pass condition.

Desktop DLL SHA256:
`dcb9ee51ba44178ab16822aa09a5db3f03cc878ff9fffacd51254ad84b204890`.
Dedicated DLL:
`c63b882afc0cacf237fc5730f7b1fae8fbaafb6b8756aae8b16e43e34f3e3dde`.
Credits tool:
`3594e1875e6be48a7cd557e59b333fb65ea960ce4eb33a4416cb21c1954b92e0`.
Signed APK:
`0a00dc0d967cf68b9f8914f086c34bef45b10cf61fc7e15684a001ce65cbdee1`.

No protected assets, pixels, glyph text or model coordinates are committed
or emitted by the probe; retained receipts contain identities/counts/logs in
`artifacts/sh-integration/credits-model-0951d04d6`. Tests use isolated user-data
and temporary directories, scrub inherited Project Prime overrides and remove
their contexts. The committed tool runs generated checks without arguments;
`--probe-render OWNED_MPH_ROOT` performs opt-in CPU source acceptance.
Windows SSH authentication remains unavailable; Mac is the authorized
fallback. Physical Android, Windows/Linux runtime and original GPU
presentation remain unqualified.

## Remaining and rollback

Original source glyphs/metrics/background admission, bounded CPU widget/text
output, viewport upload/draw/retirement, original timing/audio/results binding
and story/device acceptance remain implementation work. A future emitter
must apply aggregate per-frame budgets and avoid normalizing degenerate
zero-scale normals. No incomplete original frontend blocks campaign flow.
Rollback removes the unregistered admissions and internal decoder adapter;
existing fallback results and the previously merged timing records remain.
