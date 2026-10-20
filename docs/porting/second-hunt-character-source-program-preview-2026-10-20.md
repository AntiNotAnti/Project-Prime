# Echoes body source-program preview

This slice connects the two exact generated body profiles to the existing
private source-rig viewer. It is not playable-character integration. Normal's
fixed diagnostic cannon remains separate; Final has no cannon capability.
The existing macOS/private source-root boundary remains in force. Android
imports and shader-text generation do not activate Android source-rig drawing.

## Rendering contract

`CharacterSourceGxProgram.Policy` identifies a floating preview interpretation:
raw converted-object normals drive texture generation; PN-skinned normals drive
an authored camera-space directional preview light. Material/ambient registers
are white, initial TEV registers zero, and exposure one. This is not an original
Echoes actor/room lighting rig or fixed-point hardware-GX pixel parity.

All seven normal and nine Final programs are admitted together. The compiler
refuses unsupported inventories, operations, channels, generators and blend
factors. It keeps original material ordinals through the existing <=32-joint
partitions; physical batch indices are never source material identities.
Complete archive and generated-geometry pins bind the unchanged source UV0
stream to the independently audited UV1/UV2 aliases of these two bodies only.
A generic model with distinct secondary UVs is not admitted by those pins.

Each floating ADD/clamp stage gathers all RGB and alpha operands before either
register write. KAlpha selector0 means ONE. Effective initial-register
requirements preserve stage order and simplify ZERO/ONE/equal-input mix terms.
No external GPU register state is borrowed. Primary matrix selection,
conditional normalization, independent post selection and interpolated STQ
remain distinct. Mode2 preserves both offsets and both rates; finite caller
time wraps at900seconds. A per-viewer material clock is independent of looping
animation time and stops while that viewer is paused or hidden.

Textures use complete raw archive source/TXTR/RGBA identities and the existing
Scene texture manager. The explicit preview recipe is linear, repeatS/T,
top-level RGBA, no regenerated mips, anisotropy1; alpha is retained. It does
not reinterpret textures as PBR maps. Opaque, alpha and additive passes remain
stable by original submission order within each pass, with LEQUAL depth,
DepthWrite&&!DepthSort writes, and unculled preview geometry. No per-triangle
original transparency ordering or source cull enum is claimed.

Scene owns programs and texture residency. All required programs and images
must prepare before body publication. A failed link rolls back newly created
programs; bounded fixed image residents may remain reusable until Scene
retirement. Closing or refreshing withdraws draw authority immediately;
closing does not claim to free shared pixels. The existing geometry submission
path draws each source item and restores native program, four texture units,
active unit and compatibility raster state. Admission restores unpack/PBO
state, including failure paths. No new graphics context, renderer, model cache,
watcher, gameplay clock or wire identity is introduced.

## Import and owner identities

Normal remains bundle6/body5/ball3, preserving generated manifest, cannon,
geometry, provenance, skeleton and full animation bank bytes. Final becomes
bundle6/body5/ball3, adding its own required one-Biped `characters.json` to the
previous source-authoring inventory. Unique staging names prevent Update-all
from overwriting either appearance's manifest before independent publication.
Earlier Final receipts require Update; cancellation preserves the prior receipt
and all its files. Prime recipes remain unchanged.

Final's appearance mapping is enabled only for an exact generated source
profile. Legacy format1/native, mobile, nonempty BoneMap, cannon, alternate
form and source swaps remain refused. Normal and Final have distinct source
CMDL/CSKR, generated GLB, animation-bank, material-archive and graph bindings;
they share the independently pinned39-bone CINF. Captured material hash/policy
join the existing lease cache identity. Adding material authority retires
previous diagnostic leases; cached draw validation remains free of file IO.

## Qualification

The production slice froze at `8b25900f9eb3ba96ce671c6cef040419d2146579`.
The test-only successors `d425d16c3` and `97ce04334` leave the complete `src/`
tree byte-identical. Windows first passed seven source-free commands at8b:
desktop/dedicated Release, both import/preview tool builds and full defaults,
and the character model check. The d425 external-tool rebuild/default pair
also passed against the unchanged Windows production DLL. No owned media or
private source GL capability was transferred to Windows.

Frozen d425 Mac desktop/tool/source qualification passed the complete normal
and Final import/migration/cancellation probes, nonseekable source document,
generated cannon and strict generated profile admission. The synthetic actual
Scene GL suite passes416 checks, including50 new source-program assertions.
The pure compiler suite adds124 assertions for independent TEV arithmetic,
register writes, primary/post/STQ domains, finite clocks and refusal bounds.
Mac uses OpenGL2.1 Metal91.7; generated GLES300 text has CPU checks only, without
GLES compilation or source-body drawing qualification.

