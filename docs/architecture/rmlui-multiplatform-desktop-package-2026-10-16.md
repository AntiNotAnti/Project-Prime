# RML-15: Multi-platform native desktop package acceptance

The existing Linux-x64 software OpenGL test remains the only automated
pixel/readback acceptance gate. This slice adds source-pinned, per-runner
**package integrity** checks for `win-x64`, `osx-x64` and `osx-arm64`.

Each runner builds the exact RmlUi 6.3 and FreeType dependency commits in the
native CMake bridge, publishes the opt-in desktop client using its native
RID, copies the matching RmlUi library/manifest/licenses, and verifies:

- native PE/Mach-O architecture, exported ABI, source SHA256 fingerprint;
- RmlUi and FreeType pinned revisions and C++ bridge's GL2 backend;
- required markup, Noto Sans JP / OFL fonts, attribution, and bootstrap assets;
- package native binary digest, correct relative installation paths and
  matching platform library name.

macOS also runs `codesign --verify` against the explicitly *ad-hoc signed*
development dylib. This is **not** an Apple-notarized distributable and does
not provide release-identity signing, Gatekeeper distribution, or real-device
graphics/IME/controller validation.

These are opt-in packaging tests. They must not alter the shipping Avalonia
client or add alternative graphics backends. Windows native dependencies are
compiled on a Windows runner, not reused from a Linux cross-publish. macOS
builds use architecture-specific runners. A failure must remain visible in CI;
the packaging checks are not informational skips.

For local platform testing, use
`bash tools/rmlui/build-native.sh <rid> gl2`,
`dotnet publish ... -p:MphReadRmlUiGlSmoke=true -r <rid>`, and
`bash tools/rmlui/package-desktop.sh <rid> <publish-directory>`.
