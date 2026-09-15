# SDL controller diagnostics (P0-B)

Desktop controller diagnostics use the same SDL-owned host and normalized input
path as the client. `SdlGamepadHub` captures immutable device metadata only when
SDL opens/selects a device or observes a device transition. Per-frame polling
updates raw/effective state but does not repeatedly query identity APIs.

The report includes:

```text
name, family, GUID, vendor/product/version
gyro, accelerometer, analog-trigger, and rumble status
SDL mapping and runtime version
raw axes/buttons and effective mapped state
dead-zones, sensitivities, gyro/haptics settings, and active PadBindings
```

Optional SDL entry points are guarded. `unknown` remains distinct from
`unsupported`; a runtime that cannot answer a capability does not silently
disable a user's setting. SDL 3.4.16 remains the runtime boundary.

Run the focused probe with:

```bash
dotnet run --project src/Client/Client.csproj -c Release -- -gamepad 10
```

The probe creates one `SdlGameHost`, calls its `PumpToolEvents`, and reads the
existing `GamepadInput`, `PadBindings`, and `GyroLookProcessor` path. JSON and
text reports are written to the existing local `logs` directory with
collision-safe names. The report is safe to paste: it excludes serials, user
names, machine names, device paths, USB paths, and account information.

No GLFW probe remains. A small obsolete forwarding type is retained only so
older internal callers fail over to the SDL tool path without reintroducing a
second mapping implementation.

Settings > Controls > Gamepad now shows the latest immutable capability and
managed-input snapshot. It includes backend/device identity, raw and effective
axes/buttons, active bindings, dead-zones, sensitivities, response and
acceleration settings, gyro, and rumble configuration. The panel refreshes on
the UI dispatcher and never calls SDL or pumps events. On an unavailable,
non-SDL, or non-connected backend it says so explicitly and does not imply
physical-controller acceptance. Export uses the same builder as `-gamepad` and
writes collision-safe JSON/text files beneath the existing local `logs`
directory without exposing that private path in the report or status text.

## Physical acceptance matrix

These remain manual hardware gates. A source test or an unavailable-device
report does not mark a row passed.

| Controller | Connection | Status |
| --- | --- | --- |
| Xbox Series | USB | Not run |
| Xbox Series | Bluetooth | Not run |
| Xbox Elite Series 2 | USB | Not run |
| Xbox Elite Series 2 | Bluetooth | Not run |
| DualSense | USB | Not run |
| DualSense | Bluetooth | Not run |
| Switch Pro | Native SDL connection | Not run |
| Steam virtual controller | Virtual | Not run |
| Steam Deck controls | Integrated | Not run |
| Generic SDL controller | Native SDL connection | Not run |

For each row, retain the exported report and record whether identity, semantic
button mapping, both sticks, analog triggers, gyro when present, and rumble
when supported behaved correctly. Do not infer one transport from the other.
