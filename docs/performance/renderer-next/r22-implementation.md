# R22 implementation and Windows validation

Implemented 2026-10-22 in `codex/renderer-qualification`. The preceding native
qualification campaign remains separate evidence; its timing results do not
automatically qualify this new mode.

## Selection and compatibility

`RenderOptions.LinearColor` is an explicit graphics setting, off by default. The
desktop label is **Linear world color (next scene)**; portable settings expose
the same saved field. Selection is frozen when a Scene is created, including
asynchronous decode requests. Existing settings use off; Original resets it to
off. No preset enables it automatically. Forced legacy wins.

Native textures retain their encoded GPU storage. Enhanced world draws use
separately owned scene variants. Native HUD glyphs, icons and artwork explicitly
exclude these variants. Promotion, recolor, replacement and teardown retire
variants with the original binding. Larger pooled backing arrays are bounded to
the declared image dimensions during preparation.

## Color domain

Prepared assets retain immutable role, transfer, alpha policy/factor and wrap
metadata. Albedo, emissive and color effects default to sRGB; normal, packed
specular/roughness and masks are linear numeric data. KTX declarations survive
decode, mip selection and upload. RGBA, BC7, ETC2 and ASTC select corresponding
sRGB/UNORM internal formats. Explicit sRGB numeric maps are rejected. Alpha is
linear throughout.

Hardware sRGB sampling decodes before filtering on the qualified Windows GPU.
Native vertex/material/light/fog/toon/outline RGB enters the linear world domain;
glTF linear factors bypass that decode. Lighting, emission, translucent blending
and AA operate in linear light. The working scene is deliberately LDR RGBA8:
exposure one, clipped highlights, no HDR tone mapper or bloom.

Successive reductions and final upscale/downsample operate on linear world
color. One IEC sRGB encode writes a presentation-sized RGBA8 output before preview
models and native model/2D HUD. Capture descriptors identify that encoding and
actual dimensions. Original captures retain their original domain/dimensions.
Desktop framebuffer-sRGB state is disabled/restored explicitly; GLES receives no
invalid desktop-only toggle.

Source-GX keeps its authored encoded TEV texture/combiner semantics as an
emulation boundary, then decodes final RGB before linear scene blending.
Reinterpreting TEV operands as physical linear lighting would change its source
material contract.

## Alpha, material math and filtering

glTF OPAQUE/MASK/BLEND, cutoff and base-color alpha factor survive rigid/weighted
loaders, recolors, render items and retained identity. OPAQUE ignores sampled and
factor alpha. MASK tests their product and normalizes surviving coverage. BLEND
retains it. Actor fade is separate. Color and depth use the same coverage helper;
BLEND is excluded from binary directional-shadow casting. Batches cannot merge
different alpha modes, cutoffs, factors or tint domains.

Authored alpha also works in encoded color fallback and authored previews. Native
direct HUD model draws clear that override. Original retains its conservative
OPAQUE KTX refusal; the new mode accepts it through shader coverage semantics
without rewriting compressed blocks.

Generated color mips and dimension-cap resizing filter decoded RGB, using
premultiplied color for transparent edges. Normals average then renormalize;
packed green roughness uses RMS filtering. MASK coverage is preserved against the
source, including the initial cap. Exact area footprints avoid introducing
wrap-dependent blur into generated mips.
Explicit repeat/mirror/clamp metadata controls bounded transparent RGB edge
dilation and draw sampling; ambiguous native/shared images receive no invented
padding policy. Character base/recolor samplers and map/pack wrap metadata carry
both axes through identity and filtering. Authored mip chains remain intact.
Atlas authors must provide valid island padding and authored coverage/mips;
whole-image wrap cannot invent padding between UV islands.

The mode enables camera-relative specular and guarded tangent reconstruction.
A guarded cofactor inverse-transpose handles nonuniform scale/reflected normals
on GLSL 1.20 and GLES 3.00.

Format-1 material packs accept optional alphaMode, alphaCutoff, alphaFactor and
channel transfer fields such as albedoTransfer/normalTransfer and optional paired
wrapS/wrapT fields; missing values retain legacy behavior. Map glTF import
preserves alpha and linear RGB factors
for enhanced materials while keeping identical native fallback bytes. Validation,
serialization and reimport identity carry this metadata.

## Failure and ownership

Output program/color/depth allocation is transactional and budgeted. Failure
retains native color rendering. Texture-variant refusal is sticky for the scene
and keeps alpha metadata for fallback. Late variant failure, resolve-budget
refusal or encode failure redraws the prepared world in the native color domain
before overlays, without repeating simulation or asset updates. Teardown
releases every variant and output owner.

## Windows evidence

All execution/builds ran on Windows. Receipts are outside Git under
`%LOCALAPPDATA%\ProjectPrime\r22-20261022`; owned files and captures remain local.
Failed attempts remain in separately numbered logs.

* Desktop GPU: 548 assertions, 35 captures. Includes transfer roundtrip, hardware
  sRGB filtering, linear half-white blending, native controls, camera/mirrored/
  nonuniform normals, OPAQUE/MASK/BLEND color and real D24S8 coverage, and actual
  Scene target allocation/reuse/rollback/resize/disposal.
* All 17 saved earlier native/material RGBA captures match byte for byte.
* Eight-player full scenes pass 25/50/100/150/200/800% scales, shadows through
  High. Encoded presentation dimensions and repeated prepared color/depth match.
* Material checks pass 62 CPU assertions and 22 new linear-material GPU
  assertions, alongside the existing upload/lifetime, source-bank, settings and
  provenance suites. Per-axis sampler state, character-owned retirement and
  unrelated resident survival are checked on the real Windows GL context.
* All 45 map glTF checks pass, alongside retained identity, graphics options and
  character parser checks. Desktop and Windows server builds pass with existing
  warnings only. Android ARM64 was built on Windows; its final source passes the
  Windows-hosted `Compile` target with existing warnings only.

Final native receipts use frozen `native-r22-v3` (client MVID
`863385d0-63c7-4c4d-803b-ebf2097aca49`). The final desktop rebuild is byte-identical
to that client and harness. `manifest-final4.json` verifies their SHA256 digests
against unchanged before/after build-input inventories; subsequent changes are
test-fixture assertions and this documentation. `original-visual-parity-final3.json`
and `linear-scale-capture-final3.json` retain the capture comparisons. Full receipt
inventory and source deltas are recorded in `r22-final-report.json`.

These are correctness checks, not a new performance or device rollout claim.
Android/GLES device visual acceptance remains unperformed under the Windows-only
test constraint. The mode remains opt-in.

References: [glTF material/alpha contract](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html),
[sRGB texture transfer and alpha](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_texture_sRGB.txt).
