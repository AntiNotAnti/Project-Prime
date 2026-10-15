# V19 PR-02A: stacked LCD geometry and pointer mapping

This is the **first half** of the 4:3/5:3 stacked-screen work. It adds `DsLayout`, a pure mapping for equal vertically stacked LCD viewports, uniform letterbox fitting, normalized lower-screen pointer geometry, customizable radar placement/size and the 99 HP blink threshold.

* At 4:3, 768x1152 has two 768x576 LCDs.
* At 5:3, 960x1152 has two 960x576 LCDs.
* The top screen does not acquire input. Round weapon/alt-button hit targets compensate for stretched horizontal art, preserving their physical shape and native centers.
* No rendering changes, no assets, no UI toggle is exposed yet. `DsLayout.Enabled` defaults **false** until PR-02B adds the rendering/composite path.
* Existing user-positioned standalone stylus-zone preferences stay intact; the lower LCD owns the input rect only when stacked mode is explicitly active on supported desktop stylus.
* Test coverage is in the existing `PointerCheck`, with strict geometry, touch, 5:3 round button, radar and threshold assertions.

**Deferred and not claimed complete:** two-screen OpenGL composition, the bottom LCD HUD/radar artwork, weapon/health and native material presentation, pen hardware integration, display/safe-area controls, Android/touch parity, profile settings UI and screenshots. This should not be user-enabled before PR-02B.

No network, replay, combat, or map editor paths changed. The repository retains Avalonia and GL/GLES-only runtime.
