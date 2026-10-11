# Fruity PR04: fractional DPI, fullscreen, and desktop pacing evidence

Recorded 2026-10-11. Scope: the PR04 tasks in the supplied October 2026
implementation plan. This is an evidence record, not a declaration that the
physical display acceptance matrix has passed.

## Existing policy verification

Command:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/desktop-pacing-check/desktop-pacing-check.csproj
```

Result: exit 0, 39 `DESKTOPPACING PASS` checks, no failures. The executable
links the production `src/MphRead/Mods/Render/DesktopFramePacing.cs` directly.
No production behavior or tests were changed because no policy failure was
reproduced.

Exact symbols exercised by `tools/desktop-pacing-check/Program.cs`:

- `DesktopFramePacing.DisplayRateCap`: zero remains the Display/VSync sentinel.
- `DesktopFramePacing.UseDisplayPacing`: positive numeric caps, including
  60/120/144/165/240 at matching monitor refresh, never select Display/VSync;
  Unlimited (-1) does not select it either.
- `DesktopFramePacing.SoftwareFrequency`: nonblocking numeric caps retain their
  software deadlines; Unlimited selects zero; Display/VSync selects zero;
  ignored VSync fallback selects monitor refresh. Blocking modern presentation
  selects zero to avoid stacking a second pacing clock.
- `DesktopFramePacing.LinuxVSyncIgnored` and `MacVSyncIgnored`: display-only
  fallback detects excessive measured presentation rate, ignores ordinary
  jitter, remains latched in Display mode, and does not carry into numeric or
  Unlimited settings.

These checks evaluate policy only. They do not measure actual frame rates,
deadline jitter, GL swap interval, or driver/compositor behavior. In particular,
the existing blocking-modern-presentation exception is intentional in the
policy and is not evidence of a strict numeric cap under that backend.

## Existing sizing and mode paths inspected

- `WindowMode.EnterBorderless` resolves `GLFW.GetVideoMode` and
  `GLFW.GetMonitorPos`, detaches an attached monitor with
  `GLFW.SetWindowMonitor`, hides decorations, and assigns `window.ClientSize`.
  Windows retains the existing one-pixel-short height. This path does not
  explicitly convert video-mode dimensions using content scale.
- `WindowMode.EnterNativeFullscreen` passes the selected monitor's video mode
  to `NativeWindow.MakeFullscreen` and checks monitor attachment afterward.
- `WindowMode.SaveWindowed` / `RestoreWindow` preserve client size, client and
  outer location, decoration, auto-iconify, and maximized state.
- `WindowMode.HasMonitor` checks GLFW attachment instead of relying on
  minimized `WindowState`. `WindowMode.Sync` reconciles native fullscreen exit
  and focused borderless floating state.
- `RenderWindow.PixelSize` uses `FramebufferSize`; `PointerPixels` converts
  GLFW client coordinates independently on each axis using
  `FramebufferSize / ClientSize`; `OnFramebufferResize` invokes
  `ResizeToFramebuffer` for a ready scene.

The video-mode-to-client-size assignment deserves a physical monitor check on
platforms where screen coordinates differ from framebuffer pixels. Source
inspection alone does not reproduce clipping, oversized windows, or a DPI bug,
so it does not justify replacing those dimensions or dividing by content scale.

## Unverified acceptance work

The standalone policy run does not create a GLFW window. The following remain
unverified by this evidence: 100%, 125%, 150%, 175%, and 200% scaling; mixed-DPI
monitor moves; borderless monitor coverage; letterboxing, HUD and crosshair
placement; pointer alignment; Alt+Tab; screen capture; repeated mode toggles;
sleep/resume; actual swap interval changes; measured 60/120/144/165/240 caps;
Unlimited throughput; presentation jitter on Windows, Linux, and macOS.

Any later GLFW sizing or pacing backport should first record a failing runtime
case with monitor position, video mode, `ClientSize`, `FramebufferSize`, content
scale, mode, and presentation timing before and after the transition. Fruity's
Qt swap-interval change is not evidence of a GLFW/OpenTK defect.
