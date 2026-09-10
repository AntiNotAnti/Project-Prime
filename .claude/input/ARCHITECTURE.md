# Input architecture

Project Prime keeps platform APIs at the edge and shares one neutral input path
between desktop and Android:

```text
SDL / Android events
        |
neutral producers (gamepad, mouse, touch, stylus, gyro)
        |
LookInputCoordinator
        +-- render-rate prediction
        `-- one consume at the fixed 60 Hz simulation boundary
```

`GamepadState` and `PointerSample` contain no SDL, Avalonia, or Android types.
The platform adapters update those values; `GamepadInput`, `StylusInput`, and
`GyroLookProcessor` perform deterministic conditioning. Android and Client
compile the same platform-neutral files through `Client.SharedInput.props`.

The coordinator distinguishes ownership from contribution. `Device` is the
winning source for the fixed step, while `Contributors` retains every source
that affected that frame. This matters for mixed frames: any mouse, touch,
stylus, or conditioned gyro contribution prevents controller-stick aim assist.
Hovering a pen and resting gyro noise never claim ownership.

Platform events may arrive at render cadence, but gameplay reads only the
immutable `LocalLookFrame` consumed by `ClientSceneServices.BeginLocalLookFrame`.
Rendering may predict pending motion; it cannot advance authoritative gameplay.
Focus loss and pause suppress transient output. Device replacement resets
device-owned state and gyro bias calibration.

Keep input hot paths bounded, allocation-free, and free of platform-specific
types. New controls should extend the neutral snapshot or binding vocabulary,
not create another gameplay input pipeline.
