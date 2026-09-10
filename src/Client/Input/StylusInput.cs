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

        /// <summary>
        /// True while the pen is in the tablet/display sensing range. This is
        /// deliberately independent from <see cref="Active"/>: hover samples
        /// may update state and buttons, but can never claim look ownership.
        /// </summary>
        public bool InProximity { get { lock (_gate) return _state.InProximity; } }

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
                if (!_enabled && (_state.Contact || _state.InProximity
                    || _captureEpoch >= 0))
                {
                    long epoch = _captureEpoch;
                    ResetLocked();
                    _gestures.Cancel();
                    _captureEpoch = -1;
                    // Configuration loss is cancellation, not a normal lift:
                    // discard this source's pending movement while preserving
                    // unrelated device events in the coordinator.
                    _look.Cancel(LookDeviceKind.Stylus,
                        epoch >= 0 ? epoch : null);
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
            long epoch;
            lock (_gate)
            {
                if (!_enabled || !_state.Contact || _state.PointerId != sample.Id)
                {
                    return false;
                }
                raw = NormalizeRelativeDelta(new Vector2(sample.X - _state.X,
                    sample.Y - _state.Y), sample, _density);
                UpdateButtons(sample.Buttons);
                _state = Build(sample, contact: true);
                // A flick is a one-shot gesture layered on top of the same
                // contact. It must not discard the ordinary aim delta from
                // this sample; the gameplay consumer decides independently
                // whether the reported flick is meaningful in the current
                // form/context.
                _gestures.PointerMove(sample.X, sample.Y, sample.Timestamp);
                degrees = new Vector2(-raw.X, -raw.Y * (_invertY ? -1 : 1))
                    * (_sensitivity / 4f);
                epoch = _captureEpoch;
            }
            if (degrees != Vector2.Zero)
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
                _state = new StylusState(false, sample.Id, sample.X, sample.Y,
                    Math.Clamp(sample.Pressure, 0, 1), StylusButtons.None,
                    _pressed, _released, sample.Timestamp, false,
                    false, false, 0, 0, _state.InProximity);
                // A normal lift closes contact but preserves movement already
                // submitted before the lift for the next fixed consume.
                _look.Release(LookDeviceKind.Stylus, _captureEpoch);
                _captureEpoch = -1;
            }
            return true;
        }

        /// <summary>
        /// Update a stationary pen sample (barrel buttons or pressure) without
        /// manufacturing relative look input or moving the contact anchor.
        /// A hover button is state-only: it never claims ownership or fires by
        /// itself; gameplay bindings require a real contact/active state.
        /// </summary>
        public bool UpdateButtonState(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            lock (_gate)
            {
                if (!_enabled) return false;
                if (_state.InProximity && _state.PointerId >= 0
                    && _state.PointerId != sample.Id)
                {
                    return false;
                }
                UpdateButtons(sample.Buttons);
                // Keep the last motion sample as the relative anchor. Native
                // button/axis events may carry a rounded or stale X/Y pair;
                // treating that pair as motion would create a jump on the
                // next real move.
                _state = Build(sample, contact: _state.Contact,
                    preserveAnchor: true);
                return true;
            }
        }

        /// <summary>
        /// Record a hover/proximity sample. Hover never submits look or claims
        /// the coordinator; the next contact starts a fresh zero-delta anchor.
        /// </summary>
        public bool PointerProximityMove(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            lock (_gate)
            {
                if (!_enabled || _state.Contact) return false;
                if (_state.InProximity && _state.PointerId >= 0
                    && _state.PointerId != sample.Id)
                {
                    _released |= _state.Buttons;
                }
                UpdateButtons(sample.Buttons);
                _state = Build(sample, contact: false);
                return true;
            }
        }

        /// <summary>
        /// End proximity. If contact was already lifted, leave its submitted
        /// movement queued for the next fixed consume just like a normal lift;
        /// an active contact is a cancellation and its pending stylus events
        /// are discarded by the coordinator.
        /// </summary>
        public bool PointerProximityExit(in PointerSample sample)
        {
            if (!IsStylus(sample.Tool) || !Finite(sample)) return false;
            long epoch = -1;
            bool cancel = false;
            lock (_gate)
            {
                if (!_state.InProximity || _state.PointerId < 0
                    || _state.PointerId != sample.Id)
                {
                    return false;
                }
                epoch = _captureEpoch;
                cancel = _state.Contact && epoch >= 0;
                UpdateButtons(StylusButtons.None);
                _gestures.Cancel();
                ResetLocked();
                _captureEpoch = -1;
                // Keep cancellation under the same stylus -> coordinator
                // lock order as PointerDown/Configure. Otherwise a recontact
                // could claim the old coordinator epoch before this cancel
                // fences it, and the late cancel would erase the new contact.
                if (cancel) _look.Cancel(LookDeviceKind.Stylus, epoch);
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
                // Explicit cancellation is allowed to discard a normal-lift
                // sample that is still waiting for the next consume. Passing
                // null when no contact owns an epoch makes that operation
                // atomic without touching other device contributions.
                _look.Cancel(LookDeviceKind.Stylus,
                    epoch >= 0 ? epoch : null);
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

        private StylusState Build(in PointerSample sample, bool contact,
            bool preserveAnchor = false)
            => new(contact, sample.Id,
                preserveAnchor && _state.InProximity ? _state.X : sample.X,
                preserveAnchor && _state.InProximity ? _state.Y : sample.Y,
                Math.Clamp(sample.Pressure, 0, 1), sample.Buttons,
                _pressed, _released, sample.Timestamp,
                contact && _pressureToFire && sample.Pressure >= _pressureThreshold,
                false, false, 0, 0, true);

        /// <summary>
        /// Convert native absolute-coordinate deltas into the neutral units
        /// used by stylus sensitivity. Direct devices use the logical display
        /// scale. Indirect devices use only an extent explicitly supplied by
        /// the adapter; when SDL cannot provide one, retain the conservative
        /// density-only fallback and leave hardware tuning to that adapter.
        /// </summary>
        internal static Vector2 NormalizeRelativeDelta(Vector2 delta,
            in PointerSample sample, float density)
        {
            float fallback = float.IsFinite(density) && density > 0 ? density : 1;
            if (sample.CoordinateKind == PointerCoordinateKind.Indirect)
            {
                float x = float.IsFinite(sample.MappedExtentX)
                    && sample.MappedExtentX > 0
                    ? delta.X / sample.MappedExtentX : delta.X / fallback;
                float y = float.IsFinite(sample.MappedExtentY)
                    && sample.MappedExtentY > 0
                    ? delta.Y / sample.MappedExtentY : delta.Y / fallback;
                return new Vector2(x, y);
            }
            float displayScale = sample.CoordinateKind == PointerCoordinateKind.Direct
                && float.IsFinite(sample.LogicalDisplayScale)
                && sample.LogicalDisplayScale > 0
                ? sample.LogicalDisplayScale : 1;
            return delta / (fallback * displayScale);
        }

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
