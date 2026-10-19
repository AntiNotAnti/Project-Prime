# Opt-in Android native Pause and Journal — October 17, 2026

## Activation and bounded route

This implements a native RmlUi in-game menu on the existing Android game surface. It requires both the `MphReadRmlUiAndroidExperiment=true` build property and the initial Activity Intent boolean extra `projectprime.rmlui.pause=true`. The ordinary Android interface remains the default. A usable package also needs `MphReadRmlUiAndroidPackage=true` and its matching native draw-list libraries, shared documents, fonts, licenses and manifests. A compile without that package is a host/API check, not native startup acceptance.

The Activity starts cancellable asset preparation on a worker, using its application's `AssetManager` and private cache. It captures the cancellation token before scheduling and cancels preparation on destruction. Worker completion never publishes into an Activity. If assets are missing, failed or still being prepared when the user asks for the menu, the existing menu opens immediately. The native bridge starts only when an existing match scene and current EGL surface can service an explicit menu request; the separate [configured-local launcher slice](rmlui-android-configured-local-launcher-2026-10-17.md) reuses this foundation for an explicitly opted-in Home route.

`RmlUiAndroidPauseRoute` transfers bounded open/Back/retirement requests and immutable UI snapshots. `RmlUiAndroidPausePresentation` captures the existing scene, in-game controller facts and detached discovered-only campaign journal on the EGL thread. It reuses the shared `pause.rml`, confirmation/map-vote/bot panels and `journal.rml`, with the existing draw-list session and GLES compositor. No new scene, window, EGL context, simulation loop or `NetSession` pump is created.

## Ownership and failure behavior

The UI thread releases gameplay contacts and acquires the existing menu input barrier as soon as a native request is accepted. The ordinary touch overlay hides after a current native-open notification. The live `GameView` stays onscreen and receives copied touch coordinates and navigation/Back keys. Coordinates scale from current View pixels to the last published framebuffer dimensions exactly once; density is used only for native layout. The original pointer-down witness remains attached to that contact. Extra fingers are consumed rather than becoming extra native mouse buttons.

The existing EGL thread processes route requests before simulation input, suppresses gameplay input/late aim while the native route owns it, and draws the native terminal pass from `DrawUi` before the existing swap. Native Pause/Journal have no editable text fields; this change does not add an Android IME implementation for future editable native pages. Android Back/Escape goes through the shared presenter: close Journal or confirmation first, then resume the match.

Every native page/controller lifetime is fenced by the exact scene reference. UI effects are posted through existing Activity callbacks only when the exact `GameView` and immutable request notification remain current. Stop, a new request, scene replacement, and existing Settings/Replay/menu handoffs invalidate old notifications. A handoff racing slow native open cannot republish it. A batched close and fresh open retires the previous session before honoring the new request. Activity destruction prevents delayed callbacks from acting on a replacement match; background notifications are deferred and rechecked on resume.

UI pause, lost window focus and surface loss invalidate copied input immediately. The EGL owner flushes native holds before `ReleaseSurface`; resume publishes a fresh input lifetime only on the current surface. Size/density reflow also cancels old contact and IME ownership before native resize. It conditionally resumes only previously active input whose suspension witness remains unchanged, so a UI suspend during resize cannot be undone. Scene exit and render-loop cleanup retire page, controller, input, compositor and native host before `DestroyContext`, even when an earlier cleanup operation fails. The existing compositor forgets GPU names if the owning context is no longer current. A process-global lease prevents a replacement view from initializing RmlUi while asynchronous teardown of an earlier view still owns it; failed native shutdown quarantines later native initialization until process restart.

Native initialization/render/release failures disable the route for that view, attempt every owned cleanup, then open the existing menu. Confirmed Leave/Quit effects reach existing UI callbacks after native retirement. Settings uses the existing `ShowPauseMenu(..., openSettings: true)` page stack; Replay uses the existing editor. The legacy menu/Replay entry invalidates pending native callbacks before changing the view hierarchy, so a delayed fallback cannot hide an already opened legacy page. Android's inapplicable fullscreen button is hidden and its forged current-document intent is rejected through an optional presenter capability; desktop continues to offer it.

## Simulation behavior and remaining routes

This is a menu and input ownership port. **The current Android and desktop app menus continue the world simulation.** The presenter now truthfully defaults to `LOCAL MENU` / `LOCAL SESSION // GAMEPLAY CONTINUES`; Replay uses `REPLAY MENU` / `TRANSPORT UNCHANGED`, and Journal explicitly warns that gameplay continues. The network pump and replay transport retain their existing owners. Acquiring `GameState.MenuPause` would also change pause-map rendering and audio/HUD ownership, so this route does not introduce a partial gameplay pause lease.

| Route | Current Android owner |
| --- | --- |
| Configured-local Home/Offline/Hunter | Separate opt-in native launcher slice; initial match return uses existing interface |
| Play/lobby and campaign launch | Existing interface |
| In-game menu, confirmations, discovered Journal | Opt-in native RmlUi |
| Existing map vote/bot/spectate/record actions | Shared in-game controller and established engine services |
| Settings | Existing page stack after native retirement |
| Replay editor | Existing editor after native retirement |
| Results and other native routes | Existing Android paths; no new native migration claimed here |

Touch capture, Android key dispatch, back gestures, actual APK/native initialization, GLES state on a driver, background/resume, size-only resize/rotation, accessibility and physical-device acceptance remain runtime gates. The source-only tests do not establish them.

## Verification

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-pause-check/rmlui-android-pause-check.csproj -c Release
```

**38 checks passed** using production route/input/shared-host source with a fake page/native surface. Coverage includes UI-only request submission, unready/failed package fallback, current surface admission, scene replacement discarding stale Leave, exact notification invalidation, held touch release and fresh resume witnesses, focus changes, Journal Back, Settings retirement, wrong owner, initialization/render/release/cleanup failure, stop before Draw, stop during Open, pending fallback versus an independently opened menu, close-plus-open batching, handoff during Open, rapid open/Back before UI sees Open, and process-global bridge lease/quarantine. The [output](../evidence/rmlui-android-foundation-2026-10-17/pause-source-fixture.txt) is preserved. Independent read-only review reran a copied fixture and found no remaining concrete route/request/Stop/handoff blocker.

The existing input fixture passes 54 checks and extraction fixture passes 42; their [foundation evidence](rmlui-android-input-foundation-2026-10-17.md) states the separate scope. The root owner coordinates real Android/client compilation. No Android/native/main build or device test was run by the implementation agent.

The root owner completed the real Android Pause/input checkpoint with `MphReadRmlUiAndroidExperiment=true --no-restore`, package flag omitted: **0 errors, 39 existing SDK/JDK/XML warnings, 1:43.28**. The original log is `/tmp/prime-rml56-android-pause-build.log`; the [durable checkpoint record](../evidence/rmlui-android-foundation-2026-10-17/android-pause-build.txt) records the exact scope and desktop snapshot SHA-256 `2b09acc5738eb4f16c4c544fae129eaf3fcf30f9312fa47c416ec60bdc7d7d23`. This verifies Android host/API/AOT compilation of that frozen Pause/input checkpoint. It does not verify the later configured-local launcher source, a current native bundle/APK, startup, or a physical device.
