# Gyro input

Desktop SDL supplies right-handed angular velocity in radians/second. Device Y
maps to camera yaw, device X to pitch, and roll participates only in calibration
motion rejection. The neutral processor converts to degrees/second before
sensitivity and inversion.

Enabling gyro, connecting/replacing a controller, or choosing Recalibrate starts
a finite stationary bias capture: at least 30 samples spanning 500 ms. Motion
above 5 degrees/second rejects and restarts that window. Once ready, the learned
bias is subtracted and values at or below the initial 0.75 degrees/second noise
floor produce zero. Optional micro-smoothing exists internally and defaults off.

Calibration lifetime is separate from transient suppression. Pause, focus loss,
the weapon radial, Zoom Only outside zoom, and an unheld Hold Button clear live
output without discarding bias. Disconnect/reconnect resets device state and
requires fresh calibration. Reordered source timestamps are rejected and stale
output expires after 100 ms.

Gyro Aiming modes are Off, Always, Zoom Only, and Hold Button. Hold Button can
use Left Trigger, Left Bumper, or Right Thumb. The settings action recalibrates
without blocking the game thread and exposes calibration status. Only conditioned
nonzero motion contributes `GamepadGyro` ownership. Any actual gyro contribution
makes the frame ineligible for controller-stick aim assist.

DualSense and Switch Pro hardware measurements for drift, latency, slow tracking,
and fast correction remain release gates. Ratcheting is intentionally deferred
until the calibrated base path has hardware evidence.
