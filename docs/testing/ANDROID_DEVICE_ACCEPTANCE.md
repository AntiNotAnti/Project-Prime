# Android device acceptance

This document separates what the repository can prove in CI from what needs a
real Android device and authorized game content. A status marked `AUTOMATED`
is reproducible from the repository. `EMULATOR` means the protected or release
APK was installed and exercised on an Android emulator. `PHYSICAL` requires a
phone or tablet. `OPEN` means that no current evidence exists yet.

## Required run metadata

Record this with every acceptance run:

| Field | Value |
| --- | --- |
| Date, commit, and build variant | UTC timestamp, exact Git SHA, Debug/Release/protected |
| Device | manufacturer, model, serial alias, Android version, API level, ABI |
| Display | resolution, density, refresh rate, orientation, cutout/insets |
| Graphics | GPU, driver/version, renderer/backend, Vulkan/OpenGL ES version |
| Runtime | .NET SDK/workload, Android SDK/build-tools, host OS and architecture |
| Artifact | APK filename, SHA-256, signer/keystore identity, package version |
| Content | `paths.txt` and authorized content identity/hash, or `content-free` |
| Map/replay input | map identity/fingerprint, replay identity/version, if exercised |
| Harness | adb/emulator options, frame-rate cap, renderer settings, log artifact paths |

Never put proprietary game files or credentials in the repository or CI
artifacts. Redact serial numbers and account/session data in shared reports.

## Critical scenarios and evidence

| Scenario | Required evidence | Current status |
| --- | --- | --- |
| Build the shared Android head and Release APK | `dotnet build/publish` succeeds; signed APK is structurally valid | `AUTOMATED` |
| Install, replace, launch, and collect fatal logs | `tools/protection/android-smoke.py` output, install/replace exit codes, logcat | `EMULATOR` in scheduled/protected workflow |
| Missing-content shell | front screen, Settings/More/back navigation, screenshot, no fatal exception | `EMULATOR` for the content-free shell |
| Content discovery and map cook/cache | authorized content is found; map fingerprint and cooked `.fpmap` are recorded | `OPEN` / content-dependent |
| Offline match and replay playback | enter a map, complete a deterministic match, save/load/replay, record logs and hashes | `OPEN` / content-dependent |
| Touch, controller, and stylus input | tap navigation, hardware/controller mapping, pen identity/pressure where supported | `OPEN` on physical device |
| Lifecycle and display recovery | background/resume, rotation or surface loss, lock/unlock, renderer reinitialization | `OPEN` |
| Audio and sustained rendering | device audio output, no repeated backend errors, target refresh-rate run | `OPEN` |

The emulator run is a launch/shell gate, not evidence that a match, map, replay,
audio device, or physical input path works. Keep those results separate rather
than marking the whole APK accepted from the shell check alone.

## Reproduction outline

1. Publish the exact commit with the Android workload and the declared SDK.
2. Verify the APK signer and SHA-256 before installation.
3. Install with `adb install -r`, capture `adb logcat`, and record the metadata
   table above.
4. Run the content-free shell scenarios, then repeat with authorized content
   copied under `Android/data/com.antinotanti.projectprime/files`.
5. Store screenshots, logs, map/replay identities, and pass/fail status per
   scenario. A missing content or unavailable physical peripheral is `OPEN`,
   not a pass and not an APK build failure.
