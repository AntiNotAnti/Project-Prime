# Echoes body material and texture authoring

Implementation slice following PR878. This is private import work, not source
shader activation or playable-character completion. Normal and Overcharged
body imports now retain their complete original material-set records and all
TXTR resources referenced by any local material slot in one required
`.materials.bin` component. Related ball and Trooper materials remain future
work. Source TEV rendering, Overcharged runtime admission, first-person firing,
Trooper abilities and selection/network/replay remain unfinished.

## Ownership and behavior

`RetroCharacterMaterialArchive` is a pure, bounded CPU codec. The existing
`RetroCharacterPreviewExporter.WritePart` owns extraction under the verified
Echoes source requirements. The existing `LocalImportBundle` owns staging,
integrity receipts, cancellation, immutable generations and publication.
Normal advances to bundle6/body5, Overcharged to bundle5/body4; ball stays3.
Updating a prior bundle is deliberate. Its old generation is retained, and a
failed or cancelled update cannot publish the new receipt.

The archive stores the complete first source material-set section, including
opaque stage/binding bytes and record/set tails. Parsing a material object and
serializing it again would lose those bytes. Every referenced material-local
texture slot is covered, even when no TEV stage executes that slot. Shared
TXTRs are stored once in sorted resource order. Local slot, material-set table
index, TXTR ID, TEV texture slot and texgen index remain separate namespaces.
Unknown shader operations remain raw authoring data; they are not silently
translated, truncated, masked into a supported shader or executed.

The header binds appearance, pinned source fingerprint, source CMDL/CSKR/CINF/
ANCS IDs, full CMDL and CSKR hashes, generated diagnostic GLB hash, raw material
set hash and the existing pixel contract defined by
`RetroTextureSource.PixelContract` (`gx-expanded-cmpr-3-5-rgb5a3-v1`).
Original tiled mip chains and padding are validated and retained in the TXTR
bytes. The decoded pixel hash covers top-first straight RGBA8 with source
alpha preserved. No original-mip sampling or hardware-pixel fidelity is claimed.

The format has a 256-byte header and 112-byte sorted texture directory entries.
All payload extents are contiguous, bounded and individually hashed. Reserved
fields must be zero; missing/duplicate textures, mismatched source references,
invalid mip extents, integrity errors and trailing bytes refuse the complete
component. Maximums are 32MiB archive/CMDL, 2MiB material set, 32 materials,
64 textures, 4MiB per TXTR/CSKR/decoded image and 16MiB aggregate decoded pixels.
All texture layouts and aggregate budgets pass before pixel decoding begins.
CMDL extraction bounds its entire section directory before accessing the
first material set and requires the observed body envelope version4. This
does not expose the legacy complete geometry parser as an untrusted reader.

The reader validates a detached copy. Its lists and raw byte spans are read
only; `ReadProgram` returns a fresh mutable parser object whose mutation cannot
change the snapshot. Texture decoding returns independent immutable pixels.
The codec owns no files, GL objects, model caches, source lease, Scene or clock.
Successful structural decoding is not trusted source/UV evidence or a runtime
capability. A later rendering owner must bind the same verified generation,
source/geometry identities and independently qualified semantic profile.

Diagnostic GLB generation, provenance, normal fixed-cannon inputs/recipe,
related ball components, default native gameplay, Hunter identities, protocol
and replay layouts are unchanged. No new public selection or package route
is registered. Rollback is to the preceding importer recipe; retained old
source generations can be reused by that code without deleting user saves or
original source discs.

## Qualification

The final frozen source is `fd82d313752f2195b595e4fc51ceeacd74fdc857`,
following initial material production/test `3a506431cf960f9b0ed2094c02b398ecf92a710f`.
The only production difference is one removed closing parenthesis in the
inherited ANDROID binding-cache policy expression. The initial Android build
failed at that syntax error; its command/log and unchanged-source results are
retained. The repair preserves the desktop expression and the intended Android
opt-in/force-legacy precedence. This slice adds no gameplay/protocol/replay change.

The final source tar SHA256 is
`406ae1d424ec9033a13bfd612940755c4f6e6b88c3adcc6baec3726831bbec54`.
Windows access was restored through the existing SSH control session. Exact
SDK10.0.401 qualification on DESKTOP-94724L4 passes all seven commands:
desktop/dedicated Release, both preview/import tool builds and complete
source-free defaults, and the pure character-model check. Both 6,194-file
tracked source trees match before/after. Each core build retains one inherited
CS0649 unassigned test-field warning; tool builds have zero warnings/errors.
Windows explicitly skips the existing Mac-arm64-only private source-loader
fixtures. No Windows owned media, native GL, physical device, CI or review
qualification is inferred. Other QA processes/checkouts were untouched.

The Mac final frozen source-free suites and actual owned Echoes checks pass.
The new raw transport fixture passes 385 synthetic checks: independent byte
layout, namespace asymmetry, exact opaque/negative-zero retention, source and
pixel budgets, every truncated header, corruption, detached ownership and
cancellation. Existing generated GLB/cannon/default Prime recipe checks remain
in place. No shader/GPU proof is inferred from those CPU fixtures.

Actual owned imports cover all seven normal and nine Overcharged raw material
records and every referenced slot, including TEV-unused slots. Original TXTR
bytes, complete mip/padding extents and every top-level RGBA texel agree with
an independent logical-coordinate RGB5A3/CMPR oracle. Normal has ten textures /
49,664 pixels; Overcharged has twelve /188,416 pixels. Their union is twenty
textures /919,552 RGBA bytes; shared images are counted once in that union.
Repeated emitted profile rows describe repairs/migrations, not independent
source editions.

