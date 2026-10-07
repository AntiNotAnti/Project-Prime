# HUD editor contracts and native checks

The neutral harness edits detached `HudProfile` drafts through the real shared profile, history, geometry, migration and serialization authority. It checks atomic rejection, undo/gesture history, independent gauge detachment, context flags, inherited crosshair targets, named storage handoff and once-only settings acceptance without publishing configuration.

The optional native harness loads the real RmlUi bridge and authored HUD page. It activates all 14 element controls, verifies native focus and stale-document rejection, canvas keyboard ownership, Unicode JSON fields larger than the default field limit, escaped errors, preview texture upload and draw submission. Heading/body bounds are checked at 1280×720, 1920×1080, 960×540 and 1920×1080 with density 2.

```sh
dotnet run --project tools/hud-controller-check -c Release
dotnet run --project tools/hud-controller-check -c Release -p:MphReadRmlUi=true -- --native /absolute/path/libProjectPrime.RmlUi.Native.dylib /absolute/path/rmlui-assets
```

The native asset root must contain the runtime `pages/hud`, shared `components` and `themes`, launcher home assets and fonts. The native binary must include HUD intents 270–272, bounded rectangle/color/font bindings and textarea field support.

The presenter uses `pages/hud/editor.rml` and exposes `hud_canvas` for bounds-based pointer routing. `TryTakeAccepted` hands one detached complete draft back to Settings; Settings stages it and applies its existing save flow. `TryTakeCancelled` discards the editor draft. Keyboard/gamepad and pointer wrappers belong to the existing engine owner. The bitmap preview uses shared HUD primitives and real cartridge sprites when the configured native root is available; the automated fixture intentionally supplies no cartridge assets. These checks establish real native document behavior and draw-list submission, not GPU presentation or physical mobile input parity.

Verified: 33 controller contracts and 376 native assertions. No Avalonia dependency, second game scene or fabricated gameplay data is used by the editor.
