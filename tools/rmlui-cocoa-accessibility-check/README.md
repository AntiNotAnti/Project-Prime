# Actual Cocoa accessibility graph check

```sh
dotnet run --project tools/rmlui-cocoa-accessibility-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
```

Requires macOS. Creates a hidden actual GLFW content view and real native RmlUi
host, attaches NSAccessibilityElement descendants and queries AppKit role/name,
parent, screen-coordinate frames and secure metadata. Invokes actual AppKit
focus, Unicode SetValue and press callbacks, verifies the resulting native field
and typed business intent, rejects retired elements, enforces main-thread graph
publication and exercises 100 real attach/detach cycles.

The provider uses immutable semantic snapshots and queues owner-thread commands.
It restores the content view's original accessibility graph on disposal and
never exposes editable field values. Physical VoiceOver and alternate input
source acceptance remains a separate requirement.
