# Echoes source coordinate preservation

This SH-04 source-admission slice follows merged PR818. It preserves the
common decoded-program candidate's paired UV data before conversion loses
per-corner attribute indices. It introduces no map edition, authoring UV
schema, compiled companion, shader, GPU resource or rendering activation.

## Shared material bounds

CMDL characters and MREA world materials continue to use `Cmdl.ReadMaterial`.
Its supported profile now proves the supplied start/end slice before any
array allocation and uses an independent bounded reader for the declared
UV-animation block. Following bytes in the enclosing array cannot satisfy
a truncated material. The profile caps a material at1MiB, texture slots8,
konst colors4, color channels2, TEV stages16, texgens8 and UV animations8.
Both the20-byte stage bodies and their later4-byte texture bindings must
fit before stages are allocated. Known UV-animation parameters must be
finite; unknown zero-parameter modes remain retained metadata. Opaque
material tail bytes remain allowed as in the previous reader.

These are explicit supported-profile bounds, not a claim to execute every
Retro material family. Existing decoded field values and authoring owners
remain the contract; actual-source compatibility is checked below.

## Opt-in coordinate contract

`EchoesWorldGeometrySource.ReadWithSourceCoordinates` extends the same
traversal used by ordinary `Read` and public `DecodeSurface`. Its contract
is `echoes-base-float-t1-lightmap-s16-t0-v1`: T1 indexes the current model's
float32 base UV pool; T0 indexes its signed-int16 lightmap pool divided by
32768. Source floats stay normalized values, including negative/repeating
base UVs. They never enter native texel packing or its1/16 quantization.

Detached readonly material/triangle records retain material ordinals,
source program SHA256, resolved TXTR resource bindings and both coordinate
pairs together. Lists, alternating strips, fans and duplicate-position
degenerate filtering retain exact triangle/corner correspondence. UV seams
remain per corner rather than being joined by position index. Model-local
pool bounds are independent from position counts and previous models.
Only referenced candidate float UVs must be finite; unused pool values
and source alignment data are not invented coordinates or a new reason
to reject historical positional reads.

The common candidate compares the complete decoded semantic signature
recorded in the material gap audit: flags, channel words, blend factors,
texture/stage/texgen counts, both stage input/op/binding selectors and
texgen source/matrix/normalization/post fields; no konst, indirect stage or
UV animation. Opaque material bytes also influence the retained program
digest. Candidate identification preserves source evidence; it does not
qualify opaque bytes, C1/RASC lighting inputs or a renderer program.
Other families retain material identity and null coordinate metadata;
they are never assigned fabricated zero lightmaps.

Before primitive allocation, every descriptor-derived byte extent must
fit. The opt-in path explicitly refuses the inherited reader's unhandled
T7 attribute instead of silently misaligning subsequent primitives; it
does not claim an expanded GX attribute profile. It caps source corner visits at2million, each UV pool
at2million pairs, aggregate pool extents at64MiB and retained logical
triangle-coordinate metadata at32MiB, alongside the existing world model,
material, surface, position and triangle caps. Original candidate GX
corner occurrences are reported separately from expanded triangle corners.

## Verification and limits

Source-free tests exercise declared-end boundaries with complete following
backing data, capacities/overflow, every supported animation arity and
nonfinite parameters, model-local UV seams/bit identity, triangle expansion,
per-field candidate negatives, malformed index/primitive/source counts,
readonly output and corner-work admission. The owned probe emits only
hashes/counts/bounds/resource identities and compares positional outputs
with ordinary reads; a separate pre-change frozen DLL supplies independent
legacy position/index hashes for all six arenas.

Final frozen results are appended below.
No original source coordinates, model bytes, images or text are committed.
The existing independent TXTR oracle remains a separate texture proof;
this slice does not claim an independent hardware/UV rendering oracle.

Full authoring/compiler propagation, final-emission companion generation,
strict companion/model/generation binding, shared desktop/ES paired UV
attributes, base/lightmap material policy, additional source families,
complete arena recipes and mechanics remain engineering work. Existing
ordinary readers and private preview recipes remain the rollback path.

## Frozen acceptance result

Final combined source/tests: `aa63002249f5ced5019b735f2118587c6a9339e2`, tree `f435feaf2f1f14780fccad3dfa901dbbab8b10bf`, protocol67 after concurrent merged PR819. All23 serial commands passed from exact clean archives with5900 tracked files unchanged in each. Each process used fresh user data/TMPDIR; every context was deleted. Final documentation changes no production/test bytes. Windows SSH authentication remained unavailable at the preceding bank check, so the authorized Mac fallback continued.

- Desktop/dedicated Release:0 warnings,0 errors. Android Release:119 AOT assemblies,25 existing XML documentation warnings,0 errors; APK v2/v3 signatures verify, with both arm64-v8a/x86_64 ProjectPrime AOT libraries.
-101 source-coordinate and952 material-bound checks pass. The warmed8-byte input declaring a huge count allocates792 managed bytes before refusal, below1MiB. Existing TXTR191, bank137, bank-material128, source-rig904, map editor486 and material-tool58 checks pass. Pure custom-weapon contracts10586 and protocol67 replay543/314/84/51 plus campaign detail91 pass; these do not claim live replica authority or source visual acceptance.
- All six actual arenas preserve41,577 position entries and68,470 triangles exactly against independent frozen pre-change DLL SHA256 `96d576518b8752a05bb1c2c9abf1d83beea88c4ac6b6246492c6d238b861224b` from `e1bbd3c2043b28170880c1595882d0f87182e0d3`. Each map matches both float32-position and int32-triangle-index SHA256 including order. Candidate metadata covers260 of347 materials;137,440 total original corners include109,303 candidate occurrences, expanding to54,069 candidate triangles/162,207 paired corners. Negative/repeated base values and exact source float/short semantics are retained. Other families remain null coordinate metadata.
- RVZ SHA256 stays `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`. The coordinate probe writes no source/model/UV/image files and leaves no temporary probe files. The separate actual character bundle regression creates isolated temporary authoring data, verifies normal/Overcharged body+ball, repair/reuse/cancel/remove, and deletes all derived output. Source remains unchanged.
- Existing synthetic material GL checks pass exact bank pixels, queue/supersession/lifetime and38/38 companion handle retirement. The prior Apple editor sampler diagnostic remains in the log; final GL assertions pass. No source coordinate attributes, shader program or actual Echoes material draw are activated by this slice.

Binary SHA256 values:

| Binary | SHA256 |
| --- | --- |
| desktop | `2cf1dbd25255b3dd6bc0ece9890739a76362c0488dd2229b9dbb14b35e353554` |
| dedicated | `10f55232d32dab33e2bdab49d58aeb2d4f1ebfa716d26ed7947cb2a8912b6f61` |
| android_signed_apk | `f26fb527159989d19cb513c9db1f1bd21466ab4a8b671c10c82485f8be28c6ba` |

Ignored receipts: `artifacts/sh-integration/coordinates-aa6300224/summary.json`, exact commands/logs/source manifests and `frozen-geometry-comparison.json`. Independent old-DLL witness is retained as hashes/counts under `source-coordinate-exploratory`. The earlier protocol66 freeze `889701a64` passed21 commands and remains predecessor evidence; import production and external-tool source files are byte-identical in the final67 union. No CI or external-review claim is made.
