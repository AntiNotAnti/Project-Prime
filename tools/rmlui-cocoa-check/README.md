# Cocoa input method acceptance

```sh
dotnet run --project tools/rmlui-cocoa-check -- /absolute/path/to/libProjectPrime.RmlUi.Native.dylib
```

Runs against an actual GLFW content view, AppKit text-input selectors, and the
native RmlUi document. It checks UTF-16 platform selection versus Unicode scalar
composition, repeated preedit replacement, one commit, cancellation, stale focus
and document results, actual view destruction, owner thread, and 100 attach/detach
cycles restoring the original class. The fixture stimulates Cocoa callbacks;
physical Japanese/Korean input source selection remains a hardware acceptance
check. Candidate coordinates use actual field bounds, view scale, and screen
conversion; the pinned RmlUi API does not expose exact glyph caret rectangles.
