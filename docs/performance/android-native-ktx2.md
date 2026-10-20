# Android GPU-native KTX2 texture uploads

Project Prime can retain Basis/KTX2 replacement textures in a GPU-native
compressed format instead of always expanding them to RGBA8. Android GLES3
guarantees ETC2 RGBA8; devices advertising ASTC LDR can use ASTC 4x4. The EGL
owner publishes the device preference, and off-thread decoders never query GL.

The entire mip chain is validated against 4x4/16-byte blocks and KTX2 offsets
before promotion. Resident bytes count the actual block payloads. Oversized
source images use the existing RGBA downsample, while failed replacement
uploads keep the native cartridge texture and follow existing cleanup paths.
This does not affect desktop or server rendering.

**Opt out:** PROJECT_PRIME_ANDROID_GPU_TEXTURES=0 or global
PROJECT_PRIME_ANDROID_PERF_OPT_IN=0. Restart after changing the option.

## Required acceptance
- ETC1S/UASTC, alpha, complete/incomplete mips, quality caps, corrupt offsets.
- Adreno ETC2 and Mali/ASTC hardware: draw parity, shader material/normal and
  alpha correctness, context loss/rebuild and repeated match load/unload.
- A/B GPU memory, upload stalls, 1% lows, p95/p99, peak RSS and thermals over
  30-minute sessions on actual devices.
- Reject if shader/texture parity or fallback behavior regresses.

This implementation requires device testing; it does not claim measured gains.
