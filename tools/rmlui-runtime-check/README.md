# Managed RmlUi runtime contracts

```sh
dotnet run --project tools/rmlui-runtime-check
dotnet run --project tools/rmlui-runtime-check -- --native /absolute/native/bridge /absolute/rmlui/assets
```

The default run uses a fault-injectable bridge to check typed intent packets,
owner-thread access, copied snapshots, cancellation, stale focus and document
guards, teardown failures, clipboard bounds, and repeated initialization.

The optional update-status contract has 28 focused checks. They verify the
40-byte version-1 request, immediate dirty updates, real idle skipping, queued
bindings before scheduling, due timers, reinitialization, and conservative
fallback for unavailable exports and malformed packets. Positive infinity is a
valid idle deadline; NaN, negative delays, stale generations, and unknown flags
disable scheduling for that runtime.

`--native` additionally exercises the actual managed P/Invoke boundary and
native DOM. A bridge supporting update status runs 51 scheduling assertions:
retained idle frames, immediate text/bool/field/pointer/focus/composition/layout
invalidation, caret deadline decrement and expiry, actual CSS animation
completion, modal resource retirement, and generation replacement. Older
bridges explicitly skip this optional section and keep eager updates. Assets
must include the authored `prime_home.rml` and its fonts; the core-check tool's
output `rmlui` directory supplies a suitable fixture.

These contracts do not replace the real GPU/device-lifetime fixtures or the
alternating full-game performance benchmark.

The optional 80-byte edit/88-byte state contract adds 29 focused Host/Cocoa
checks in `RangeImeAcceptance`: UTF16 replacement/selection forwarding, true
zero-width caret geometry, older bridge fallback, malformed packet rejection,
exact revision/focus ownership, synchronous discard-to-Unmark reentry and FIRST
stale Unmark before a refresh. These use synthetic bridge/Cocoa boundaries. The
separate `tools/rmlui-text-edit-check` fixtures exercise the actual native owner
and actual macOS programmatic callbacks without the main game graph.
