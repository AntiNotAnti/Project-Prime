# Controller input

Both SDL and Android produce the same Xbox-shaped `GamepadState`; controller
family affects button labels only. `PadBindings` maps normalized buttons to
game actions, including the weapon radial. Trigger axes become buttons through
press/release hysteresis before bindings are applied.

Movement uses a radial dead zone, 0.25 activation and 0.18 release thresholds,
then eight-way quantization. A retained sector has 6 degrees of angular
hysteresis so noise near a 22.5-degree boundary does not alternate directions.
Disconnect and inactive movement reset the retained sector.

Look uses radial inner and outer dead zones. The shipped response preset is
Balanced (exponent 1.60); Linear is 1.00, Precision is 2.00, and Custom retains
the advanced numeric exponent. Yaw and pitch rates default to 300 and 240
degrees/second. Turn Acceleration offers Off, Standard, and Fast mappings over
the existing outer-ring boost fields. Fast shortens delay/ramp without changing
the maximum configured yaw or pitch rate. Zoom scaling applies to the controller
component only.

Controller-stick aim assist is local rotational/friction assistance only. It
uses the target's current active collision center, conservative acquire/retain
cones, a switching margin, and applied-angle escape intent so deliberate motion
away from a target rapidly releases both pull and slowdown. It never changes
hitboxes, projectiles, server authority, or non-stick input. Mixed mouse,
touch, stylus, or gyro frames are ineligible.

Aim assist intentionally has no row on the normal, advanced, preset, or search
settings surfaces. Its enabled and strength values remain persisted internal
policy keys for controlled builds and migration compatibility; controller
presets do not overwrite them.

Haptics stay queued to the SDL host. Vibration strength scales low/high
amplitudes at the pump boundary while duration and priority remain unchanged;
zero strength stops active output.

Extended SDL controls were reviewed, but rear paddles, touchpad/share clicks,
and miscellaneous buttons are not yet exposed consistently by every active
backend. The normalized Xbox-shaped mapping therefore remains unchanged until
cross-platform and hardware evidence can support stable `Paddle`/`Misc`
semantics. Controller-family differences continue to affect presentation only;
visual glyph assets remain later presentation work.

Use `ProjectPrime -gamepad [-seconds N]` to inspect normalized axes, buttons,
bindings, family, and reported capabilities. Real Xbox Series, DualSense, and
Switch Pro USB/Bluetooth validation is still a release gate, not implied by
build or virtual-input tests.