The final owned-source test-only calibration passes1,629 checks/86 frames in
two recreated contexts against the exact frozen Mac core
`8ffd170fce777edeee32e193f5057645857fe8e045ea146034e0994ab081ae9f`.
Both bodies submit all7/9 programs and6,340/12,734 triangles. Actual clip13
changes palettes and pixels, all four views differ, body/cannon/body returns
exactly, and Scene retirement releases captured programs/textures/lists.
Injected second-program link failure proves rollback before publication and
exact dirty upload/PBO/program/unit restoration; fixed resident images are
reused on successful retry. Native source-page tests separately pass at960x600
and640x480, including appearance capability, Final cannon refusal, pause,
rotation/zoom, hide/resume, Back/dispose and stale input retirement.

The clock probe checks every actual submitted packet time and every active
program-zero GPU primary row bit-for-bit against the compiler's CPU matrix values.
Normal uses9 active rows and Final6. All A/B/A palettes, models, lists, textures
and programs remain stable. Final changes visible pixels at all four angles;
normal's full-body pixels remain identical at0 and30.25 seconds despite
changed exact GPU rows and nonzero scroll rates. The reason is unknown: these
owned views have no independent visibility/quantization oracle. This evidence
does **not** claim visible normal material animation. Invented known-texel
GPU fixtures independently qualify visible scrolling and modulo900 timing.

Retained failures distinguish setup/test defects from production results.
The initial native verifier rejected a cached bridge from before unrelated
main native changes; all three bridges were rebuilt and verified against the
exact current16-file native closure, fingerprint
`63103845dbe581aa41f6498a6fa5584f6072a6b5a3b6258d1d38ecc780a06d60`.
Two isolated tool-only diagnostic setups lacked existing reference/apphost
build outputs and stopped before probing. The original owned clock assertion
incorrectly required full-body pixel changes and was calibrated as described
above. A tight second-context cannon-to-body test also arrived before the
native60Hz model step; the harness now allows one20ms native frame only on
part changes and still requires its next single draw to succeed. Production
source was unchanged through these test corrections. Logs preserve each
attempt, including a native-UI continuation setup NameError before any build.

Local ignored receipts are under `artifacts/sh-integration/`:

- `windows-character-source-gx-8b25900f9` and `windows-character-source-gx-d425d16c3`;
- `character-gx-final-d425d16c3-attempt-02`, including `continued-native-ui`;
- `character-source-gx-clock-diagnostic-d425d16c3/attempt-05` (final owned GPU),
  with prior failures retained;
- `character-source-gx-native-rebuild-d425d16c3`.

Owned Echoes RVZ SHA256 remains
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`;
the configured native source inventory and core/tool assemblies also remain
unchanged during the GPU probes. All temporary derived payloads are deleted;
receipts retain metadata/hashes, without protected captures or assets.

Frozen d425 platform continuation passes4/4:910 character-model checks,
standalone Android Debug, Release/AOT and signature verification. Both Android
builds have26 inherited warnings and zero errors; signed Release contains119
correct ELF64 AArch64 libraries and119 x86_64 libraries, with v2/v3 verification.
Debug APK SHA256 is `b9e280260ff05e4bfc43365b6e632bc6f87927a607b512014c3e5c1969001413`;
Release is `b69f04aad4f5e4cb0bee4390ee891e277c8c7638ac927d8869ccc998bedba10b`.
All6,237 frozen tracked files and the Mac desktop DLL remain unchanged.
See `character-gx-final-d425d16c3-attempt-02/continued-platform/summary.json`.

The clean main integration froze at `4d049ead2ba98a7d6b44d0982e6b5ad992ee94af`
and passed all seven Windows commands again. Both6,281-file tracked source
trees remain exact and both tool core copies match the desktop DLL. Desktop
and dedicated builds each report one inherited CS0649 warning; tool builds
report zero warnings/errors. macOS-only source-rig checks are explicitly skipped
on Windows. See `windows-character-source-gx-4d049ead2/summary.json`. This
integration evidence qualifies builds and source-free checks for the combined
source; owned GPU/Android results above remain scoped to the frozen production.

Actual Android emulator5580 SAF acceptance passes the old fd82 to d425
in-place update, complete group upgrade, repeat, removal cancellation and
independent removal. Normal's receipt and all24 components stay exact. Final
changes5/body4 to6/body5, adds only its own canonical Biped manifest and changes
its parts index;21 existing source siblings stay exact. All47 new current files
match independently measured lengths/SHA256, repeat retains both exact
generations/receipts, and69 unique retained generation files remain unchanged
after removal. The run performs186 independent component length/hash pairs.
The uniquely copied emulator RVZ, exact MediaStore row and own test package
are removed; local source/APKs, main package and global settings hashes remain
unchanged. Evidence is `android-character-gx-d425d16c3/summary.json` and
`cleanup-and-integrity.json`, metadata only. No Android GX drawing or physical
device qualification is implied. Setup navigation retries and the need to
Apply Changes to restore write buttons after selector navigation are retained
as observations with unknown cause; no importer failure or ANR was observed.

Gameplay, Trooper abilities, normal firing/cannon materials,
balls/morph, character selection/network/replay, physical-device rendering and
performance remain unfinished. This increment qualifies the private viewer.
