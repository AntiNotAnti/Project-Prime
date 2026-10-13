# Native PNG callback ownership

The unchanged main and frozen consolidation client both aborted during actual
replay PNG export when optimized JIT was selected. The owned-process crash
summary identifies `CallbackOnCollectedDelegate` followed by
`stbi_write_png_to_func`; this is independent of the existing one-pixel 30 FPS
visual mismatch.

The resolved ReFuel.StbImage 2.1.1 package identifies repository commit
`89d6b5f236fd140f3af0637cb73b5d5b8760631a`. Its [high-level PNG method](https://raw.githubusercontent.com/ReFuelGameEngine/ReFuel.StbImage/89d6b5f236fd140f3af0637cb73b5d5b8760631a/ReFuel.StbImage/StbImage.cs)
creates a write wrapper, converts it to an unmanaged pointer, and calls STB
without a post-call liveness boundary. The [public wrapper](https://raw.githubusercontent.com/ReFuelGameEngine/ReFuel.StbImage/89d6b5f236fd140f3af0637cb73b5d5b8760631a/ReFuel.StbImage/StbiStreamWrapper.cs)
owns the actual readonly callback delegate. Its documentation requires keeping
the wrapper alive during the write. Keeping only the destination stream alive
cannot root that delegate.

A standalone project referencing only the package reproduced the fatal collected
`StbiWriteProc` callback on its first write under concurrent GC pressure. No GL,
audio, assets or game initialization were involved. The final immutable harness
also reproduces this baseline failure and passes both corrected modes, separating
PNG ownership from rendering and runtime-loop hypotheses.

The production helper owns the existing public wrapper, invokes the existing
native PNG function with the same pinned byte span, dimensions, components and
stride, and keeps that wrapper alive in `finally` through native return. This
roots wrapper → delegate → forwarding stream → caller destination. The forwarding
stream records write exceptions and rethrows the original exception after STB
returns, allowing native cleanup to finish without unwinding a managed exception
through the callback. The destination remains caller-owned.

All existing PNG call sites use the helper: screen captures, both thumbnail
workers, image/movie exports and diagnostics. Caller flip settings, native
compression/filter options, RGB/RGBA bytes, alpha and encoding are preserved.
Android's existing `PngWriter` override is unchanged. There is no new library,
custom P/Invoke, global callback cache, production GC or compilation setting.
Both `MainApplication.cs:102` and `PreviewService.cs:109` install `AndroidPng.Write`.
All three screen/thumbnail paths still choose that override before the STB
fallback. The Android package still excludes ReFuel native assets; existing
STB-only image/movie/diagnostic export routes do not acquire Android support from
this helper. This is a source-boundary check, not a physical-device test.

## Validation

On .NET 10.0.12/macOS ARM64, the final same-harness controls reported:

- Baseline: expected SIGABRT on the first native write, collected delegate named.
- Candidate optimized JIT: eight RGB/RGBA writes under 41,896 forced collections,
  decoded pixels/alpha/bottom-up orientation exact.
- Candidate default JIT: the same checks under 39,895 forced collections passed.
- Grey, GreyAlpha, RGB and RGBA under both flip settings: exact package PNG byte
  parity and decoded pixels, including explicit alpha 0, 127 and 255. The small
  package parity reference uses a temporary no-GC region solely to isolate its
  known callback bug; production has no such policy.
- Injected stream failure: original exception identity, caller stream ownership
  and subsequent encoder reuse passed.
- Desktop/server Release builds: zero errors, 30/22 existing warnings.
- Final-composite tool build: zero warnings/errors; compile only.
- OpenGL/GLES source/package guard, workflow YAML syntax and whitespace passed.

The new independent CI matrix runs this PNG-only project on Ubuntu, Windows,
macOS ARM and macOS Intel, with native assets resolved from the existing package.
Remote execution remains pending at publication. CI runs only the candidate;
the intentionally process-fatal baseline is a local isolated-child control.

All early and final child receipts/logs, source and binary hashes are in
[png-callback-ownership-receipt.json](png-callback-ownership-receipt.json).
Bulky PNGs, upstream source snapshots and crash/log files remain local under
`artifacts/png-callback-lifetime`. The original main/frozen GL abort summaries
remain in their separate robustness artifacts.

This closes a reproduced callback-lifetime error. It does not establish complete
replay visual determinism, driver memory boundedness, physical Android encoding
or performance, and does not add a pixel tolerance or waive any replay gate.
