# RmlUi input latency and ordering

Desktop GLFW callbacks forward input on the existing render owner. Android
callbacks enqueue copied events and its existing EGL owner drains them before
native update/render. Neither path adds an independent input timer.

Native range `change` actions read the current DOM value. The bridge replaces
adjacent pending changes from the same named range/document/action with the
latest intent. Clicks, navigation and other controls fence replacement. Discrete
actions retain FIFO semantics and sequence numbers remain strictly increasing.
This prevents rapid slider input from filling the 256-action queue while the
desktop owner consumes at most 64 intents per frame. This native change requires
rebuilding and packaging the native bridge.

Android replaces adjacent pointer moves only within the same surface/input
generation, document, device, button code, modifiers and focus epoch. Down/up,
keys, text and focus events fence replacement. This keeps the latest sampled
position without overflowing the queue and cancelling an otherwise valid drag.
Surface/focus retirement and overflow release guarantees remain in force.

Focused checks:

- `dotnet run --project tools/rmlui-android-input-check` stresses 8,000 moves
  between down/up, ordering fences, ring wrap and retirement using fake native/GLES.
- `python tools/rmlui-timeline-check/native-source/check.py` compiles the actual
  bridge queue functions against a stub DOM, stresses 8,000 range changes and
  verifies discrete-action ordering and existing timeline source guards.

These checks establish bounded dispatch work and correct ordering, not physical
input-to-photon latency. Reproduce reports on the affected device/platform and
compare frame timing while idle, scrolling and dragging. Linux IBus has a
synchronous key-decision wait (up to 120 ms); a stalled input method remains a
separate platform-specific lead requiring a Linux reproduction. Optional native
update scheduling already treats input mutations as immediately dirty.

## Native menu font preferences

The desktop native owner compares the published `HudRuntimeProfile` identity
and native document lifetime before sending the resolved `ui.body`,
`ui.headings`, and `ui.data` font choices. `UseGlobalFont` and
`UseInterfaceFont` precedence comes from the same runtime resolver used by HUD
text. The optional native font export accepts only indices 0 through 33 or -1
for the authored role. Older native bridges retain their authored fonts.

Licensed `Assets/HudFonts/*.ttf` faces and notices ship under `rmlui/hud-fonts`.
Each selected face is loaded once per native lifetime under an exact alias.
The native plugin tracks original local font properties until element deletion,
restores them when disabled, and handles newly opened documents. Body, heading
and monospace/data roles remain independent; font preferences preserve authored
sizes, colors and layout rules. No profile subscription survives native teardown.
An unchanged selection leaves the retained native frame valid.

October 21, 2026 verification on Windows:

- `dotnet run --project tools/rmlui-runtime-check`: 9 typography cache,
  owner, lifetime and older-bridge fallback assertions, plus the existing suite.
- `dotnet run --project tools/rmlui-android-input-check`: 63 source-linked
  queue/session assertions using fake native/GLES.
- `python tools/rmlui-timeline-check/native-source/check.py`: 20 actual queue
  function assertions compiled with MSVC against a stub DOM.
- `python tools/rmlui-typography-check/check.py artifacts/rmlui-native/win-x64/ProjectPrime.RmlUi.Native.dll --glfw src/MphRead/bin/Debug/net10.0/runtimes/win-x64/native/glfw3.dll`:
  819 assertions: actual RmlUi font loading, independent role geometry, exact restoration,
  idle retention, new-document application and reinitialization. All 34 faces
  load; the GL pass captures real glyphs in an offscreen framebuffer. All 184
  new HUD/previous-choice commands activate through native DOM focus/Enter and
  produce the exact managed kind/argument once, in increasing sequence order.

The font run writes its assertion count and measured geometry to
`artifacts/rmlui-typography-check/report.json` and screenshots to
`artifacts/rmlui-typography-check/baseline/typography_960x600_d1.png` and
`artifacts/rmlui-typography-check/selected/typography_960x600_d1.png`.
These are synthetic native fixtures, not game or physical-device acceptance.
