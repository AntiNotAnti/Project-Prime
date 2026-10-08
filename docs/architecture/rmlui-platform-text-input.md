# Native text input ownership

All native text input is applied on the engine owner thread. Platform events
carry the runtime generation, document token and text-focus epoch. Preedit,
commit, cancellation, and input-method deletion/selection keys are rejected
after focus changes, page retirement, a modal change, hiding, or runtime reload.
Physical keyboard keys use epoch zero and follow current UI navigation.

The additive text-state ABI is a versioned 64-byte packet. Selection offsets
inside RmlUi and IBus are Unicode scalars. Windows and Cocoa offsets are UTF-16;
the adapters convert them explicitly. The optional UTF-16 selection export
returns indices only, guarded by generation/document/epoch. Capability bit 4
identifies a protected input without returning its contents. Input traces and
event `ToString` methods contain numeric ownership and authored IDs, never text.

Windows uses the existing GLFW HWND subclass and IMM composition messages.
The hook checks the owner/window thread, retains reverse callbacks until native
uninstall or window destruction, handles failed removal, and suppresses promoted
character messages after an owned commit or cancellation. Cocoa installs a
subclass on the existing GLFW content view, preserves the original methods and
class, and consumes owned `NSTextInputClient` marked/insert callbacks once.
Ordinary Cocoa committed characters continue through GLFW. The AppKit
accessibility graph reads immutable native semantics and queues stamped actions;
it coexists with the input subclass and retires before view destruction.

Linux uses optional system libibus, GLib, GObject and GIO. IBus discovers its
D-Bus connection; a private worker GLib context owns every proxy and callback.
GLFW continues to own the display event queue and its input context. The adapter
does not create an XIC or read X events. X11 key symbols use the current XKB
group and modifiers; Wayland uses available GLFW key names. Only IBus-accepted
keys suppress subsequent GLFW character callbacks. Declined keys, absent
libraries, disconnection and timed-out requests retain committed Unicode input.
Key IPC is bounded to 80 ms with a 120 ms owner deadline. Context creation is
asynchronous and cancellable; latest focus work has a separate priority slot.
Cancellation completion remains rooted and pumped after owner disposal.

IBus advertises preedit/focus/synchronous keys; the desktop panel owns candidate
and auxiliary lists. No surrounding-text capability is advertised and no
surrounding field contents are transmitted. Protected fields request password
purpose and the private input hint. All fields request the private hint.

Candidate placement currently uses the actual field rectangle. Cocoa converts
framebuffer pixels to view points and screen coordinates; Linux uses the X11
window origin or relative positioning. Exact glyph caret geometry is not
available through the pinned RmlUi public API. Physical Windows/Cocoa input
sources, VoiceOver, physical Linux engines and Wayland shifted punctuation need
platform acceptance; the fixtures do not certify those hardware paths.

Checks: `tools/rmlui-runtime-check` covers typed input, stale ownership and
throwing presentation/input/page/GPU/error-sink teardown. The actual Cocoa tool
passes 218 AppKit/GLFW/native assertions, including 100 attach/detach cycles and
IME/accessibility coexistence. The Linux tool passes 219 real native-host checks
with a fake bus. Its isolated real D-Bus fixture is available for Linux CI in
`tools/rmlui-linux-ime-check/run-ibus-check.py`; it has not run on the macOS host.

Windows UI Automation and Linux AT-SPI now share that immutable accessibility
service. HWND `WM_GETOBJECT` exposes actual COM fragment/root providers; a
private GLib worker exports standard AT-SPI Accessible/Application/Component/
Action/EditableText/Cache interfaces and registers with the accessibility bus.
Neither OS callbacks nor IPC threads enter the host. They enqueue document,
revision and private-node-key commands for fresh native validation on the owner.
Providers retire before window/view/input hooks; old COM objects and D-Bus
object paths cannot act on a reused document or row. Optional missing Linux
libraries/bus preserve the launcher runtime.

`tools/rmlui-platform-accessibility-check` passes 150 real native semantic/action
contract assertions plus actual GLib XML/Cache wire checks. It includes actual
external UIAutomationClient and pyatspi OS fixture modes for Windows/Linux CI;
those modes have not run on the macOS host. Physical Narrator/NVDA/Orca checks
remain required. Editable value readback is intentionally unavailable: UIA
Value reads return access denied, and AT-SPI omits Text rather than inventing
empty field contents. UIA scroll percentages need real scroll metadata before
they can be exposed; focus-based ScrollIntoView is supported. AT-SPI relative
region scroll actions reuse the native commands. Wayland provides actual
window-relative geometry, with global screen coordinates explicitly unavailable.

Primary contracts: [IBus input context](https://github.com/ibus/ibus/blob/main/src/ibusinputcontext.h),
[IBus modifier/capability/input-purpose definitions](https://github.com/ibus/ibus/blob/main/src/ibustypes.h),
[GLFW X11 event ownership](https://github.com/glfw/glfw/blob/3.4/src/x11_window.c).
