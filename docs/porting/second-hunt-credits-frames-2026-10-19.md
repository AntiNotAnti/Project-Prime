# Original credits catalog and CPU frames

This SH-08B slice joins the existing admitted source, animation, widget and
text components into one bounded CPU frame owner. It remains internal and
unregistered. It owns no UI route, clock, callbacks, Scene, GPU resource,
audio, save or return action. Base main is PR835 at
`32977c43e3de5cffa28532beb78fd029ac32259d`; reference behavior remains Second
Hunt41452f2 GameEnding/MenuEngine/MenuText. Protocol69 is unchanged.

## Inventory and ownership

`CampaignCreditsCatalog` requires the exact referenced canonical model and
animation paths, all selected pages and four explicit good/bad background
roles. Missing, extra, differently cased or model-mismatched entries refuse
the catalog. Duration metadata is derived from each binding's admitted bytes.
The font model participates in the same immutable model inventory.

The catalog retains one model instance per source hash, with the font's
retained instance chosen first. Animation bindings canonicalize by both model
and animation hash: identical animation bytes can legitimately bind to two
different model identities. These distinct retained bindings are charged
separately. Caller dictionaries can be discarded after construction.

Before any Model, sampler or pixel decoding, admission caps unique models8,
widget IDs64, private sampler pairs32, source vertices/triangles262144 each,
and source pixels2097152 across planned private sampler instances. It also
caps encoded model/binding/metric/background extents plus per-style text at
16MiB. Original MARM/localization file lengths are not available or retained
by this catalog; the separate source admission bounds their records/text.

Reachable material texture/palette pairs are enumerated directly from the
admitted model bytes, deduplicated by model hash plus both IDs, and their
actual RGBA extents capped at8MiB before GetPixels. Declared texture pixel
count alone cannot bound multiple palette variants. Four decoded backgrounds
add786432bytes. There are at most256 text layouts and65536 aggregate source
characters. Frame construction separately admits actual generated result
strings, conservative measurement work64MiB, cached glyphs65536, per-frame
glyphs16384 and triangles65536 before constructing private models/pixels.
These are explicit component budgets, not a total process-memory claim.

## Captured results and frame composition

`CampaignCreditsResultsBinding` finds five unique positional roles on both
results pages using the pinned reference's strict1.5-pixel anchor tolerance.
Every role has three consistent text states. Four roles share source IDs
between pages; completed/incomplete Gorea IDs must remain independent. IDs,
item ordinals and game text are not hardcoded. Replacements bind only the
exact document and result item, preserving all other source strings.

Total/Gorea time use the existing captured nullable60Hz records and invariant
formatters. Missing/invalid history or completion stays NOT AVAILABLE; known
zero remains valid. Deaths use captured Deaths; defeated hunters use
HunterKills. EnemyKills and Octoliths cannot substitute for either field.
The bad ending preserves its authored incomplete battle status even when the
receipt contains recorded Gorea time. No live save or world is reread.

`CampaignCreditsFrameOwner` requires the exact catalog document, frame table,
existing timeline instance and captured ending outcome. All private mutable
samplers, pixel decoding and capture belong to its creating thread. Equal
model/animation pairs reuse serially evaluated samplers; output is detached.
Layouts are cached by page/item/state, independent of frame age. Each model's
reachable pixel pairs decode once through one private still Model; no global
model/texture cache or source-file reopening is used.

Capture reads the timeline without advancing it, takes only started items
with a current visual, loops steady widgets and holds transition endpoints,
and omits only currently zero-alpha samples. Widget placement translates XY
once; item depth adjusts painter order without changing source vertexZ.
Text already carries the authored anchorY-192 and depth mapping. Glyph quads
preserve8x8 geometry and the source UV inset. Ascending depth with an explicit
source-order tie-break reproduces the flat collection ordering.

