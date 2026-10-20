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

Final frozen results are appended after platform and actual-source checks.
No original source coordinates, model bytes, images or text are committed.
The existing independent TXTR oracle remains a separate texture proof;
this slice does not claim an independent hardware/UV rendering oracle.

Full authoring/compiler propagation, final-emission companion generation,
strict companion/model/generation binding, shared desktop/ES paired UV
attributes, base/lightmap material policy, additional source families,
complete arena recipes and mechanics remain engineering work. Existing
ordinary readers and private preview recipes remain the rollback path.
