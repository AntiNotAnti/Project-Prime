using System;
using System.Diagnostics;
using MphRead;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// The single ownership/extraction gate for local look input.
    ///
    /// Platform threads submit neutral frames; the fixed-step consumer takes
    /// them exactly once. Ownership and contributor metadata are changed under
    /// the same gate as submission, so a precision event followed by a stick
    /// sample cannot expose a controller-only frame to a later consumer.
    /// </summary>
    public sealed class LookInputCoordinator
    {
        public const double PrecisionReclaimQuietSeconds = 0.20;

        private readonly object _gate = new();
        private readonly LookPredictionBuffer _prediction = new();
        private readonly Func<double> _clock;
        private LookDeviceTracker _tracker;
        private LookDeviceKind _contributors;
        private Vector2 _statefulRawDirection;
        private float _statefulMagnitude;
        private bool _precisionContact;
        private bool _hasPrecisionTimestamp;
        private double _lastPrecisionTimestamp;
        private long _epoch;

        public LookInputCoordinator(Func<double>? clock = null)
        {
            _clock = clock ?? DefaultNow;
        }

        public LookPredictionBuffer Prediction => _prediction;

        public LookDeviceKind ActiveLookDevice
        {
            get { lock (_gate) return _tracker.ActiveLookDevice; }
        }

        public LookDeviceKind PendingContributors
        {
            get { lock (_gate) return _contributors; }
        }

        public long Epoch
        {
            get { lock (_gate) return _epoch; }
        }

        public static LookInputCoordinator Shared { get; } = new();

        /// <summary>
        /// Claim ownership without inventing motion. Used by stylus contact so
        /// the first sample disables controller assistance but never snaps aim.
        /// </summary>
        public bool Claim(LookDeviceKind device, double? seconds = null)
            => Claim(device, out _, seconds);

        /// <summary>
        /// Atomically claim a source and return the lifecycle epoch that owns
        /// the capture. Stylus adapters hold their own gate while calling this
        /// method, then use the returned epoch for every later sample.
        /// </summary>
        public bool Claim(LookDeviceKind device, out long captureEpoch,
            double? seconds = null)
            => Claim(device, expectedEpoch: null, out captureEpoch, seconds);

        /// <summary>
        /// Atomically claim a source only if the caller's lifecycle snapshot
        /// is still current. This closes the PointerDown-vs-Reset window.
        /// </summary>
        public bool Claim(LookDeviceKind device, long expectedEpoch,
            out long captureEpoch, double? seconds = null)
            => Claim(device, (long?)expectedEpoch, out captureEpoch, seconds);

        private bool Claim(LookDeviceKind device, long? expectedEpoch,
            out long captureEpoch, double? seconds)
        {
            captureEpoch = -1;
            if (!LookDeviceTracker.IsSingleDevice(device)) return false;
            lock (_gate)
            {
                if (expectedEpoch.HasValue && expectedEpoch.Value != _epoch)
                {
                    return false;
                }
                if (!_tracker.Claim(device)) return false;
                _contributors |= device;
                if (LookDeviceTracker.IsPrecisionDevice(device))
                {
                    StartPrecision(device, ResolveTime(seconds));
                }
                captureEpoch = _epoch;
                return true;
            }
        }

        /// <summary>Submit a relative event contribution.</summary>
        public bool Submit(in LocalLookFrame frame, double? seconds = null,
            long? expectedEpoch = null)
        {
            if (!frame.IsFinite || !LookDeviceTracker.IsSingleDevice(frame.Device))
            {
                return false;
            }
            lock (_gate)
            {
                if (expectedEpoch.HasValue && expectedEpoch.Value != _epoch)
                {
                    return false;
                }
                double now = ResolveTime(seconds);
                if (!TryClaim(frame.Device, frame.Magnitude, now))
                {
                    return false;
                }
                LookDeviceKind contributors = frame.Contributors == LookDeviceKind.None
                    ? frame.Device : frame.Contributors;
                if (LookDeviceTracker.IsPrecisionDevice(frame.Device))
                {
                    StartPrecision(frame.Device, now);
                }
                _contributors |= contributors;
                _prediction.Add(frame.WithContributors(contributors), now);
                return true;
            }
        }

        public bool Submit(LookDeviceKind device, Vector2 deltaDegrees,
            Vector2 rawDirection, float magnitude, double? seconds = null)
            => Submit(new LocalLookFrame(device, deltaDegrees, rawDirection, magnitude), seconds);

        /// <summary>
        /// Submit stateful look ownership/velocity without creating a fake
        /// relative event. Call this from a fixed-step processor after it has
        /// advanced its state.
        /// </summary>
        public bool SubmitStateful(in LocalLookFrame frame, Vector2 angularVelocity,
            float gamepadLookDeadzone = 0.10f, double? seconds = null,
            long? expectedEpoch = null)
        {
            if (!frame.IsFinite || !LookDeviceTracker.IsSingleDevice(frame.Device)
                || !float.IsFinite(angularVelocity.X) || !float.IsFinite(angularVelocity.Y))
            {
                return false;
            }
            lock (_gate)
            {
                if (expectedEpoch.HasValue && expectedEpoch.Value != _epoch)
                {
                    return false;
                }
                double now = ResolveTime(seconds);
                if (!TryClaim(frame.Device, OwnershipMagnitude(frame), now,
                    gamepadLookDeadzone))
                {
                    return false;
                }
                _contributors |= frame.Contributors == LookDeviceKind.None
                    ? frame.Device : frame.Contributors;
                _statefulRawDirection = frame.RawDirection;
                _statefulMagnitude = frame.Magnitude;
                _prediction.SetAngularVelocity(angularVelocity, now);
                return true;
            }
        }

        /// <summary>Publish a stateful velocity under the same ownership gate.</summary>
        public void SetStatefulVelocity(Vector2 angularVelocity, double? seconds = null)
        {
            if (!float.IsFinite(angularVelocity.X) || !float.IsFinite(angularVelocity.Y))
            {
                return;
            }
            lock (_gate)
            {
                if (angularVelocity == Vector2.Zero)
                {
                    _statefulRawDirection = Vector2.Zero;
                    _statefulMagnitude = 0;
                }
                _prediction.SetAngularVelocity(angularVelocity, ResolveTime(seconds));
            }
        }

        public LocalLookFrame PeekForRender(double? seconds = null,
            bool simulationActive = false,
            double? simulationRemainderSeconds = null)
        {
            lock (_gate)
            {
                double now = ResolveTime(seconds);
                (Vector2 mouse, Vector2 touch, Vector2 stylus)
                    = _prediction.PeekPrecisionForRender(now);
                Vector2 precision = mouse + touch + stylus;
                Vector2 controller = _prediction.PeekStatefulForRender(now,
                    simulationActive, simulationRemainderSeconds);
                Vector2 rawMouse = _prediction.PeekRawMouseForRender(now);
                Vector2 delta = precision + controller;
                LookDeviceKind contributors = _contributors
                    | _prediction.PendingContributors;
                Vector2 raw = controller != Vector2.Zero ? _statefulRawDirection : Vector2.Zero;
                float magnitude = controller != Vector2.Zero
                    ? _statefulMagnitude : precision.Length;
                return new LocalLookFrame(_tracker.ActiveLookDevice, delta,
                    raw, magnitude, controller, mouse, touch, stylus, rawMouse)
                    .WithContributors(contributors);
            }
        }

        /// <summary>Peek only stateful velocity under the coordinator gate.</summary>
        public Vector2 PeekStatefulForRender(double? seconds = null,
            bool simulationActive = false,
            double? simulationRemainderSeconds = null)
        {
            lock (_gate)
            {
                return _prediction.PeekStatefulForRender(ResolveTime(seconds),
                    simulationActive, simulationRemainderSeconds);
            }
        }

        /// <summary>Consume only relative events and advance the prediction boundary.</summary>
        public LocalLookFrame ConsumeForSimulation(double? seconds = null)
        {
            lock (_gate)
            {
                double now = ResolveTime(seconds);
                LookPredictionFrame consumed = _prediction.ConsumeFrameForSimulation(now);
                LookDeviceKind contributors = _contributors | consumed.Contributors;
                _contributors = LookDeviceKind.None;
                return new LocalLookFrame(_tracker.ActiveLookDevice,
                    consumed.DeltaDegrees, Vector2.Zero, consumed.DeltaDegrees.Length,
                    Vector2.Zero, consumed.MouseDeltaDegrees,
                    consumed.TouchDeltaDegrees, consumed.StylusDeltaDegrees,
                    consumed.RawMouseDelta)
                    .WithContributors(contributors);
            }
        }

        /// <summary>
        /// Consume relative events and include stateful velocity for one fixed
        /// step. Most gameplay callers already apply their processor's delta;
        /// this overload is useful to neutral consumers and focused tests.
        /// </summary>
        public LocalLookFrame ConsumeForSimulation(float fixedDeltaSeconds,
            double? seconds = null)
        {
            lock (_gate)
            {
                double now = ResolveTime(seconds);
                // Expire a stale velocity before taking the fixed-step sample;
                // otherwise a disconnected/stalled native source could leak
                // one last controller delta into a later precision frame.
                _prediction.PeekForRender(now, simulationActive: false);
                Vector2 controller = _prediction.AngularVelocity;
                if (!float.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0)
                {
                    controller = Vector2.Zero;
                }
                else
                {
                    controller *= fixedDeltaSeconds;
                }
                LookPredictionFrame consumed = _prediction.ConsumeFrameForSimulation(now);
                Vector2 precision = consumed.PrecisionDeltaDegrees;
                Vector2 delta = precision + controller;
                LookDeviceKind contributors = _contributors | consumed.Contributors;
                LookDeviceKind returned = _contributors;
                _contributors = LookDeviceKind.None;
                Vector2 raw = controller != Vector2.Zero ? _statefulRawDirection : Vector2.Zero;
                float magnitude = controller != Vector2.Zero
                    ? _statefulMagnitude : consumed.PrecisionDeltaDegrees.Length;
                return new LocalLookFrame(_tracker.ActiveLookDevice, delta,
                    raw, magnitude, controller, consumed.MouseDeltaDegrees,
                    consumed.TouchDeltaDegrees, consumed.StylusDeltaDegrees,
                    consumed.RawMouseDelta)
                    .WithContributors(returned | contributors);
            }
        }

        public void MarkSimulationStep(double? seconds = null)
        {
            lock (_gate) _prediction.MarkSimulationStep(ResolveTime(seconds));
        }

        /// <summary>
        /// End a pointer-owned precision capture. A normal lift preserves the
        /// already-submitted event for the next fixed consume; the expected
        /// epoch fences a late move racing with cancel/reset.
        /// </summary>
        public bool Release(LookDeviceKind device, long? expectedEpoch = null,
            double? seconds = null)
            => EndCapture(device, expectedEpoch, seconds, discardPending: false);

        /// <summary>
        /// Cancel a source-owned capture and discard only its pending events.
        /// Unrelated device events remain in the shared prediction buffer.
        /// </summary>
        public bool Cancel(LookDeviceKind device, long? expectedEpoch = null,
            double? seconds = null)
            => EndCapture(device, expectedEpoch, seconds, discardPending: true);

        private bool EndCapture(LookDeviceKind device, long? expectedEpoch,
            double? seconds, bool discardPending)
        {
            if (!LookDeviceTracker.IsSingleDevice(device)) return false;
            lock (_gate)
            {
                if (expectedEpoch.HasValue && expectedEpoch.Value != _epoch)
                {
                    return false;
                }
                double now = ResolveTime(seconds);
                if (discardPending)
                {
                    _prediction.Remove(device);
                }
                _contributors &= ~device;
                if (_tracker.ActiveLookDevice == device)
                {
                    _tracker.Reset();
                }
                if (LookDeviceTracker.IsPrecisionDevice(device))
                {
                    EndPrecision(device, now);
                }
                _epoch++;
                return true;
            }
        }

        /// <summary>
        /// Drop controller ownership and velocity without touching a pending
        /// relative look event or its precision-device owner. A disconnect
        /// must not erase a mouse/touch/stylus input that arrived in the same
        /// frame.
        /// </summary>
        public void ResetControllerState()
        {
            lock (_gate)
            {
                _contributors &= ~(LookDeviceKind.GamepadStick
                    | LookDeviceKind.GamepadGyro);
                if (LookDeviceTracker.IsControllerDevice(_tracker.ActiveLookDevice))
                {
                    _tracker.Reset();
                }
                _prediction.ClearStatefulVelocity();
                _statefulRawDirection = Vector2.Zero;
                _statefulMagnitude = 0;
            }
        }

        /// <summary>Reset all pending look, ownership, processor-boundary state.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _tracker.Reset();
                _contributors = LookDeviceKind.None;
                _prediction.Reset();
                _statefulRawDirection = Vector2.Zero;
                _statefulMagnitude = 0;
                _precisionContact = false;
                _hasPrecisionTimestamp = false;
                _lastPrecisionTimestamp = 0;
                _epoch++;
            }
        }

        private bool TryClaim(LookDeviceKind device, float magnitude, double now,
            float gamepadLookDeadzone = 0.10f)
        {
            // Expire a precision event before evaluating a new controller
            // sample. Without this small boundary a render stall could leave
            // an old mouse contributor shielding the first deliberate stick
            // sample after the stall.
            _prediction.PeekForRender(now, simulationActive: false);
            LookDeviceKind pending = _prediction.PendingContributors;
            if ((pending & (LookDeviceKind.Mouse | LookDeviceKind.Touch
                    | LookDeviceKind.Stylus)) == 0 && !_precisionContact)
            {
                // _contributors is the current fixed-frame ownership set,
                // while the prediction buffer owns event lifetime. Once the
                // buffer expires a relative event, do not let its precision
                // bit shield controller ownership indefinitely just because
                // an older stateful velocity is still being previewed.
                _contributors &= ~(LookDeviceKind.Mouse | LookDeviceKind.Touch
                    | LookDeviceKind.Stylus);
            }
            if (pending == LookDeviceKind.None
                && _prediction.AngularVelocity == Vector2.Zero && !_precisionContact)
            {
                _contributors = LookDeviceKind.None;
            }
            bool precisionPending = LookDeviceTracker.IsPrecisionDevice(_contributors)
                || LookDeviceTracker.IsPrecisionDevice(pending)
                || _precisionContact
                || !PrecisionQuietElapsed(now);
            if (LookDeviceTracker.IsPrecisionDevice(device)
                && _precisionContact && device != LookDeviceKind.Stylus)
            {
                // A finger/mouse source cannot steal an active stylus capture;
                // PointerInputRouter owns that explicit coexistence policy.
                return false;
            }
            if (device is LookDeviceKind.GamepadStick or LookDeviceKind.GamepadGyro
                && precisionPending)
            {
                // Keep the frame's event, but never let a later controller
                // sample turn same-frame mixed input into controller
                // ownership. Stick drift is still rejected at its deadzone;
                // gyro has no stick-scale threshold and only needs a finite,
                // meaningful sample.
                return float.IsFinite(magnitude)
                    && magnitude > (device == LookDeviceKind.GamepadStick
                        ? Math.Clamp(gamepadLookDeadzone, 0, 1) : 1e-6f);
            }
            return _tracker.Observe(device, magnitude, gamepadLookDeadzone);
        }

        private void StartPrecision(LookDeviceKind device, double now)
        {
            if (!LookDeviceTracker.IsPrecisionDevice(device) || !double.IsFinite(now))
            {
                return;
            }
            _lastPrecisionTimestamp = now;
            _hasPrecisionTimestamp = true;
            if (device == LookDeviceKind.Stylus)
            {
                _precisionContact = true;
            }
        }

        private void EndPrecision(LookDeviceKind device, double now)
        {
            if (!LookDeviceTracker.IsPrecisionDevice(device)
                || !double.IsFinite(now))
            {
                return;
            }
            _lastPrecisionTimestamp = now;
            _hasPrecisionTimestamp = true;
            if (device == LookDeviceKind.Stylus)
            {
                _precisionContact = false;
            }
        }

        private bool PrecisionQuietElapsed(double now)
            => !_hasPrecisionTimestamp
                || double.IsFinite(now) && now >= _lastPrecisionTimestamp
                    && now - _lastPrecisionTimestamp >= PrecisionReclaimQuietSeconds;

        private static float OwnershipMagnitude(in LocalLookFrame frame)
        {
            if (frame.Device == LookDeviceKind.GamepadStick
                && float.IsFinite(frame.RawDirection.X)
                && float.IsFinite(frame.RawDirection.Y))
            {
                return frame.RawDirection.Length;
            }
            return frame.Magnitude;
        }

        private double ResolveTime(double? seconds)
        {
            double now = seconds ?? _clock();
            return double.IsFinite(now) ? now : 0;
        }

        private static double DefaultNow()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
}
