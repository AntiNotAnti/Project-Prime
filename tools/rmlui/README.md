# RmlUi native builds and opt-in packages

The `RmlUi migration acceptance` CI job requires every build, platform, server,
Studio, input and renderer gate for the same revision to succeed. Its Linux full
source job reuses the compiled bridge, builds the actual client in isolated
native, transitional, default and server snapshots, audits the native package,
and runs shared Shell routing against the resulting `ProjectPrime.dll` files.
It also runs the module/controller/native DOM checks, real local UDP lobby and
queue fixtures, accessibility/draw-list caching, Vulkan recovery and 100 native
page lifetime cycles. It never supplies cartridge files or production account
state. To run that Linux job locally after building the bridge and patched
wgpu-native payload:

```sh
bash tools/rmlui/full-source-check.sh \
  artifacts/rmlui-native/linux-x64/libProjectPrime.RmlUi.Native.so \
  linux-x64 /tmp/prime-rmlui-full-source-proof
```

Use a new evidence directory. Logs, real source snapshots, the audited native
package and acceptance JSON remain there for review. Passing this gate proves
the automated checks it runs; the user's live testing is separate.

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
from the bridge and exposes the native draw-list ABI. The modern engine and
Android ES3 compositors consume this protocol; their platform acceptance is
tracked separately from native compilation. Both adapters default to the same
RID artifact directory. Set `PRIME_RMLUI_OUTPUT_DIR` to an absolute directory
when retaining both variants, for example `artifacts/rmlui-native-neutral/osx-arm64`.

macOS builds set the dylib identity to
`@rpath/libProjectPrime.RmlUi.Native.dylib` and apply an ad-hoc signature. This
is a local build signature, not Developer ID signing or notarization. Verify
the payload before subsequent distribution signing, which changes binary
hashes. Windows CI checks compilation and package contents; it does not prove
SmartScreen acceptance or real Windows keyboard/IME/GPU behavior. Those release
and clean-machine acceptance gates remain open.

## Android

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
not select RmlUi presentation.

`MphReadRmlUiAndroid=true` selects the opt-in native launcher and shared Core/page
presenters and includes the assets automatically. Launcher documents and GPU
resources belong to a dedicated render thread. Match menus use the existing game
render owner after the launcher retires its graphics lease. Android input, back,
content insets and virtual accessibility descendants queue immutable events to
that owner. The existing launcher remains the default until acceptance passes.

```sh
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 \
  -p:MphReadRmlUiAndroid=true -p:AndroidPackageFormat=apk \
  -o publish/rmlui-android-runtime
python3 tools/rmlui/verify-runtime.py --apk publish/rmlui-android-runtime/com.projectprime.game-Signed.apk android-arm64
```

An explicit validation build adds `-p:MphReadRmlUiAndroidCheck=true`. Its ES3
fixture checks real framebuffer pixels, atlas/color blending, transforms,
scissors, clipping, scene-stencil preservation, GL-state restoration and 100
stable GPU resource cycles before the first launcher frame. Start this APK with
the Activity boolean extra `rmlui-ime-check=true` to exercise the real Android
`InputConnection` against native fields without submitting account forms.
The extras `rmlui-a11y-check=true` and `rmlui-hud-check=true` run actual Android
virtual-node actions and framework multitouch events through the same owner queue.
Results are logged with `[rmlui-android]` and written to private app files and
mirrored to app-specific external storage. The mirror lets the owned emulator
collect trimmed Release evidence without enabling debugging. These fixtures are
disabled in normal builds. Coverage is the actual emulator/framework path;
physical device IMEs, TalkBack and vendor GPUs are covered by user live testing.
Normal AOT package publishing is checked separately in CI.

Add `-p:MphReadAvalonia=false` to the Android runtime publish above to select the
native-only Activity and Application. This opt-in removes Avalonia packages,
assemblies and resources while retaining the existing game, replay and lobby
authorities. Verify the actual signed APK and its corresponding restore graph:

```sh
python3 tools/rmlui/verify-android-client.py \
  --apk publish/rmlui-android-runtime/com.projectprime.game-Signed.apk \
  --assets src/MphRead.Android/obj/project.assets.json --expect-avalonia false
```

Use `--expect-avalonia true` for the default Android build as a positive control.
The audited restore graph must come from the same build configuration as its APK.

On an existing task-owned emulator, collect fresh framework reports and screenshots:

```sh
python3 tools/rmlui/android-runtime-check.py --apk path/to/validation-Signed.apk \
  --serial emulator-5554 --hud --output artifacts/rmlui-android-proof
```

The Linux CI wrapper `tools/rmlui/android-check.sh` creates and retires its own
Android 36 emulator on port 5560. Its evidence manifest records the exact APK hash.
These framework fixtures run without shipping cartridge files; real match/replay
acceptance uses locally extracted game data and is recorded separately.

The canonical desktop-to-GLES source guard runs independently of Android:

```sh
dotnet run --project tools/rmlui-es-shader-check -c Release
```

It checks all six pinned translation hashes. Actual Android scene rendering
also checks that the resulting GLES shaders compile on the active driver.

The Linux IBus transport fixture uses an isolated session bus and Xvfb:

```sh
dbus-run-session -- xvfb-run -a /usr/bin/python3 tools/rmlui-linux-ime-check/run-ibus-check.py \
  --native artifacts/rmlui-native/linux-x64/libProjectPrime.RmlUi.Native.so
```

It requires `ibus`, `libibus-1.0-5`, `gir1.2-ibus-1.0`, `python3-gi`, `dbus-x11`,
`xvfb` and `xauth`. The deterministic engine checks Unicode preedit, commit/cancel,
focus retirement and fallback through actual D-Bus transport.
