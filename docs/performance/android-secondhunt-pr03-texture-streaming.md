# PR-03: Android HD texture promotion pacing

The current renderer *already* decodes replacement textures off-thread, has a
one-decoder limit on Android, preserves the native cartridge texture until
promotion, and uploads at most one prepared image per frame. It would be
incorrect to add a competing asset queue.

This PR adds bounded recovery spacing after a measured expensive OpenGL
promotion to avoid **consecutive** texture-upload stalls. A nominal 2ms
budget controls spacing, not the duration of a single `glTexImage2D`
(or mip-generation) call: OpenGL ES does not support preempting it. GPU
uploads still run on the context owner. Desktop behavior is unchanged.
If background decoding fails, the valid native texture remains in place.

Do not use this PR to claim p99 will improve on every device; one very
large upload can still exceed the frame budget. Truly interruptible
promotions would require a separate staging texture plus atomic binding
publication and context-loss-safe ownership, and need a subsequent PR.

## Validation

- `dotnet run --project tools/android-pacing-check -c Release`
- A/B original and HD materials on an Android 60/120Hz device, including
  repeated map loads, hot texture-quality changes, context loss, and 8-player
  matches. Inspect GL upload times, p95/p99, GPU memory, and visible fallback
  continuity under slow decoding.
- Large compressed/8K textures must not produce a unbounded queue or
  unhandled decoder exception; default textures remain correct.
- No frame-rate cap, renderer, or multiplayer state is modified.

Rollback: remove the `ModernTexturePromotionPacer` use in
`PumpModernTextureStreaming`; previous single-promotion/frame behavior remains.
