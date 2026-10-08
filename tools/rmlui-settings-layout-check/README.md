# Actual Settings layout regression

This opens the authored Settings document with the real native bridge. It checks
five viewport/density combinations, including 640×320 and 1280×720 at density2.
Every category, Back, search control, first/last field, page control and commit
control is focused, scrolled fully inside its own viewport, and hit with the
real pointer. Categories and Apply must emit their exact typed commands. The
workspace and commit row must remain inside the fixed application chrome/panel.
The fixture does not invoke settings services or write preferences.

```sh
dotnet run --project tools/rmlui-settings-layout-check -c Release \
  -p:MphReadRmlUi=true -p:MphReadAvalonia=false -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/bin/Release/net10.0/rmlui
```

For an already-built isolated full client, add
`-p:PrimeAssemblyDirectory=<full-client-directory>` to avoid rebuilding the game.
The complete asset root must include the current Settings RML/RCSS and shared
templates/fonts. A successful run reports 765 native assertions. This bounded
layout check does not accept persistence, gameplay, or legacy golden parity.
