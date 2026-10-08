# RmlUi runtime ownership boundaries

Status: implemented foundation with limited local validation; production parity
and release acceptance remain open.

The migration preserves the game's existing service and renderer authority. RmlUi
owns document layout, native input dispatch and UI draw data. It does not own a
second network session, gameplay scene, replay simulation, graphics device or
swapchain.

The source baseline is `6a24a39d9a37014f9ae4618da4af6361c1872203` (PR #387).
The supplied plan audited `f8308a92460cb7962762b6bf15c01341e8e3b845` (PR #388).
These are different baselines. The October 7 remote audit and frozen source
inventory are linked from [migration status](rmlui-migration-status.md).

## Authority and presentation

| Boundary | Owner | Presentation contract |
| --- | --- | --- |
| Network identity, roster, rules, ready, ownership, phase and match start | `NetSession`, `LobbyRules`, `MatchStart` | A presentation-neutral lobby controller reads authoritative state and submits existing commands. A rule draft is not acknowledged state. |
| Discovery, connection and local hosting | `ServerBrowserService`, existing online launch services, `LocalServer` | Presentation starts/cancels work and displays service results; it does not invent lobbies or protocol commands. |
| Local preferences and input | `MenuSettings`, `LauncherPrefs`, `InputSettings`, `GamepadRuntimeConfig`, `SettingsPersistence` | Settings draft validation/apply/discard must preserve existing persistence semantics and last-known-good video recovery. |
| Content and game data | `GameFiles`, existing Community API/map services, map validation/cache/prewarm | UI displays progress and errors; existing services enforce format, ownership, identity and size constraints. |
| Profile and account | `HunterLicenseClient` and existing authentication services | Credential values are transient protected input; tokens/passwords never enter layout or action diagnostics. |
| Replay and offline/Adventure | `DemoPlayback`, replay document/session services, `OfflineLaunch`, `AdventureSave`, `AdventureLaunch` | UI requests actions against existing worlds/documents; navigation cancellation does not create a second simulation. |
| Studio | `StudioApplicationLauncher`, broker/IPC, standalone document/workspace owners | The client presents a launch/deep-link/error surface. Full Studio removal is a separate conditional track. |
| Navigation and modal policy | Presentation-neutral router/application controller | Typed routes, history, back precedence, pending work lifetimes and focus restoration; no toolkit control types in snapshots. |
| Native layout and document resources | One engine-owned RmlUi host | Document/model lifetime, snapshot diffs, native focus/input, action queue and resource release. |
| World, Hunter previews, HUD and final composition | Existing engine/frame renderer | Chamber/Hunter/HUD remain engine draws; RmlUi overlays them before present. |

The baseline `PrimeRouter` exposes `News`, `Play`, `HunterLicense`, `Theatre`,
`Forge`, `Offline`, `Settings` and `Lobby`. Future route naming may add `Home`,
`Hunters`, `Community`, `Adventure` and `StudioLaunch`, but this does not remove
any operation covered by those legacy workspaces. A route alias must preserve its
guards, dirty-settings confirmation, session restrictions and back behavior.

## Lobby clock and game handoff

In the pinned baseline, `LobbySessionCoordinator` is compiled under
`MPHREAD_AVALONIA`, requires `LobbyScreen` and attaches its clock to
`StartScreen`'s visual tree. The current foundation replaces its authority with
Core `LobbySessionController` and `NetLobbySessionBackend`. The coordinator is a
legacy clock/presenter adapter for that same controller; native Shell owns its
own engine tick without constructing those screens.

The shared controller must own lobby control-plane pumping on the engine owner
thread, independently of a view's attach/detach events. One engine tick can cause
at most one lobby `NetSession.Pump()`. Once local gameplay has taken ownership,
the lobby controller yields pumping; `NetSession.InMatch` alone is insufficient
because a late join still needs the local launch barrier. Start/match handoff
must be emitted at most once for the applicable session generation.

Both temporary Avalonia presentation and RmlUi presentation call the same
controller. `Shell.RmlUi.cs` directly binds native lobby snapshots and match
handoffs. Its explicit legacy rollback transfers the same controller and clock
ownership. Native queued starts are checked against lobby lifetime and match
identity while prewarming; gameplay receives the clock only after the local
scene is created. Source extraction and fake backend tests do not prove real
server/scene behavior: custom-map preparation, spectator, rollback and match
return still require integration evidence before RML-04 acceptance.

## Managed/native ABI

RmlUi stays pinned to 6.3 (`ba95ffe8bfb6370efb2cdcca927eaad4710c5413`), with
FreeType 2.13.3 (`42608f77f20749dd6ddc9e0536788eaad70ea4b5`). Upgrading either
dependency is a separately validated change.

The baseline exports `pp_rmlui_initialize`, update/render/resize/shutdown, input,
field/model setters and `pp_rmlui_take_action`. Actions are UTF-8 strings. Keep a
temporary adapter for existing authored string actions while consolidating them
in a typed managed intent registry. Unknown/malformed intents must be rejected;
a syntactically accepted intent must still pass the service's owner/phase/rule
validation.

The current managed `RmlUiHost`/`RmlUiNativeBridge` and native
`projectprime_rmlui_api.h` expose intent protocol version 1. Its 40-byte intent
packet contains size/version, kind/argument, generation, document and sequence.
The registry rejects unsupported versions, malformed packets and invalid
arguments. `pp_rmlui_protocol_version`, generation/document operations and
`pp_rmlui_take_intent` separate native lifetime from the temporary string adapter.
The version-zero compatibility path supports the old OpenGL/single-document
bridge only; it does not provide the new document or draw-list contracts.

Native bytes are decoded under bounds and lifetime checks. Queued actions carry
their document generation; closed-document events are rejected. Lobby intents
also carry the session lifetime and expected revision. The queue is drained by
the engine owner thread. A future incompatible ABI change requires explicit
version/capability negotiation and fresh platform validation.

Snapshot fields are immutable and revisioned per page/session. Only changed
fields dirty the native data model. Text fields keep a local edit draft until
submit/apply; a periodic server refresh must not overwrite an active caret or
unsaved rule input. Workers enqueue completion events and never invoke RmlUi,
OpenGL or WebGPU directly. Back/cancel/disconnect/disposal invalidates their
lifetime tokens.

Document management preserves focus when opening/closing modals and releases
per-document resources. Host snapshot revisions, cancellation and owner-thread
guards are implemented, and debug-only reload is guarded. Real native validation
exercised 100 modal open/close cycles and 100 reinitializations; actual managed
P/Invoke integration exercised DOM intents, Unicode/clipboard, focus restoration,
document cancellation and device loss. Shared page/component/theme decomposition
and full platform/failure acceptance remain open; these tests do not establish
RML-01 parity.

## Input coordinate and focus contract

GLFW events arrive in logical window coordinates. `RmlUiPointerMapping` converts
them to framebuffer coordinates exactly once using each framebuffer/window axis.
Density selects RmlUi layout scale; it must not scale already-converted input a
second time. Full `RenderWindow → Shell → host` tests supplement native pixel
injection tests because only the full path proves this boundary.

The same focus policy applies to pointer, keyboard/text, gamepad and Android
touch. Topmost modal gets back/cancel first, then the route, then exit
confirmation. A closed modal restores focus to its launching control. Focused
text entry owns editing/clipboard/IME keystrokes; gameplay receives input again
when the menu releases ownership. Mouse release outside the window, pointer
capture, scroll, fractional DPI and framebuffer resize need explicit evidence.

Android touch carries pointer IDs and safe-area/soft-keyboard coordinates once.
The menu explicitly owns or releases those IDs independently of gameplay's
touch movement/aim overlay. Desktop pointer correctness does not establish
Android, IME or accessibility acceptance.

## Renderer and platform adapters

Backend-independent RmlUi core owns layout, event dispatch and geometry/texture
identities. Optional OpenGL 2 rendering is a compatibility adapter. A draw-list
adapter records RmlUi 6.3 render semantics without creating a graphics device.
The managed compositor consumes that data using the engine's existing device,
queue, surface and device generation.

The foundation splits `projectprime_rmlui_gl2.cpp` and
`projectprime_rmlui_draw_list.cpp` behind `projectprime_rmlui_renderer.h`.
`RmlUiDrawListReader` copies ABI 1 data under limits; `RmlUiGpuCompositor`
submits through `ModernGraphicsCompat` after scene/HUD and before present.
Transforms, framebuffer scissor, premultiplied textures and set/inverse/intersect
stencil masks are implemented. Unsupported GPU layers, filters and custom
shaders set feature flags and cause an explicit rejected frame. SVG plugin
support is not added. These restrictions remain RML-15 gaps.

| Platform/backend | Adapter responsibility | Required evidence |
| --- | --- | --- |
| Desktop OpenGL fallback | Preserve the compatibility renderer and GL state across engine/UI draws | Native build, direct input/layout, running-game overlay and resource recovery |
| Windows DX12/Vulkan | Submit UI geometry/textures/clips through existing modern graphics | Native Windows build/packaging, physical input, GPU validation, device loss/resize |
| Linux Vulkan | Same core/action path with engine-owned modern renderer | Native Linux render gate, real launcher overlay, GPU validation/recovery |
| macOS designated Metal/MoltenVK/Vulkan path | Same draw list with the engine's selected supported backend | Both architecture compiles, Retina/fullscreen input, actual backend validation/recovery |
| Android Vulkan/GLES fallback | NDK core, surface/lifecycle/input adapter and existing graphics ownership | Packaged ABIs plus physical create/play/pause/return/background/resume/device recovery |

UI composition is terminal after scene/Hunter/native HUD and before present.
The compositor must implement premultiplied alpha/color-space behavior, texture
update/release, compiled/dynamic geometry, transforms, scissor and clip masks
required by shipped CSS. Device-generation IDs invalidate stale GPU handles.
Resize, hidden/minimized windows, backend changes and device loss preserve core
behavior while invalidating backend resources. A draw-list recording test alone
does not prove actual modern GPU rendering.

There is no new UI swapchain, independent WebGPU device or gameplay HUD rewrite.
Dedicated-server targets remain UI-free: no native bridge, font, UI shader,
RmlUi or Avalonia dependencies.

Local compositor checks on an Apple M4 Pro passed 20 assertions on Metal and
20 on Vulkan/MoltenVK, including resize, zero-size suspend/resume, destroyed
device recovery and shutdown/reentry without validation errors. This establishes
those compositor cases on that machine; it does not establish Windows
DX12/Vulkan, Linux Vulkan, Android, forced sRGB surfaces, a complete native
document under gameplay, or measured performance parity.

macOS arm64/x64 GL2 and neutral native compiles, Android arm64-v8a/x86_64 NDK
compiles with 16 KB alignment, and opt-in signed APK payload builds are recorded
in the status evidence. Android's `MphReadRmlUiNativeAssets=true` packages the
bridge/assets only: `AndroidUiSurface`/`AndroidUiOverlay`, touch/IME and Activity
lifecycle remain Avalonia-based. APK validation used `RunAOTCompilation=false`;
production AOT and physical-device UI acceptance remain open.

## Rollout and removal gates

The baseline remains opt-in (`MphReadRmlUiPoc=true`, `-rmluipoc`, OpenGL). The
foundation adds the separate `MphReadRmlUi=true` build feature while retaining
the proof switch and development launch path; this does not switch the shipping
default. Native/bootstrap failures may use
the temporary legacy fallback for interactive runs; automated acceptance must
fail visibly instead of concealing a missing bridge or rendering error.

Do not enable a shipping default on an unvalidated platform. Client Avalonia
removal follows all functional pages, real backend/platform validation, parity
evidence and measured rollout/rollback acceptance (RML-18/RML-19). A fallback
route into Avalonia is not a migrated RmlUi workflow. Product-wide removal also
requires STUDIO-RML-00 through STUDIO-RML-05; standalone Studio currently keeps
its own Avalonia dependencies.
