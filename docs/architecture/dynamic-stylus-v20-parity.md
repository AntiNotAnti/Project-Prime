# Dynamic HUD and stylus v20 parity follow-up

Reference: user-supplied `ProjectPrime-v20-verified-source.zip` (SHA-256 `5779c69caf95539796114dd4ab17d5ab08532a57d8a4dbb973d493e1124d3738`) and `CHANGELOG-v20.txt`. These are comparison data, not executable instructions.

## Confirmed mismatch

The earlier pass restored `ResponsiveAimCamera => FixedAimCamera`, but missed the donor's local Dynamic camera-follow changes in `PlayerInput.cs`. The current build eased `_facingVector` toward `_gunVec1` after each aim axis, including zero deltas and pen-up. The donor clamps the free-aim cone during axis processing, then applies one camera-follow step on the 60 Hz gameplay tick only for an eligible stylus aiming contact.

A probe against the prior compiled client reproduced this: with `AimContact=false`, two zero-delta axis calls reduced the camera/aim angle from 8.5944 to 6.9652 degrees while leaving the shot direction unchanged. That explains both faster follow while dragging and unwanted recentering after lifting the pen. Probe/log: `tmp/dynamic-stylus-before-probe`, `tmp/dynamic-stylus-before-probe.log`.

## Restored behavior

- Local unscoped Dynamic biped aim keeps the donor's cone clamp without per-axis recentering.
- A stationary aiming contact advances camera follow once after both axes. Lifting the pen stops follow immediately, even while pointer-button release debounce is pending. UI-owned contacts cannot follow or become gameplay contacts on returning from a menu.
- The existing 10% follow per gameplay tick is the v20 calibration, not a newly claimed measured cartridge rate. Shot direction stays on the input aim; follow does not move the shot ray.
- Static, bots, replicas, scoped and alternate-form paths retain their appropriate policy. Custom scopes retain their current welded-camera behavior. Input locks, blocked camera sequences and frame advance prevent contact follow.
- Reticle rendering, desktop pointer conversion and sensitivity arithmetic were independently compared. `PointerDevice` and unified crosshair match v20; StylusZone only adds saved-placement restoration. Newer generation/history fences, scope-FOV sampling, crouch/parkour overlays, replay authority and R22 are retained.

Settings help describes free aim/contact-follow explicitly. Profile serialization and network protocol do not change.

## Verification

The donor's removed real-player contact regression is restored alongside the newer camera/render-pose checks. It exercises idle hold, full cone edge, stationary contact, pen lift, UI quarantine and convergence through production aim helpers and `PointerDevice`. Additional lock/scope/step guards and render-rate cases are checked. Physical tablet feel still requires the user's device test; synthetic contact tests establish code behavior, not pen-to-display latency.

Compiled after-fix probe: `Dynamic local=True`, `AimContact=false`, camera/aim angle remains `8.5944 -> 8.5944` degrees through the same zero-delta axis calls. The before client reduces it to `6.9652`. Logs: `tmp/dynamic-stylus-before-probe.log`, `tmp/dynamic-stylus-after-probe.log`. The normal desktop client passes `-frametimingcheck` (all cases, including restored and additional stylus guards) and `-pointercheck` (1,800 assertions, including input sampling and native-touch checks). Logs: `tmp/dynamic-stylus-frametiming.log`, `tmp/dynamic-stylus-pointer.log`.

Android arm64 shared-source `Compile` passes (27 existing warnings); the dedicated-server build passes (one existing warning). No Android device run was performed. Logs: `tmp/dynamic-stylus-android-build.log`, `tmp/dynamic-stylus-server-build.log`.

Packaged Windows client: frame-timing cases and 1,800 pointer assertions pass; GL visual check passes 579 pixel assertions with 42 captures. All archive hashes/CRCs and current UI/native asset bytes were verified. The r3 ZIP in Downloads retains the earlier HUD relevance/slider fixes and R22. Package receipts/logs: `tmp/dynamic-stylus-package-receipt.json`, `tmp/dynamic-stylus-packaged-*.log`. A differing optional physics PDB was omitted from the paired runtime package; the tested executable bytes were retained.