Credits6..12 have black background. Results return two explicit256x192 image
regions in Y-up source space: top edge192 for the top screen and0 for the
bottom screen. This is a named stacked DS policy, distinct from the
reference's widescreen recomposition and13-second image crossfade. It makes
no widescreen parity claim. Closed timelines return no stale geometry or
background. Dispose releases only this owner's managed references; the
external timeline retains its state and exactly-once return intent.

## Qualification

Frozen source `48ec03524158af1e2cca72545fa7cf2d9609b567` passed all22
serial commands. Both archives retain all6,008 tracked files unchanged.
Desktop/dedicated/credits-tool Release builds have zero warnings/errors.
Android Release/AOT has25 existing warnings, zero errors,119 AOT libraries
on each arm64-v8a/x86_64 ABI and valid v2/v3 APK signatures. All3,853
source-free checks pass, including101new catalog/frame cases and the prior
credits fixtures/corruptions. Historical replay, movie lifetime, ending,
presentation, post-match save and campaign timing regressions pass.

Source-free checks cover canonical object ownership, model-bound animation
identity, aggregate refusals before pixel allocation, both result roles,
known/zero/unknown/longMax timing, strict anchor boundaries, source-space
numeric placement/UV/depth/order, loop/hold/alpha, snapshot immutability and
thread/timeline/disposal ownership. The preliminary fixture run initially
expected font cell outer edges, omitting the authored.02pixel inset. The
fixture oracle was corrected; production layout did not change for that
failure. Source review also corrected pre-freeze snapshot canonicalization,
model-specific animation identity and widget depth placement.

The owned probe uses33 pinned files and invented captured statistics. It
exports only counts/IDs/hashes, with no protected text, vertices, glyphs,
pixels, screenshots or saves. All source files, core/tool binaries and five
existing caches remain unchanged. Both branches traverse3000timeline ticks
and3001sampled frames each, including all seven credits pages and390result
page ticks. The resulting6,002frames contain3,471,898sampled triangles,
maximum1,100per frame;166 repeated-capture and166 earlier-snapshot checks
pass. Independent timeline acknowledgment still consumes return once after
frame-owner disposal.

The catalog contains8models,39widget IDs,17canonical sampler pairs,
456source vertices/282triangles and388864source pixels across those sampler
instances. It admits28unique texture/palette images totaling904192RGBA
bytes,174text styles/10704source characters, and406552charged source bytes.
There is no GPU, physical device or original pixel-parity claim.

Desktop DLL SHA256:
`a60b0cfb6045d7461052924a05ec5e31750a7b4e76f437c9a669aab2a31ceabf`.
Dedicated DLL:
`bf6e191798e8182e642741364a39dcd5df2219f5fac026aecc44d1e8eb8c28ba`.
Credits tool:
`fe4bd802b04b42422749f45c25ea2aacf837c174887560fb3d7915d02c5ef950`.
Signed APK:
`e6c170dd0b122ad3fec565944cf9890b6a28d54ee50e4659c6a329433e1aca7b`.

Receipts: `artifacts/sh-integration/credits-frames-48ec03524`.
Windows SSH authentication remains unavailable; testing uses the authorized
Mac fallback. Physical Android and Windows/Linux runtime remain unqualified.

Concurrent CW-05f PR836 merged at `adbaedd2e04852a811d1fb7b5e6c79c0aa0ca81d`
during qualification. Its10custom-weapon/match-policy files touch none of
this slice's code/tests or shared model/geometry dependencies. That change
is outside this frozen whole-binary proof; source-file identity is checked
again after merging this slice.

## Remaining and rollback

Native viewport geometry/texture submission, explicit sampler and
premultiplied-color policy, GPU retirement, scoped async asset admission,
receipt/root/document epochs, page activation, input/return integration,
audio transitions and Android ending ownership remain implementation work.
The existing ending receipt stays the presentation fallback. Rollback removes
the unused catalog/frame components and probe flag; current presentation,
simulation, saves and wire behavior remain unchanged.