| Body | Required archive bytes | Archive SHA256 |
| --- | ---: | --- |
| Normal | 52,056 | `6a5666bfb14ee989658c7543543429d8de824820bee2531f0051a21a42be86d7` |
| Overcharged | 202,340 | `69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d` |

Missing material components make the affected body Incomplete while verified
siblings/ball stay Ready. Repair preserves every old sibling and emits exact
new bytes in a fresh generation. Actual prior normal-v5/Overcharged-v4 complete
receipts are valid under their old recipes and Outdated under the new ones;
inline cancellation at BeforeCommit preserves their receipt and every old
component. Updates, repeated imports, cancellation, removal and reimport pass.
Existing diagnostic body/ball GLB pins, provenance and normal cannon bytes stay
unchanged. The owned source SHA256 before/after remains
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`;
all derived probe data is deleted.

A separate metadata-only helper against the exact frozen Mac DLL captures all
46 committed component lengths/hashes (normal24, Overcharged22), passing 54
checks. Source, core/helper binaries and all five Read caches are unchanged;
all derived output is deleted. The first helper build failed because it tried
to call an internal path-check API; that access-only harness failure is retained.
The corrected helper uses public Inspect and explicit receipt-name/hash checks.

All 19 serial final Mac commands pass, including desktop Release, three native
runtime verifications, RML pages, preview/import/settings/game-files source-free
suites, both owned-source probes, the character-model check, standalone Android
Debug, Release/AOT and APK signature verification. All 6,194 tracked source files
match after the complete run. Desktop retains the inherited unassigned test
field warning; both Android builds retain 26 inherited warnings /zero errors.
The Release APK has 119 AOT libraries for each of arm64-v8a and x86_64; v2/v3
signature verification passes.

| Frozen output | SHA256 |
| --- | --- |
| Windows desktop DLL | `bc1903ff1e7837da1d45cdf68c5506e8239a059791904392456e3817dfca6a25` |
| Windows dedicated DLL | `eb13a6b86035fd373fcf7d8a033b69e998e6a6b021b009c0e9d36e7caed57bab` |
| Mac desktop DLL | `72224b3a3003c4930e1c4b5c62be420d48ecf7648112015f7654aa0f9bfe79bc` |
| Mac import-tool DLL | `9d5c5c3e4959805a8e787b1f003095246b7625a3d4df0c7b69cb6b62c429f54d` |
| Android standalone Debug APK | `07d28091c8c311a0d6389bb14e3ba016c09298de14e9ffddae15696be122b2fe` |
| Android signed Release/AOT APK | `e9e49a1a29145c6ed8d2a1397a8d12982a5b55754d506d329b74f4d582d141d8` |

Actual API36 arm64 emulator qualification uses only the isolated
`com.projectprime.sourceaccesscheck` standalone Debug package. The old c66 APK
and final fd82 APK share certificate SHA256
`5333a64c8c2cd6d9ee7cab995cf810c1469c44fee23ecc828053ca62cb027288`.
Old imports publish normal-v5/23 and Overcharged-v4/21 files. Updating the APK
with `-r` preserves their exact receipts and all44 components. Fresh Check
reports both Outdated; the actual source chooser/ContentResolver/snapshot/
default importer upgrades them to normal-v6/24 and Overcharged-v5/22, both Ready,
with ball3 retained. The same active view displays actual progress and completion.
Repeating that group import preserves both receipt hashes, generations and all46
components. Remove Cancel preserves the normal receipt; Confirm unpublishes
only normal while Overcharged stays unchanged. Old immutable generations remain
until final test-data cleanup.

Across old/post-package-update/new/repeat stages,180 independent component
length/hash comparisons pass. Desktop comparison is44/46 byte-identical,
including both new material archives, GLBs, provenance, cannon, full animation
banks, raw skeletons and all ball files. Only the two body `.motions.json` files
differ, each Android file five bytes shorter. Their cause/numeric magnitude is
unestablished; cross-platform generation, sparse-motion-byte and decoded-pose
parity are not claimed. Repeated Android imports themselves remain exact.

During setup the old c66 APK showed a startup ANR before any importer job.
One Wait action recovered it without a reset/relaunch; the cause is unknown.
That event is retained separately and is not hidden as a passing crash test.
The source push and one read-only setup query were also slow under observed
host load. No process-death, activity-recreate, low-space, revoked-provider,
cloud/SD or physical-device test is claimed in this slice. The final own-UID
crash buffer is empty; that is not a broad crash-suite result.

The original RVZ and both APKs retain their hashes. The unique emulator source
copy, MediaStore entry, isolated app, internal/external/scratch directories and
all source-derived bundles are deleted and absence-verified. No screenshots or
protected payloads were retained. The main app, other QA and emulator settings
were untouched. Release/AOT is build/signature qualification; the exercised APK
is Debug. No rendered-material, native/private Windows runtime, GLES source
rendering or physical-platform acceptance is inferred.

Local ignored receipts live under `artifacts/sh-integration/` in
`character-material-final-fd82d3137`,
`windows-character-source-materials-fd82d3137` and
`character-material-desktop-components-fd82d3137` and
`android-character-source-materials-fd82d3137`; predecessor/failure logs are
retained under their exact source/attempt identities. These contain metadata,
commands and logs, not source payloads or source-model screenshots.
