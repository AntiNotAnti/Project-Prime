# Linux input method acceptance

The system IBus client connects through D-Bus on its private GLib worker context.
GLFW retains the X11/Wayland event queue and its existing input context. The
adapter advertises inline preedit and focus, while the desktop panel owns candidate
lists. It never supplies surrounding field contents. Password fields carry only
the protected flag and request password purpose plus the private input hint.

Run native document/lifetime tests on any platform with the matching bridge:

```sh
dotnet run --project tools/rmlui-linux-ime-check -- --native /absolute/path/to/native-bridge
```

Run the real Linux D-Bus transport fixture in a disposable desktop session:

```sh
sudo apt-get install ibus libibus-1.0-5 gir1.2-ibus-1.0 python3-gi dbus-x11 xvfb
dbus-run-session -- xvfb-run -a python3 tools/rmlui-linux-ime-check/run-ibus-check.py --native /absolute/path/to/native-bridge
```

The deterministic engine checks supplementary Unicode preedit replacement,
single commit, native cancellation, focus transfer, direct Unicode commit, and
declined-key fallback. The tool also checks stale bus events, hidden/retired
documents, a failed request, password metadata, owner thread, and 100 contexts.
No input strings are written to diagnostic logs.

Missing libraries or an absent/disconnected bus leave GLFW committed Unicode
input available. Engine key calls have an 80 ms IPC deadline and a 120 ms owner
deadline. Candidate positioning uses the actual field rectangle because the
pinned RmlUi public API does not expose glyph caret bounds. X11 uses the active
XKB group/modifier state; Wayland uses GLFW's available key names and relative
candidate positioning. Physical engines and Wayland layout-specific shifted
punctuation still need platform acceptance beyond this deterministic fixture.
