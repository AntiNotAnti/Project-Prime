# Native menu motion capture

Runs the actual Metal chamber shader, atmosphere pass, engine Hunter previews,
and RmlUi draw-list compositor in an isolated hidden GLFW window. Requires an
active macOS display, the native bridge, built game assemblies/native libraries,
and an extracted-game `paths.txt`. Uses temporary preferences; it does not join a
server, publish maps, read account credentials, or record a live multiplayer game.
Roster, party and countdown fields are fixtures. Stock Hunter assets are used.

```sh
dotnet run --project tools/menu-motion-check \
  -p:PrimeAssemblyDirectory="$PWD/src/MphRead/bin/Debug/net10.0" -- \
  /tmp/prime-menu-capture \
  artifacts/native-check/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/bin/Debug/net10.0/rmlui /absolute/path/to/paths.txt
```

Produces seven Hunter themes, six current activity presets, one/four/eight-player
formations at multiple timestamps, ready/arrival/departure/countdown/cancellation,
rear-slot tall-Hunter cases, Reduce Motion and `performance.txt`. The synthetic
presentation clock makes environmental phases reproducible. Native RmlUi retains
its own steady clock; captures use its Reduce Motion flag for stable DOM layouts.
Models use their normal bounded fixed-step previews plus presentation sampling.

The same harness can reference an older built engine via `PrimeAssemblyDirectory`.
New motion APIs are discovered by reflection; no baseline source changes are
required. Add `--benchmark-only` to skip the gallery. Do not run baseline and new
builds concurrently through the same tool output directory.

Benchmark: 20 warm-up frames, then 120 chamber + FX samples without Hunter models,
UI rasterization or screenshot readback. Measures serialized CPU + GPU completion
using `Finish`, with `Present` timed separately; the completion number includes
any surface-acquire pacing and is not pure shader GPU time. Default graphics
quality is retained (Medium). Allocations include the diagnostic completion and
timestamp machinery. Timestamp samples that all read zero are invalid evidence
of GPU duration, even if the device advertises support. This short test is not a
full-game FPS or multi-platform performance claim.
