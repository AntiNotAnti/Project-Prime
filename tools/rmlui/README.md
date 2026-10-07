# RmlUi native builds and opt-in packages

The game client still defaults to its existing launcher. `MphReadRmlUi=true`
is the separate opt-in runtime build feature; `MphReadRmlUiPoc=true` remains a
compatible development switch. Dedicated-server builds suppress both UI
features and do not package the bridge, its fonts, or its documents.

## Desktop

Install CMake, Git, Python 3, and a C++17 compiler. Windows additionally needs
Visual Studio C++ build tools and Git for Windows. Dependencies are pinned
inside `artifacts/rmlui-poc-src`; FreeType and RmlUi core are linked statically.
Linux GL2 builds need OpenGL development headers (for example,
`libgl1-mesa-dev` on Debian/Ubuntu).
The Windows bridge uses the static MSVC runtime, so it does not require a
separate VC++ redistributable for its own runtime.

```sh
tools/rmlui/build-native.sh auto
# Explicit RIDs: win-x64, osx-arm64, osx-x64, linux-x64.
# PowerShell entry point on Windows:
# tools/rmlui/build-native.ps1 -Target win-x64

dotnet publish src/MphRead/MphRead.csproj -c Release -r osx-arm64 \
  -p:MphReadRmlUi=true --self-contained true -p:PublishSingleFile=true \
  -o publish/rmlui-osx-arm64
python3 tools/rmlui/verify-runtime.py --package publish/rmlui-osx-arm64 osx-arm64
```

The existing patched wgpu-native build remains a prerequisite for publishing a
client. Build it with `tools/wgpu/build-native.sh <rid>` if its artifact is absent.
The RmlUi publish gate fails if the library or its `PRIME-RMLUI.json` manifest is
missing, has the wrong architecture/dependency revisions, or comes from stale
bridge sources. The package keeps RML/RCSS, fonts, font licenses, native licenses,
the native library, and its manifest beside the single-file executable.

`PRIME_RMLUI_BUILD_JOBS=1..32` sets build concurrency (default 2).
`PRIME_RMLUI_OFFLINE=1` skips fetches only when both dependency checkouts exactly
match their pinned commits and contain no modified tracked files.

The compatibility GL2 adapter is the desktop default. A neutral core build is
also available:

```sh
tools/rmlui/build-native.sh osx-arm64 draw-list
```

`PROJECT_PRIME_RMLUI_GL2=OFF` omits all OpenGL headers/backend sources/linking
from the bridge and exposes the native draw-list ABI. This is a rendering
foundation: an engine GPU consumer and backend validation are required before
it can provide a production modern-renderer UI. Each adapter currently writes
the same RID artifact directory; rebuild `gl2` before running GL2 regressions.

macOS builds set the dylib identity to
`@rpath/libProjectPrime.RmlUi.Native.dylib` and apply an ad-hoc signature. This
is a local build signature, not Developer ID signing or notarization. Verify
the payload before subsequent distribution signing, which changes binary
hashes. Windows CI checks compilation and package contents; it does not prove
SmartScreen acceptance or real Windows keyboard/IME/GPU behavior. Those release
and clean-machine acceptance gates remain open.

## Android foundation

Install Android NDK 28.2.13676358 (the CI pin). The same static dependencies are
cross-built for `arm64-v8a` and `x86_64`; C++ is linked statically and ELF load
segments are aligned for 16 KB pages.

```sh
ANDROID_NDK_ROOT=/path/to/ndk/28.2.13676358 tools/rmlui/build-native.sh android-arm64
ANDROID_NDK_ROOT=/path/to/ndk/28.2.13676358 tools/rmlui/build-native.sh android-x64
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 \
  -p:MphReadRmlUiNativeAssets=true -o publish/rmlui-android-arm64
python3 tools/rmlui/verify-runtime.py --apk publish/rmlui-android-arm64/com.projectprime.game-Signed.apk android-arm64
```

Use the actual signed APK filename produced by the Android SDK. Repeat with
`android-x64` to build the emulator ABI; without a RID argument the APK verifier
expects both ABIs in one APK. An Android publish still needs the Android workload/SDK/JDK and the existing Android
wgpu-native/KTX payloads. `MphReadRmlUiNativeAssets` only includes the neutral
core, documents, fonts, and attributions for integration development. It does
not select RmlUi presentation or replace the Activity's Avalonia/input/lifecycle
path. Native compilation and payload checks do not constitute physical-device
validation; Android touch IDs, IME, safe areas, back handling, surface recovery,
and suspend/resume remain separate runtime work.
