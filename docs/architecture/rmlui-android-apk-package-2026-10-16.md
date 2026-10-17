# RML-17: Android opt-in resource package

This slice adds an explicit `MphReadRmlUiAndroidPackage=true` build flag
that stages authored RML/RCSS, licensed fonts, native attributions and
both Android NDK **draw-list** shared libraries into an APK.

The Android user interface still runs Avalonia. There is **no RmlUi renderer
activation, OpenGL ES draw-list playback, touch/IME controller, or automatic
fallback in this PR**. Never use the desktop GL2 build on Android.

The packaging gate requires both architecture-specific libraries and native
license files. Build both with the pinned Android NDK as documented in RML-16,
then package via the Android project using the opt-in property. The existing
`tools/rmlui/verify-runtime.py --apk path.apk` checks contents, library
architectures, SHA manifests and source/attribution contracts.

Missing native packages fail the explicit opt-in build. Standard Android
builds remain unaffected.

Next: OpenGL ES draw-list compositor sharing the existing game surface,
then touch/IME and lifecycle and real-device acceptance before default
RmlUi migration.
