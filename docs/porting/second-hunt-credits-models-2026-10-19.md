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

Qualification pending on the frozen source commit. Generated fixtures and
owned read-only probes cover admission, decoder ownership and CPU animation.
No protected assets, pixels, glyph text or model coordinates are committed
or emitted by the probe; retained evidence contains only identities/counts.
Windows SSH authentication remains unavailable; Mac is the authorized
fallback. Physical Android and original GPU presentation remain unqualified.

## Remaining and rollback

Original source glyphs/metrics/background admission, bounded CPU widget/text
output, viewport upload/draw/retirement, original timing/audio/results binding
and story/device acceptance remain implementation work. A future emitter
must apply aggregate per-frame budgets and avoid normalizing degenerate
zero-scale normals. No incomplete original frontend blocks campaign flow.
Rollback removes the unregistered admissions and internal decoder adapter;
existing fallback results and the previously merged timing records remain.
