# Native theme, localization and accessibility contract

Status: implemented shared assets and platform source with named local checks.
Automated platform provider, body localization and cutover checks remain open.
The user owns external physical live testing; unperformed screen-reader/device
runs remain coverage limits rather than completion blockers. Counts refer to mutable October 7 source; review-head
validation is recorded separately in [migration status](rmlui-migration-status.md).

## Authored layout and appearance

The shared app shell imports `Components/controls.rcss`, `Components/shell.rcss`
and `Themes/prime.rcss`. Standalone pause/results/Hunter/Admin documents import
controls explicitly. These styles define block headings/paragraphs and the
native generated scrollbars. Vertical width and horizontal height are 16dp,
track/thumb dimensions are explicit, optional arrows are zero-sized and the
corner has explicit dimensions. This matches RmlUi's documented responsibility
for built-in/generated element styling. [RmlUi core element style guide](https://mikke89.github.io/RmlUiDoc/pages/style_guide.html)

Real native diagnosis found that an unsized generated scrollbar consumed its
parent's width, reduced child content width to zero and intercepted the pointer.
The shared fix restores usable content and actual wheel/pointer routing. Layout
checks validate positive control rectangles and reachable scrolling, rather
than treating document visibility as evidence of usable content.

Density uses logical dp for text and hit targets. Compact Home keeps its activity
viewport between header/footer and scrolls to its launch button. Compact License
scrolls the whole route so tabs cannot move its account fields below an
unreachable inner viewport. License body/fields use 14–16dp text and controls use
at least 44dp; the shared touch policy increases controls to 48dp. Existing
Offline/Community responsive panel widths also remain in their scoped styles.

`RmlUiVisualPolicy` applies document classes from existing launcher preferences:
Reduce Motion, High Contrast, Large Text and Touch Targets. These preferences
load/save through `LauncherPrefs` and have explicit Settings schema rows. Android
also selects touch targets automatically. Reduce Motion removes authored
animations/transitions; high contrast uses opaque black/white surfaces and
visible focus; large text raises body/control sizes. The engine chamber and
Hunter previews remain engine draws.

The portable authored theme uses ordinary geometry, surfaces, borders and text.
The actual native render test reports no layer/filter/custom-shader feature
flags. The compositor still rejects those unsupported arbitrary RmlUi features
explicitly; equivalent authored appearance is not an implementation of every
possible filter/layer/shader.

## Localization and packaged fonts

`RmlUiChromeLocalization` binds ten known header/footer/status labels for the six
existing language indices: English, Japanese, French, Spanish, German and Italian.
It updates only authored chrome IDs that exist in the document. It does not
replace user drafts, account identity, service metadata or editable values.
Per-route body strings are not yet fully translated and remain an explicit gate.

Rajdhani and JetBrains Mono retain their existing typography. A static regular
Noto Sans JP fallback is bundled with its SIL Open Font License, source URL,
upstream variable-font hash, conversion recipe and output hash. Native creation
loads the optional fallback before the context, and corrupt supplied fallback
payloads fail instead of being silently ignored. RmlUi supports registered
fallback faces for missing glyphs. [RmlUi font loading](https://mikke89.github.io/RmlUiDoc/pages/cpp_manual/fonts.html)

The 33 distinct Japanese chrome codepoints were verified in the bundled font's
cmap. This is not complete Unicode/emoji coverage. Release manifests must include
all three fonts and their license/source attributions; font assets are independent
of authenticated or network-supplied content.

## Owner-thread semantic projection

`projectprime_rmlui_accessibility.h` projects the current live document into a
bounded JSON packet: version, generation, document, semantic revision and up to
4,096 immutable nodes within 1MiB. Nodes expose opaque DOM keys, authored IDs,
roles/names, enabled/focused/protected/offscreen flags, framebuffer bounds and
advertised Focus/Press/Scroll/SetText actions. Nested clipping uses the actual
RmlUi clipping region. Hidden/aria-hidden nodes are excluded.

Editable input/textarea values are never read into the semantic packet. Passwords
have protected metadata; ordinary editable fields also omit values. Labels come
from authored semantic labels, associated form labels, placeholders or visible
non-editable text. Private typed action identities participate in the revision,
so a recycled row cannot reinterpret a stale provider activation.

`RmlUiAccessibilityService` captures only on the host owner and publishes a
read-only snapshot. OS callbacks read that snapshot and enqueue immutable
commands. The queue is bounded to 256. Both managed enqueue and native execution
validate document generation, current foreground document/modal, semantic
revision, live opaque key, enabled state and advertised action. SetText also
validates UTF-8/128KiB and authored maxlength; readonly fields reject changes.
Native Press dispatches the same real DOM event and typed intent as ordinary
input, so service authorization remains in the existing controller/backend.

The managed service reuses its byte buffer and checks the fixed packet header.
Unchanged semantics/framebuffer dimensions return the same immutable graph,
avoiding deserialization and per-frame node allocations. Android trimming
explicitly preserves the private packet DTOs. Command `ToString()` includes only
action/document/revision metadata and never its text payload. Retire clears the
published graph and queued commands.

An optional native update-state export enables reuse before reading the semantic
tree: generation, foreground document, framebuffer and visual revision must
match the last successful read, dirty must be false, and the native next-update
delay must remain positive. Accepted commands and retirement invalidate the
stamp. Older bridges retain the full native capture path. Actual native mutation/deadline checks pass; only matching full-host reruns can
establish production timing improvement.

## Actual platform providers

Cocoa uses actual `NSAccessibilityElement` instances attached to the existing
GLFW content view. It caches descriptors by native element handle, maps roles and
secure-field metadata, converts framebuffer rectangles into view coordinates,
and queues focus/press/scroll/SetValue to the shared owner service. Editable
`accessibilityValue` queries return no value. Prior view children/role/label are
restored on disposal, and stale OS handles cannot target a future document.
The service coexists with the per-view Cocoa IME adapter. [Apple NSAccessibilityElement](https://developer.apple.com/documentation/appkit/nsaccessibilityelement-swift.class)

Android's `AccessibilityNodeProvider` exposes real virtual descendants of the
native SurfaceView. It reports framework class/role/name, protected/editable
state, view/screen bounds, keyboard/accessibility focus and advertised actions.
Touch exploration chooses visible semantic targets from the immutable graph.
Commands queue to the renderer owner; UI callbacks never invoke the native host.
Content events require an enabled accessibility service and safely tolerate it
switching off during dispatch. [Android AccessibilityNodeProvider](https://developer.android.com/reference/android/view/accessibility/AccessibilityNodeProvider)

Windows UIA exposes a real COM HWND provider; Linux AT-SPI exposes real GDBus
Accessible/Application/Component/Action/EditableText/Cache and asynchronous
registry embedding. Their 150 real native/provider contracts and actual GLib
protocol XML/Cache wire checks passed locally. External Windows UIAutomationClient
and Linux pyatspi fixture modes await hosted CI. Editable readback remains absent;
UIA percentage scrolling needs actual scroll metrics, and Wayland global screen
coordinates remain unavailable. Keyboard and
controller focus continue to use the native host. External physical screen-reader
coverage is user-owned under the current acceptance scope.

## Named checks and remaining acceptance

| Check | Named local result | Boundary |
| --- | --- | --- |
| Shared authored theme | 610 real native assertions at seven viewports/aspects | Six chrome languages, header/footer containment, real Social/Hunter pointer actions, compact wheel reachability, 48dp policy,actual normal/high-contrast selected-route draw triangles and unsupported-feature absence |
| Native semantic service | 150 real native assertions | Protected/no-editable-value metadata, clipping/focus, Unicode SetText, real typed press, disabled/readonly, action rebinding, modal/document/generation retirement and unchanged graph reuse |
| Cocoa provider | 218 actual AppKit/GLFW/native assertions | Names/roles/frames/protection, owner-queued actions, stale handles and 100 attach/detach cycles |
| Compact/Retina License | 60 actual native assertions | Positive independent field layout, real wheel reachability, pointer input and Back retirement; zero production account commands |
| Android InputConnection | 13 actual emulator assertions | Unicode composition, surrogate deletion, protected and stale fields |
| Android provider | 16 actual emulator assertions | Real virtual nodes, screen bounds, protected metadata, focus, Unicode SetText, typed Back and retirement; redundant native focus suppressed and owner queue synchronized |

Run `tools/rmlui-accessibility-check` and `tools/rmlui-cocoa-accessibility-check`
against a fresh bridge. Android validation builds use
`MphReadRmlUiAndroid=true; MphReadRmlUiAndroidCheck=true`; Activity extras
`rmlui-ime-check` and `rmlui-a11y-check` execute framework/native fixtures without
submitting account forms. Tool READMEs and [platform packaging instructions](../../tools/rmlui/README.md)
record commands and platform prerequisites.

The semantic capture test records 200 stable samples at three viewports with
zero native semantic reads and managed bytes per capture, with local p95 around
0.0002ms. Model, pointer, resize, focus/action and actual caret deadline changes
prevent idle reuse. Older bridges retain the tested eager path. These are local
component diagnostics, not production UI timing, native allocation measurements
or a same-machine Avalonia comparison.

The [initial production comparison](rmlui-evidence/performance-metal-initial/comparison.json)
measured native UI/CPU regressions with production accessibility enabled. Those
results remain visible while native update scheduling and idle capture are
optimized; only a fresh matching full-host comparison can accept that gate.

Full body translation, automated font/glyph/provider cases, required goldens and
production performance remain automated gates. Physical VoiceOver/TalkBack,
vendor IMEs and controller/touch/stylus ergonomics remain user-owned live coverage. No screenshot, node query or bounded harness count
is treated as certification of those broader gates.
