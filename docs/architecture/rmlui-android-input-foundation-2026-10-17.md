# Android RmlUi input and surface foundation — October 17, 2026

## Baseline Android owner

`MainApplication` creates Avalonia once and moves settings/game-file paths to app files. `MainActivity` initially displays its Avalonia child; no `GameView` exists until a match plan has settled the window size. `BeginMatch` creates one `GameView` plus the ordinary touch overlay. `GameView.RenderLoop` owns one long-lived GL thread, EGL context, window surface, `Scene`, fixed-step simulation, and presentation/swap loop. It currently builds the match scene immediately after binding a usable surface.

Activity pause releases gameplay contacts and gamepad state and asks the render loop to pause. The GL owner drops only its EGL window surface and retains its context/scene. Resume resets accumulated display/simulation time after the replacement surface is current. Pause menus and Replay Studio presently park that still-attached game surface offscreen and show Avalonia. Results use an offscreen Avalonia buffer composited by the same game surface. Stop/Activity destruction asks the GL thread to clean up its own scene/audio/context; the UI thread does not perform that work.

The existing experimental `RmlUiAndroidDrawListSession` owns native RmlUi, copied draw-list capture and the GLES compositor. It creates no EGL context, Android View, scene, network session or pump. The compositor checks the existing current EGL context, scopes/restores GLES state, and discards stale GPU names after context loss. This foundation previously lacked an input/lifetime boundary.

## Isolated implementation

`RmlUiAndroidInputQueue` is compiled only with `PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT`. It has a fixed ring capacity (256 events by default), a 4,096 UTF-16-unit text cap, and immutable copied input snapshots. No `MotionEvent`, `KeyEvent`, View, Activity, native call or GL object crosses the UI-thread boundary.

The owner publishes current native document, text-focus epoch and surface/input generation. A platform callback retains that witness and enqueues an existing `RmlUiPlatformInputEvent`; an Android `InputConnection` must retain its original focus witness rather than acquiring the new field's witness for a delayed commit. Pointer coordinates are framebuffer pixels exactly once. Device density is only the native layout density, not another pointer multiplier.

The existing session drains input before `RmlUiHost.Update`, then refreshes native document/text ownership before draw-list capture/composition. Owner-only operations reject a different thread. Stale documents—including a still-alive page covered by a new modal—cannot receive queued callbacks. Stale IME epochs are rejected both when queued and by the production host at drain time. Dropped terminal events reserve an owner-thread release when they could strand a held input.

Size or density reflow is also an input lifetime boundary, even when Android does not destroy the surface. The owner atomically suspends and records whether input was active, releases native holds before `Host.Resize`, and resumes only if that exact suspended snapshot is still current. Old coordinates, pointer-up and IME/preedit cannot cross into the new layout. A route already suspended for focus/lifecycle stays suspended; a UI suspend arriving during native resize wins over the conditional resume.

Overflow discards the entire burst, invalidates cached witnesses and reserves a release; it never merely drops pointer-up. UI-thread `Input.Suspend()` immediately invalidates input ownership without native calls. EGL-owner `session.SuspendInput()` drains that release before unbinding the surface. `ResumeInput()` publishes a new surface lifetime. Shutdown retires input, compositor and host even if native input release fails. Route changes release prior holds while preserving the newly authored focus with a fresh native epoch.

## Exact integration seams for the root host

1. Keep activation behind the existing explicit Android experiment/package flags and a separate opt-in runtime choice. Do not switch the Android default based on a compile/package result.
2. Native startup needs a launcher mode in the existing `GameView.RenderLoop`; its current unconditional `BuildScene()` path is not a launcher loop. Reuse that EGL owner and shared RmlUi pages/controllers. Never create a second game scene, EGL loop, window, or `NetSession` pump.
3. Use `RmlUiAndroidApkAssets.Extract` with `RmlUiAndroidPackageAssets` and an app-private cache directory to extract the read-only packaged `rmlui` inventory. Pass the returned root without a trailing separator to `session.Initialize`. APK assets cannot be read through ordinary native filesystem paths directly. The [extraction contract](rmlui-android-apk-assets-2026-10-17.md) defines publication, cancellation and cache ownership.
4. After route changes, call `session.Input.Refresh(session.Host)` on the owner so the UI receives the current document/focus witness. Route Android touch/key/IME input to this queue only while its current snapshot is active and that native route owns foreground input. The current gameplay `TouchOverlayView` otherwise owns every contact.
5. Use `session.Render` as the terminal pass after scene/HUD composition and before the existing `eglSwapBuffers`. Do not composite the Avalonia results buffer over the same native route.
6. Activity/UI pause or route retirement may immediately call `session.Input.Suspend()`. The EGL owner must call `SuspendInput()` before `ReleaseSurface`; use `ResumeInput()` only after the existing surface is usable. Retain the native session/GPU resources across ordinary surface replacement. Run `Shutdown()` while the owning context is current before destroying a native session whose context will otherwise remain alive.
7. Stop/replacement must retire the old session before another render thread initializes the process-global native RmlUi bridge. Late callbacks hold old snapshots and are rejected. Existing asynchronous `GameView.Stop()` is not proof that its thread has already completed native teardown.

The input/extraction foundation alone does not activate a route. The subsequent [opt-in native Pause/Journal integration](rmlui-android-native-pause-2026-10-17.md) and [configured-local Home/Offline/Hunter slice](rmlui-android-configured-local-launcher-2026-10-17.md) wire `MainActivity` and `GameView` through these seams while retaining the existing default interface. Android input-method composition, candidate positioning, accessibility, touch capture and device rotation still require actual platform/runtime testing.

## Verification

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-input-check/rmlui-android-input-check.csproj -c Release
```

The source-only fixture links the production queue, session orchestration and shared RmlUi host/input codec. Native bridge and GLES compositor/reader are instrumented fakes. **54 checks passed**, covering UI-thread enqueue, pixel coordinates, owner drain/order, suspend/resume, stale IME and queued focus replacement, stranded focus-scoped key-up, held-pointer/modal transitions, overflow, payload limits, wrong owner, session teardown and teardown failure. Reflow checks cover held contacts and queued old coordinates, stale pointer-up, preedit plus queued/delayed IME, density-only changes, already suspended input and UI suspension during native resize.

The fixture output is preserved in [input-source-fixture.txt](../evidence/rmlui-android-foundation-2026-10-17/input-source-fixture.txt).

This is not an Android compile, APK verification, native startup, actual input-method/touch run, GLES render, screenshot, surface-resume or physical-device acceptance result.

Separately, the root owner compiled the foundation against the real Android host/API with `MphReadRmlUiAndroidExperiment=true` and the package flag omitted: **0 errors, 39 existing SDK/JDK/XML/nullability warnings**. That earlier compile includes the production queue, session and Android `AssetManager` adapter; it precedes the native Pause route wiring and does not prove a packaged native bundle or device run. Its summary is recorded in [android-foundation-build.txt](../evidence/rmlui-android-foundation-2026-10-17/android-foundation-build.txt).
