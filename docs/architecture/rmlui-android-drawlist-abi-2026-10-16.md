# RML-16: Android draw-list native bridge acceptance

Android remains on the existing Avalonia front end. This change does **not**
enable the desktop GL2 adapter on Android, nor start a second GL context.

The Android NDK 27.2 native build matrix exercises both currently supported
Android library architectures, `android-arm64` and `android-x64`, using the
existing RmlUi 6.3 / FreeType 2.13.3 pinned dependency sources.

Unlike Windows and macOS, Android uses RmlUi's **draw-list ABI**, not fixed-
function OpenGL. CI must verify:

- the exact RmlUi/FreeType source SHA, native bridge fingerprint, export set,
  and release build manifest;
- Android ELF ABI identity and 16 KB load-segment alignment;
- the GL2 backend is *not* linked into the Android native binary;
- the source-built native license material is available.

These are **native foundation tests only**, not a complete RmlUi Android
application or runtime migration. Drawing the geometry with the existing
OpenGL ES game compositor, DPI/touch/IME and soft-keyboard lifecycle, APK
asset/native library packaging, and real device acceptance are required
before any Android runtime cutover.

The default client, Android Avalonia startup and GPU renderer remain untouched.
