# Desktop accessibility provider checks

`dotnet run --project tools/rmlui-platform-accessibility-check -- --native <bridge>`
checks the real RmlUi semantic snapshot and guarded actions through the Windows
provider contract with an injectable HWND seam. It covers Unicode writes, actual
native focus and typed button intents, readonly/disabled controls, modal and
revision retirement, screen bounds, worker callbacks, failed HWND uninstall,
and 100 attach/detach lifetimes. The current fixture passes **152 assertions**,
including checks that UIA retains HWND focus ownership; the provider queues the
fragment's internal focus only.

On Linux, or macOS with the optional Homebrew GLib libraries, the same command
also parses the standard AT-SPI introspection through actual GIO and checks the
native GVariant signature of every application/control Cache item. This verifies
wire serialization; it does not prove an OS accessibility bus or screen reader.

On Windows, add `--windows`. This creates an actual GLFW HWND, attaches the
provider through `WM_GETOBJECT`, and starts an external Windows PowerShell
`UIAutomationClient` process. That client discovers actual controls through UIA,
reads name/type/password metadata and screen bounds, focuses an input, sets a
Unicode value, and invokes a real button. The engine checks the resulting native
field and typed intent. The external client waits for the real typed intent to
change an accessible heading before it exits: UIA invocation is asynchronous.
The field is reset first, so this check cannot reuse a preceding fixture's edit.
The HWND callback remains rooted when removal fails and
retires on `WM_NCDESTROY`.

On Linux, install `at-spi2-core`, `python3-pyatspi`, GLib/GIO, `dbus-x11`, and
`xvfb`, then run:

```sh
dbus-run-session -- xvfb-run -a dotnet run --project tools/rmlui-platform-accessibility-check -- --native <Linux-bridge.so> --atspi
```

The actual accessibility bus launcher and AT-SPI registry register the application.
An external `pyatspi` client discovers its controls, verifies role/name/password
metadata, focus, Unicode text setting, screen bounds, and actual activation. No
fixture accessibility tree is substituted for RmlUi. D-Bus registration is async:
`Socket.Embed` can call back into writable `Application.Id` before its reply.

The Windows and Linux OS modes cannot run on the macOS authoring host. The first
hosted run exposed a pyatspi API spelling error and a Windows action completion
failure. The corrected external clients use `get_interfaces()` and wait for an
owner acknowledgment; their hosted rerun remains required. Physical
NVDA/Narrator/Orca acceptance also remains required.
Ordinary and protected edit values are absent from shared semantic metadata;
UIA Value readback is denied, and AT-SPI deliberately omits `Text`. Full text
review/selection patterns therefore remain outside the published capability.
UIA exposes focus-based `ScrollIntoView`; it omits scroll-percentage patterns
because the shared snapshot has no scroll range/position data. AT-SPI region
actions support the existing relative scroll commands. Wayland global screen
coordinates are explicitly unavailable; actual window-relative bounds remain.

Primary interface sources:

- [Microsoft server-side provider contract](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider)
- [Microsoft asynchronous Invoke contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iinvokeprovider-invoke)
- [Microsoft HWND and fragment focus ownership](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementproviderfragment-setfocus)
- [Microsoft HWND provider lifetime](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiareturnrawelementprovider)
- [Windows SDK COM interface definitions](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/UIAutomationCore.idl)
- [GNOME AT-SPI standard D-Bus XML](https://github.com/GNOME/at-spi2-core/tree/main/xml)

`RmlUiLinuxAccessibility.Protocol.cs` adapts interface declarations from GNOME
at-spi2-core XML, licensed LGPL-2.1-or-later. Provider implementation is project
code and does not copy an ATK toolkit or native rendering/event ownership.
