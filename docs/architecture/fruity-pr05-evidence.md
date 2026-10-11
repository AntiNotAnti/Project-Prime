# PR05 spectator free-camera reliability

The spectator camera now uses the normal hunter mouse scale (four pixels per degree before sensitivity), mouse-axis inversion, and a safe pitch limit based on the hunter's `Facing.Y = ±0.985` boundary. Its facing/right/up basis stays orthonormal even when initialized from a vertical watched-player view (preserving the previous horizontal right axis when yaw is undefined), preserving full strafe speed near the pitch limit.

Focus loss suppresses keyboard movement, Alt selection, Shift speed, mouse drag, mouse aiming, late presentation aim and gameplay hardware input. Hardware ownership resumes after keys and mouse buttons return to neutral; a fire button held through focus regain cannot become a fresh shot. Controller focus handling retains its existing reset path.

Compatibility decision: Space retains live spectator view toggling and replay play/pause. E ascends in both live spectator and replay free cameras, V descends; the standalone model viewer retains Space ascent. Android touch UP emits E for both live and replay spectator cameras. Repeated Space key events do not repeatedly toggle live view.

Content-free regression command:

```sh
dotnet run --project tools/nettest --no-restore -- --spectator-camera
```

The checks cover focus loss and neutral release, held movement/modifier/fire suppression, near-vertical unit camera axes, pitch bounds, normal hunter mouse scale, inversion, and live/replay ascent compatibility. Physical mouse/keyboard focus-transfer and replay camera checks still require a running game window; no device evidence is claimed here.

Local result (2026-10-11): PASS using `/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Debug/net10.0/nettest.dll --spectator-camera`. The hardware-neutral latch suite exercises ten repeated focus cycles.
