# RML-14: Native Studio entry routing

The native RmlUi Studio button now opens an authored, minimal Studio
selection page instead of launching an editor immediately.

- **Open Map Studio** invokes the existing, guarded
  `Shell.RmlUiNavigateStudio` / StartScreen workflow.
- **Replay Library** uses the already-supported Theatre route, preserving
  existing replay files, playback, and editor entry.
- **Back** closes the native document and restores the current Home page.
- Failed launcher admission is displayed in the native page without claiming
  Studio opened.
- The route is document, generation and owner-thread validated.
- The new page does not duplicate map conversion, build, publish, editor
  process management, or the current Studio project/replay persistence.
- No file-path argument is passed from untrusted native text to a process.
  The existing Studio workflow remains the source of authority for file
  picking, recovery, and subsequent project launch.

Only opt-in desktop builds include this native Studio entry. Avalonia is still
the default. Real map/replay editing stays in Project Prime Studio, not RmlUi.

Run `dotnet run --project tools/rmlui-studio-check -c Release` for independent
device-free launch, rejection, close and thread-ownership acceptance. CI also
builds the opt-in launcher and runs native GL2 software-frame checks.

Remaining: richer Studio project picker, Replay Studio deep links, runtime
parity on real Windows/macOS/Linux devices, Android Studio fallback, and
return-from-Studio lifecycle acceptance.
