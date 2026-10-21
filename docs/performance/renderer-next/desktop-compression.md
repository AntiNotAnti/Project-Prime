# Desktop block-compression candidate (R17)

`PROJECT_PRIME_GL_BC7_TEXTURES=1` explicitly opts in; default is off and forced
legacy wins. The render owner checks the actual OpenGL 4.2+ or exact ARB BPTC extension contract before
decoders start. Workers read a volatile capability snapshot and never query GL.
No BC7 support is inferred from operating-system or vendor names.

Supported desktop contexts transcode portable ETC1S/UASTC KTX2 to BC7 RGBA and
retain authored levels. A quality cap chooses a fitting authored level before
managed mip allocation. A failed native transcode destroys/reopens its original
container before portable RGBA fallback, because a failed transcode may mutate
native state. Unsupported contexts retain portable Basis-to-RGBA; fixed-format
BC7 files require support and otherwise fail cleanly to native artwork. Use
portable Basis files for a cross-platform pack. Android ETC2/ASTC selection is
unchanged.

The upload path checks the currently published capability and never generates
compressed mips. Full/partial/1x1/NPOT residency contracts are shared with ETC2
and ASTC. Actual compressed bytes enter the existing admission ledger. Native
quality failures retain the ordinary native/portable fallback.

This candidate preserves the renderer's existing encoded-byte color domain:
BC7 uses BPTC_UNORM, including authored sRGB-tagged color containers, matching
the prior RGBA8 encoded-byte path. It does not claim linear lighting or alter
normal/mask transfer functions. Full asset transfer/alpha metadata and exactly-
once output encoding remain the separate R22 migration. Channel-specific
artist/pack quality, alpha edges, normal error, transcode peak and sustained
upload/frame-tail acceptance are UNVERIFIED; this path has NO_ROLLOUT.

The Windows native fixture creates an invented 64x64 authored mip container,
encodes it using the installed native KTX writer, and decodes the actual Basis
payload through production KTX decode. It selects 32x32 and smaller authored
levels, uploads the candidate, queries actual driver format/compressed storage
at every level, and compares decoded pixels against the same payload's RGBA
reference for albedo, normal, material and emissive semantic channels. The flat
fixture cannot establish artist/normal-map quality or end-to-end throughput.
Default-off and enabled executions use separate processes. Native results will
be recorded alongside the final integrated material receipts.

KTX writer/transcoder API contract: [Khronos libktx writer reference](https://github.khronos.org/KTX-Software/libktx/group__writer.html).
No new runtime library or package was added; the test-only writer entry point
uses the existing Ktx2.NET native dependency.

The first enabled native attempt failed because NVIDIA 617.42 omits BPTC
from its generic compressed-format list despite OpenGL 4.6 and the exact BPTC
extension. The failed probe is retained as bc7-probe-negative.log. Capability
selection now follows the [ARB BPTC specification](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_compression_bptc.txt).

The corrected Windows native check PASS on NVIDIA OpenGL 4.6 / 617.42,
Debug client MVID `6c6cc5be-32e7-4e60-b2ad-abbe9366e10a`:
`bc7-qualified-debug-build.log` and `bc7-qualified-gpu.log`. Each of four
semantic-channel fixtures stored **1,392 actual BC7 bytes versus 5,460 RGBA
bytes**, with zero worst-channel difference from the same payload's RGBA
reference. The total was 5,568 versus 21,840 bytes. These constant-color fixtures
establish native format/storage and exact scope only. Their ordered, concurrent
development-run decode/upload timings are diagnostics, not throughput acceptance.
The default-off execution passed in `texture-settings-gpu-default.log`.
The final integrated Release rerun is recorded separately.

