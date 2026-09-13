# Launcher — overview

This file summarises the launcher features and where its code lives.

Basics

`MphRead -launcher` opens a front screen, not a settings dialog: a map picture
on the left, the things you can do on the right. Everything that is not a
per-session choice lives in the settings window, which is one of the entries and
is also what the pause menu opens mid-match. The front screen starts multiplayer
sessions and recorded replays; it has no Adventure/save-slot or offline/bot entry.

| Entry | What it does |
|---|---|
| Host | choose a multiplayer room, match type and hunter; the GUI asks the directory to run the match. **See every map** opens the picture grid |
| Join | browse listed servers or enter `host` or `host:port`, with a live line saying what the server is running |
| Replays | choose or import a `.fpreplay` recording and replay it |
| Settings | display, audio, controls, match rules, profile, credits, and game files |
| Game files | where the .nds goes. Shown first, before the other launcher entries become available, when there is nothing set up yet |
| Quit | close the launcher |

The text launcher keeps the same multiplayer flow. Hosting still goes through
the persistent Node and its managed Worker; the client does not start a private
Worker. The old
local-gameplay path no longer accepts the CLI switches `-mode` and `-players`; `-room` and `-model`
still open the multiplayer room/model asset viewer.

Key implementation notes

- **One launcher presentation, in Avalonia, on every platform.** Shared views,
  XAML, themes, and controllers live in `src/Client.Presentation/Launcher/`.
  Desktop windows/event integration live in `src/Client/Launcher/`; Android
  supplies its activity host.
- Every control is painted by this code (`GuiTheme`, `MenuEntry`, `ChoiceRow`,
  `SliderRow`, `KeyRow`, `SplashView`); only the text boxes and scroll bars are
  stock, under Fluent dark.
- The picture is a map preview out of `thumbnails/`, rendered from the user's own
  files -- no art is shipped. A `splash.png` beside the exe replaces the home
  picture.
- Choices live in `launcher.txt` beside the exe (`LauncherPrefs`) and keys in
  `controls.txt` (`InputSettings`).
- Two desktop front ends coexist: the shared Avalonia presentation and
  `src/Client/Launcher/Portable/TextLauncher.cs`. Portable preferences and
  session coordination live in Client.Core; shared views and plans live in
  Client.Presentation.
- `-launcher -text` forces the text launcher; the code falls back to text when
  there is no display.

Windows, and the loop

- A bare invocation opens the launcher on Windows and macOS -- the platforms
  where a program is normally started by double-clicking it. On Linux it opens
  the text launcher; `-launcher` asks for the window.
- The toolkit is set up **once per process, on the game's own thread**, and each
  visit to the launcher is a nested dispatcher loop
  (`GuiLauncher.EnsureSetup`/`Ask`). One launcher, then a match, then the
  launcher again; "Quit" and closing the window are what end the program.

First-run behaviour and progress

- First run shows only the game-files card until extraction completes.
- The progress bar is milestone-driven: `SetupProgress` classifies output into
  phases rather than counting files first.

macOS and Android

- **macOS** publishes as `osx-x64`/`osx-arm64`. On 2026-09-12, the content-free
  SDL GPU runtime smoke passed locally on Apple Silicon/Metal. That does not
  verify the actual launcher, content-backed match, input, or audio; those
  remain in `.claude/KNOWN-GAPS.md`.
- **Android** is `src/Android/Android.csproj`. It references Client.Core and an
  explicit Android evaluation of Client.Presentation, while owning lifecycle,
  EGL, touch/gamepad, storage, and update adapters. An API 30 x86_64/SwiftShader
  emulator has loaded an offline match; physical ARM64 behavior and appearance
  remain unverified. Full account: `.claude/android/ANDROID-PORT.md`.
- The explicit Android Presentation evaluation remains a compile check on
  shared code, while project guards prevent platform APIs from entering Core.
  The separation already forced out
  `LauncherPrefs.Directory` (an Android package's directory is read-only, so
  the head points `launcher.txt` at the app's data directory), the matching
  `GameFiles.Root` for `paths.txt`, and the `ANDROID` guard in `GuiLauncher`
  (Android stands the toolkit up from its activity, with no desktop backend
  to detect).
- Building Android needs the workload, a JDK 21 and an SDK with
  `platforms;android-36`:
  ```bash
  export JAVA_HOME=$HOME/jdk21
  dotnet workload install android
  dotnet build src/Android/Android.csproj -c Debug \
    -p:AndroidSdkDirectory=$HOME/android-sdk
  ```
  Android opts Client.Presentation into `net10.0-android36.0`; ordinary and
  desktop Presentation evaluation remains `net10.0` only.

## ⚠️ "Random" is a menu entry, not a hunter

`Hunter.Random` is offered by every front screen and has no entry in
`Metadata.HunterModels`, which has one for each of the seven and for the
Guardian. Nothing rolled it into a real hunter, so picking Random and starting
anything threw `KeyNotFoundException` the moment `PlayerEntity.Create` asked
for a model -- *"The given key 'Random' was not present in the dictionary"* on
the desktop, and `Arg_KeyNotFoundWithKey` on Android, which is the same
exception with the message strings trimmed out. Every platform, every match
kind, since Random was added.

`Hunters.Resolve` (`src/Client.Presentation/Launcher/Portable/LaunchPlan.cs`) rolls it, and
`LaunchPlan.Hunter` resolves on the way in, so no consumer of a plan can see
Random. Two things about it are not obvious:

- **The roll is held for one launch.** Joining a server announces the hunter
  (`NetLaunch.Join`, which resolves too) *before* the plan carrying it is
  built, so two independent rolls would put a player on the roster as one
  hunter and draw them as another -- on their own screen and everyone else's.
- **It is rerolled where a launch begins**, not where the match ends:
  `GuiLauncher` and `TextLauncher` before they ask, and `HomeView.Reset`,
  which is Android's equivalent (one HomeView lives for the life of the app).
  Without that, "Random" picks one hunter and gives it to you for the rest of
  the session.

The preference keeps the word: `LauncherPrefs.LastHunter` still stores Random,
so the picker still shows it and the next match rolls again.

See also: .claude/launcher/LAUNCHER-DESIGN.md, .claude/launcher/LAUNCHER-SETTINGS.md, .claude/launcher/LAUNCHER-FIRSTRUN.md
