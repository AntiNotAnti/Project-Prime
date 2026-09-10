using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Pointer-id-owned relative stylus input. Native adapters submit neutral
    /// samples; simulation consumes only the shared look coordinator.
    /// </summary>
    public sealed class StylusInput
    {
        private readonly object _gate = new();
        private readonly LookInputCoordinator _look;
        private StylusState _state = StylusState.Empty;
        private StylusButtons _pressed;
        private StylusButtons _released;
        private float _density = 1;
        private bool _enabled = true;
        private float _sensitivity = 1;
        private bool _invertY;
        private bool _pressureToFire;
        private float _pressureThreshold = 0.35f;
        private readonly AimGestureRecognizer _gestures = new();
        private bool _flickContext;
        private long _captureEpoch = -1;

        public StylusInput(LookInputCoordinator? look = null)
        {
            _look = look ?? LookInputCoordinator.Shared;
        }

        public bool Active { get { lock (_gate) return _state.Contact; } }

        public void ConfigureGestures(bool classic, bool doubleTap, bool flick,
            bool flickContext, float density)
        {
            lock (_gate)
            {
                _gestures.Enabled = classic;
                _gestures.DoubleTapEnabled = classic && doubleTap;
                _gestures.FlickEnabled = classic && flick && flickContext;
                _gestures.Density = density;
                _flickContext = flickContext;
                if (!classic) _gestures.Cancel();
            }
        }

        public void Configure(bool enabled, float sensitivity, bool invertY,
            bool pressureToFire, float pressureThreshold, float density)
        {
            lock (_gate)
            {
                _enabled = enabled;
                _sensitivity = float.IsFinite(sensitivity)
                    ? Math.Clamp(sensitivity, 0.01f, 10) : 1;
                _invertY = invertY;
                _pressureToFire = pressureToFire;
                _pressureThreshold = float.IsFinite(pressureThreshold)
                    ? Math.Clamp(pressureThreshold, 0, 1) : 0.35f;
                _density = float.IsFinite(density) && density > 0 ? density : 1;
                if (!_enabled && _state.Contact)
                {
                    long epoch = _captureEpoch;
                    ResetLocked();
                    _captureEpoch = -1;
                    // Configuration loss is cancellation, not a normal lift:
                    // discard this source's pending movement while preserving
                    // unrelated device events in the coordinator.
                    _look.Cancel(LookDeviceKind.Stylus, epoch);
                }
            }
        }

        public bool PointerDown(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            lock (_gate)
            {
                if (!_enabled || _state.Contact && _state.PointerId != sample.Id)
                    return false;
                // Keep the local lifecycle lock while the coordinator returns
                // the capture epoch. Cancel/Configure use the same lock order
                // (stylus -> coordinator), so they cannot invalidate a new
                // capture between Claim and recording its epoch.
                long expectedEpoch = _look.Epoch;
                if (!_look.Claim(LookDeviceKind.Stylus, expectedEpoch,
                    out long captureEpoch))
                {
                    return false;
                }
                UpdateButtons(sample.Buttons);
                _state = Build(sample, contact: true);
                _gestures.PointerDown(sample.X, sample.Y, sample.Timestamp);
                _captureEpoch = captureEpoch;
                return true;
            }
        }

        public bool PointerMove(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            Vector2 raw;
            Vector2 degrees;
            bool flick;
            long epoch;
            lock (_gate)
            {
                if (!_enabled || !_state.Contact || _state.PointerId != sample.Id)
                    return false;
                raw = new Vector2((sample.X - _state.X) / _density,
                    (sample.Y - _state.Y) / _density);
                UpdateButtons(sample.Buttons);
                _state = Build(sample, contact: true);
                flick = _gestures.PointerMove(sample.X, sample.Y, sample.Timestamp);
                degrees = new Vector2(-raw.X, -raw.Y * (_invertY ? -1 : 1))
                    * (_sensitivity / 4f);
                epoch = _captureEpoch;
            }
            if (degrees != Vector2.Zero && !flick)
            {
                _look.Submit(new LocalLookFrame(LookDeviceKind.Stylus,
                    degrees, raw, raw.Length), expectedEpoch: epoch);
            }
            return true;
        }

        public bool PointerUp(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            lock (_gate)
            {
                if (!_state.Contact || _state.PointerId != sample.Id) return false;
                UpdateButtons(StylusButtons.None);
                _gestures.PointerUp(sample.X, sample.Y, sample.Timestamp);
                _state = new StylusState(false, -1, sample.X, sample.Y,
                    Math.Clamp(sample.Pressure, 0, 1), StylusButtons.None,
                    _pressed, _released, sample.Timestamp, false,
                    false, false, 0, 0);
                // A normal lift closes contact but preserves movement already
                // submitted before the lift for the next fixed consume.
                _look.Release(LookDeviceKind.Stylus, _captureEpoch);
                _captureEpoch = -1;
            }
            return true;
        }

        public void Cancel()
        {
            lock (_gate)
            {
                long epoch = _captureEpoch;
                ResetLocked();
                _gestures.Cancel();
                _captureEpoch = -1;
                _look.Cancel(LookDeviceKind.Stylus, epoch);
            }
        }

        public StylusState ConsumeState()
        {
            lock (_gate)
            {
                bool doubleTap = _gestures.TakeDoubleTap();
                (bool fired, float x, float y) = _gestures.TakeFlick();
                StylusState result = _state with
                {
                    PressedButtons = _pressed,
                    ReleasedButtons = _released,
                    DoubleTapJump = doubleTap,
                    FlickBoost = fired && _flickContext,
                    FlickX = x,
                    FlickY = y
                };
                _pressed = _released = StylusButtons.None;
                _state = _state with
                {
                    PressedButtons = StylusButtons.None,
                    ReleasedButtons = StylusButtons.None
                };
                return result;
            }
        }

        private StylusState Build(in PointerSample sample, bool contact)
            => new(contact, sample.Id, sample.X, sample.Y,
                Math.Clamp(sample.Pressure, 0, 1), sample.Buttons,
                _pressed, _released, sample.Timestamp,
                _pressureToFire && sample.Pressure >= _pressureThreshold,
                false, false, 0, 0);

        private void UpdateButtons(StylusButtons buttons)
        {
            _pressed |= buttons & ~_state.Buttons;
            _released |= _state.Buttons & ~buttons;
        }

        private void ResetLocked()
        {
            _released |= _state.Buttons;
            _state = StylusState.Empty;
        }

        private static bool IsStylus(PointerToolKind tool)
            => tool is PointerToolKind.Stylus or PointerToolKind.Eraser;

        private static bool Finite(in PointerSample sample)
            => float.IsFinite(sample.X) && float.IsFinite(sample.Y)
                && float.IsFinite(sample.Pressure);
    }
}
