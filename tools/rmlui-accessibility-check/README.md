# Native semantic and shared-theme checks

Run against a freshly built native bridge and the authored assets:

```sh
dotnet run --project tools/rmlui-accessibility-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
```

The default project copies fresh Home/components/themes and Rajdhani, JetBrains
Mono and Noto Sans JP fonts. An optional second argument supplies an already
assembled asset root. This tool exercises real RmlUi through the DrawList bridge;
it needs no display, game data, authenticated account or network mutation.

The semantic suite covers live names/roles/bounds, protected fields with no
editable values, nested clipping/focus, Unicode SetText, actual typed DOM clicks,
readonly/disabled rejection, private action rebinding, modal/document/generation
retirement and allocation-free managed reuse of unchanged snapshots. It prints
200-sample capture timings as local diagnostics, without certifying production
performance or an Avalonia comparison.
With the optional native update-state export, it also proves zero native reads
during confirmed idle and invalidation on model/pointer/resize changes, commands
and the actual caret deadline. Older bridges exercise eager capture fallback.

The shared-theme suite opens the real authored Home page at seven viewports,
including short landscape, Retina, 4:3, 16:10, ultrawide and narrow portrait. It
checks all six existing chrome languages, real Social/Hunter pointer actions,
header/footer containment, short-stage wheel reachability and 48dp touch targets.
Actual native draw triangles verify the primary route highlight in normal and
high-contrast modes; utility pages clear the selection. Reduced motion suppresses
the authored navigation transition so the highlight changes immediately.
It verifies authored geometry; complete legacy golden acceptance remains open.

Platform providers are separate: `tools/rmlui-cocoa-accessibility-check` uses real
AppKit/GLFW nodes. The Android CHECK build can run the actual framework/provider
fixture with the `rmlui-a11y-check` activity extra. Neither is certification of
physical VoiceOver/TalkBack use, which is user-owned live coverage. Windows UIA
and Linux AT-SPI source/native/GLib contracts are checked separately by
`tools/rmlui-platform-accessibility-check`; external OS modes await hosted CI.
