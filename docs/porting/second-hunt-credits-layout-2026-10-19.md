# Original credits backgrounds, font metrics and CPU text layout

This SH-08B slice adds detached native background/font admission and bounded
text geometry/color evaluation. It reuses the accepted model admission for
the font atlas. It does not register original credits, allocate GL resources,
change the timeline or touch global HUD fonts, audio, saves or a game Scene.

Target main: `e24be8ae318627fa1c86f8fc2a96704ddd563759` (PR832).
Behavior reference: Second Hunt `41452f2d053fcfa679e33c00a73d120a53f59e61`,
MenuText, MenuEngine and FrontendSession. The existing repository MIT notice
remains intact. Protocol69 and historical replay formats are unchanged.

## Implemented

`CampaignCreditsBackgroundSource` requires exactly49,664 bytes:256 little-
endian RGB555 colors followed by a256x192 byte index plane. It detaches input
before decoding, preserves row order, ignores palette bit15 and produces
196,608 private RGBA bytes with opaque alpha. Background RGB expansion uses
the pinned frontend's integer floor(c5*255/31). Existing model/font textures
retain their separate rounded native RGB5 conversion. These contracts differ
for some values; neither is silently replaced by GX bit expansion.

`CampaignCreditsFontSource` binds480 advance widths and480 signed vertical
offsets to one admitted128x256 A3I5 font atlas (one node/material/mesh/texture/
matrix, material texture0/palette0). All widths must be1..9 and offsets-2..1.
Width9 remains valid with8x8 glyph cells. The snapshot privately copies metrics,
exposes bounded lookups and fingerprints width-then-offset bytes plus the
model identity. This pure slice API cannot authenticate a whole ARM9 edition;
the caller must supply edition-bound slices. The owned probe verifies AMHE1
ARM9 offsets0xC020C/0xC03EC and the matching embedded font at0xC7F5C without
loading unrelated runtime data or modifying the global Font owner.

`CampaignCreditsTextLayout` consumes immutable font facts, NUL-free Latin1
content, source text style and20.12 item coordinates. It returns immutable
glyph records in native Y-up menu space. Input is capped at4096 bytes,512 lines
and8MiB of measurement visits. Word wrapping retains native newline/space
splitting, empty-builder behavior and overlong words. Source slices replace
repeated candidate-string allocation, while preserving pair decoding across
candidate word boundaries. Invalid glyphs advance8 without geometry; a dangling
high-bit prefix retains its single-byte interpretation. Literal spaces skip
geometry, while an encoded space remains a glyph.

Alignment preserves odd-width center floor. Size controls baseline/line
spacing, with fixed8x8 quads and independent advances. Signed vertical offsets,
0.02-pixel atlas inset and native depth ordering are preserved. Formatting
fields remain source facts; no new bold/italic/Unicode typography is invented.
Color sampling is separate from geometry: RGBA low five bits per byte,
fractional fixed-point transition duration and triangular steady pulse.
Modulo reduction precedes multiplication so long ages cannot overflow.
Sampling colors never advances the timeline or changes captured geometry.
Zero-alpha geometry remains available for later source-style visibility.

## Qualification

Frozen source `e2e67b98c6771d89ce195125e039638dc1157649` passed all20
serial commands. Both git archives retain all5,988 tracked files unchanged.
Desktop/dedicated/credits-tool Release builds have zero warnings/errors.
Android Release/AOT has25 existing warnings and zero errors,119 AOT libraries
on each arm64-v8a/x86_64 ABI, and valid v2/v3 APK signatures.

Source-free tests pass798 bitmap/font and104 numeric text cases, plus the
previous239 source/87 duration/89 timeline/158 model/139 binding/48 decoder
checks and2,000 corrupted-input trials. Owned source/timeline/model probes
pass again, including all1,156 animation frames. Historical replay, ending,
movie lifetime, presentation, post-match save and timing regressions pass.

Read-only owned bitmap/layout acceptance passes1,921 checks:four independent
full RGBA hashes,196,608 public pixel lookups,480 glyph metric lookups and174
source text-state layouts (1,308 lines/8,865 glyph quads/17,730 triangles,
maximum214 quads per state),885 representative color samples. All eight
source hashes remain unchanged; no original image, metric array, glyph
coordinates or string content is exported. The combined metric SHA256 is
`1619a5a096d200058a3781424e7d68a287821ad4fe435a6b45e00cffab3b5a95`.
The independent oracle uses Python struct/LE RGB555 integer-floor conversion;
the committed C# probe pins its four full decoded RGBA hashes.

Desktop DLL SHA256:
`784c18066a44ebfcb3620994c2cdce29e46cc66c7919e9e636630b5d628ae3f5`.
Dedicated DLL:
`33dbee3c33fc90505f8038d2f721af3a9efd49e6e88815729f57515a8b135525`.
Credits tool:
`415605807843ec80a4dcb2f8764be099d1634a9c44662a83077053ad0259008b`.
Signed APK:
`0783c7366dee7bde478b57742105135d3a7785a75a0c031027c01d91f0c0cd0f`.

Receipts: `artifacts/sh-integration/credits-layout-e2e67b98c`. Builds/tests
scrub inherited Project Prime overrides and use removed temporary contexts.
Run committed generated tests without arguments; `--probe-bitmaps OWNED_MPH_ROOT`
performs opt-in CPU source verification. Windows SSH authentication remains
unavailable; testing uses the authorized Mac fallback. This qualifies CPU
admission/evaluation, with no original text pixel parity, GPU/story/device,
physical Android or Windows/Linux runtime acceptance claim.

## Remaining and rollback

Widget triangle emission, per-frame aggregate budgets, viewport draw/upload/
retirement, source results-placeholder binding, ending audio transition and
receipt/document/root epoch fences remain implementation work. Layout choice
is not introduced here; native stacked screens and the reference's widescreen
recomposition must not be conflated. The original frontend stays unregistered.
Rollback removes these unused helpers; current ending fallback and campaign
progress continue through their existing owners.
