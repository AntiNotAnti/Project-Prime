# Theatre controller and native document checks

This executable tests the presentation-neutral library through a controllable replay service boundary. The native mode uses the actual RmlUi library, templates, documents, action listener, focus, disabled controls, modal stack, layout, image binding and draw-list renderer. It verifies PNG thumbnail decoding, TGA conversion, native texture pixels, and submitted textured geometry.

```
dotnet run --project tools/theatre-controller-check -c Release

dotnet run --project tools/theatre-controller-check -c Release -p:MphReadRmlUi=true -- --native /absolute/path/to/libProjectPrime.RmlUi.Native.dylib /absolute/path/to/rmlui
```

The asset directory is the packaged runtime layout: `home.rml`, `components/`, `themes/`, `pages/`, and `fonts/`. Native mode runs 1280×720, 1920×1080, 960×540, and 1920×1080 at density 2. It does not require game data or a graphics context.

Pass `-p:PrimeAssemblyDirectory=/absolute/path/to/complete/client/output` to test an already-built client without building the shared game project. The native playback fixture uses the real session and transport to verify that keyboard changes on the actual range control request exactly one seek to the decimal field's frame. Its controlled session is retired afterward; this establishes the UI-to-transport boundary, not replay scene simulation.

The production default backend invokes the existing DemoLibrary, ReplayVirtualClips, ReplayAnnotations, ReplayArchive, ReplayStorageManager, NativeFilePicker and Studio IPC services. Replay controls use DemoPlayback's ReplayController; the viewport helper uses the engine Scene camera. The test boundary supplies copied metadata and controllable worker completions only inside this check executable. These checks do not establish physical input, full campaign/replay gameplay, GPU screenshot quality, or Android picker/export parity. Those require the platform and game route checks.

Integration API:

- `TheatreController.Pump()` applies disk completions on the engine thread; `Snapshot()` returns an immutable, structurally cached revision. `TryTakeLaunch()` hands off once, retaining the pending state until failure, navigation, or `ResumeAfterPlayback()`.
- `TheatreEngineBackend` accepts import/export platform delegates. Desktop defaults use NativeFilePicker and archive export. Android must supply its platform document provider.
- `TheatrePagePresenter(host, pages, controller)` owns documents and thumbnail work, while the router owns the reusable controller. `Present()` pumps the controller. `Back()` dismisses delete confirmation or cancels pending work. Route launch failures must call `ReportLaunchFailure()`.
- `TheatrePlaybackPagePresenter(host, pages, controller)` exposes `Document`, `Viewport` and one-shot `TryTakeEngineCommand()` for Back/Fullscreen. `replay_viewport` bounds define the engine presentation rectangle. The input owner feeds viewport pointer/key events and the renderer polls its camera against the real replay Scene. Playback controls never create a competing replay clock.
