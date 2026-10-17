# RML-09: Verified opt-in desktop native GL2 package

This slice produces a complete optional native client package with an exact
RmlUi 6.3 GL2 library, pinned revision/source SHA manifest, fonts, complete
authored RML/RCSS, and applicable native + font licenses.

Workflow:

```sh
bash tools/rmlui/build-native.sh linux-x64 gl2
dotnet publish src/MphRead/MphRead.csproj -c Release -r linux-x64 \
  -p:MphReadRmlUiGlSmoke=true --self-contained false \
  -m:1 -p:BuildInParallel=false -o artifacts/rmlui-publish/linux-x64
bash tools/rmlui/package-desktop.sh linux-x64 artifacts/rmlui-publish/linux-x64
PROJECT_PRIME_RMLUI_GL_SMOKE=1 artifacts/rmlui-publish/linux-x64/ProjectPrime
```

The target must match the native compiler host: Windows DLL must be compiled
on Windows, macOS dylibs on the correct arch's macOS target, Linux SO on Linux.
Cross-publishing .NET alone never cross-compiles the RmlUi native bridge.

The script fails closed on mismatched source fingerprint, ABI, manifest hashes,
renderer backend, missing fonts or missing native attribution. It cannot modify
normal release directories unless explicitly given as its output.

CI validates full Linux-x64 package integrity following native software-frame
acceptance. RML09 does not switch the default launcher or establish macOS,
Windows or Android physical-device runtime parity.
