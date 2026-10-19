# Presence and Community publication CI fixture compatibility

The asset-free presence harness links production controller sources. Its current
Social controller also depends on invitation and party travel contracts, but the
project omitted that dependency closure. The harness now includes the real
`NativeSocialInvitations`, `NativePartyTravelContracts`,
`NativePartyReservationContracts` and `NativePartyAuthorityController` sources.
It continues to use a synthetic service and clock; it adds no account or network
operation and does not substitute fake production contracts.

The Community publication native GL fixture retained the removed
`native_studio_publish` control as its parent focus target. The Studio entry page
now presents a workspace chooser. Its visible `native_studio_theatre` button is
the fixture's retained parent control. The fixture verifies that this actual
button accepts focus before opening the modal and that focus returns there after
Back. Native pointer, bounds, sequence, foreground ownership and account-retirement
assertions remain intact. The injected publication authority does not publish a
real package or contact a backend.

These are harness repairs only; no production source changes are included.

## Verification

- macOS arm64 portable presence check: 86 assertions, zero failing scenarios.
- Existing ignored GL2 native bridge verified against the current source
  fingerprint and its library SHA-256 with `tools/rmlui/verify-runtime.py`.
  No bridge library was copied from another checkout.
- Current production and GL fixture build: zero warnings/errors (4:21.57).
- Published package: 148 authored markup/style assets verified with all local links
  resolved; packaged native library also passed fingerprint/hash verification.
- Actual framebuffer 640×360, 1280×720 and 1920×1080 Community publication GL
  checks pass on macOS arm64, including parent focus before and after the modal,
  account retirement and final document/GL cleanup. These are native macOS GL checks, not
  Linux llvmpipe/software-frame acceptance.

Logs are retained under `artifacts/ci-presence-publish/` in the dedicated
compatibility worktree. One initial GL build was deliberately cancelled while
coordinating shared-machine compiler load; that cancellation is not a successful
build or a source compilation failure.

Commands:

```sh
dotnet run --project tools/rmlui-presence-check -c Release
python3 tools/rmlui/verify-runtime.py \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib osx-arm64
dotnet build tools/rmlui-opengl-check -c Release -m:1 -p:BuildInParallel=false
```

On this Retina Mac, request client sizes 320×180, 640×360 and 960×540 for actual
framebuffers 640×360, 1280×720 and 1920×1080. The native fixture logs both values;
acceptance depends on the reported framebuffer dimensions, not the requested
client size.

Reproduce the passing native runs with isolated user-data directories:

```sh
for client in 320x180 640x360 960x540; do
  PROJECT_PRIME_USER_DATA="$PWD/artifacts/ci-presence-publish/user-$client" \
    dotnet tools/rmlui-opengl-check/bin/Release/net10.0/rmlui-opengl-check.dll \
    tools/rmlui-opengl-check/bin/Release/net10.0/rmlui --community-publish "$client"
done
```
