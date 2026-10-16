# RmlUi RML-02: OpenGL compatibility-frame smoke

Source: current main plus the merged RML-01 host; historical native GL2 bridge
remains pinned to RmlUi 6.3. **This is a developer diagnostic, not a released
launcher migration.**

## Integration

- Both shell and in-match paths reach RmlUi after the existing Avalonia
  picture/Hunter compositor and before the window's one and only buffer swap.
- Native RmlUi is hosted on the existing OpenTK owner thread and context.
  No independent window, graphics device, render graph, or WebGPU is created.
- The GL2 adapter saves/restores the compatibility context's fixed-function
  attributes, vertex client state and projection/modelview/texture matrices.
  The managed pass preserves the GLSL program, active texture and read/draw
  framebuffer binding, even on exceptions.
- Color and depth are never cleared by the RmlUi pass. The native GL2
  renderer clears stencil to support its clipping masks at terminal
  composition. This remains an acceptance item for full renderer parity.
- The smoke only forwards pointer, wheel, keyboard and Unicode events while
  active. Only the Quit intent is applied. No lobby or Community actions are
  permitted to change the current authoritative game services.
- A missing or incompatible native library/document disables only the opt-in
  overlay with diagnostics; the Avalonia client remains active.

## Developer build

Use the exact native bridge source in native/rmlui-poc (requires RmlUi 6.3
and FreeType). Place the resulting platform library in the executable
directory using .NET's normal P/Invoke discovery rules:
ProjectPrime.RmlUi.Native.dll (Windows), libProjectPrime.RmlUi.Native.so
(Linux), or libProjectPrime.RmlUi.Native.dylib (macOS).

\`\`\`sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadRmlUiGlSmoke=true
PROJECT_PRIME_RMLUI_GL_SMOKE=1 dotnet run --no-build --project src/MphRead/MphRead.csproj -c Release
\`\`\`

The build switch copies the historical prime_home RML/RCSS and a minimal
set of already-licensed font resources under rmlui/. The additional
environment switch is required for runtime activation. Without **both**
switches the source does not alter shipping behavior.

## Validation

- Dotnet build default game and headless server.
- Dotnet build with -p:MphReadRmlUiGlSmoke=true (CI compile gate).
- RML-01 managed fake bridge/lifetime contracts.
- Current OpenGL-only and other build checks.
- Pending local rendered smoke: native bridge compiled for matching ABI,
  real mouse/keyboard/DPI and resize, clip masks on real framebuffer,
  post-frame GL state and long-run texture/resource lifetime.

Next: RML-03 authoritative, presentation-neutral service adapters and
an opt-in true native launcher. Keep the existing game defaults and
Studio runtime until real functional and device parity is demonstrated.
