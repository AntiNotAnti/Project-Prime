# UI overlay renderer ownership checks

Run `dotnet run --project tools/ui-overlay-state-check -c Release`.
This package-free harness links the production `UiOverlayRendererState` and
checks renderer/context generation changes, failure reset, shutdown/restart,
complete RGBA input, and overflow-safe extent validation. It requires no graphics
device or game files.

The separate `tools/final-composite-check` native fixture exercises the shared
production compositor: premultiplied alpha, CPU row origin, caller state
restoration, and same-size upload after release. The debug Android renderer
acceptance activity also exercises the actual `AndroidUiOverlay` Vulkan branch,
without creating an EGL context. Those fixtures require the relevant graphics
device and must be run separately; the managed ownership checks establish no
pixel or physical Android acceptance result.
