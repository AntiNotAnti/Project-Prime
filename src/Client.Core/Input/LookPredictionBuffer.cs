using System;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>One consumed event batch, including all deliberate sources.</summary>
    public readonly struct LookPredictionFrame
    {
        public LookPredictionFrame(Vector2 deltaDegrees, LookDeviceKind contributors,
            Vector2 precisionDeltaDegrees = default,
            Vector2 mouseDeltaDegrees = default,
            Vector2 touchDeltaDegrees = default,
            Vector2 stylusDeltaDegrees = default,
            Vector2 rawMouseDelta = default)
        {
            DeltaDegrees = deltaDegrees;
            Contributors = contributors;
            PrecisionDeltaDegrees = precisionDeltaDegrees;
            MouseDeltaDegrees = mouseDeltaDegrees;
            TouchDeltaDegrees = touchDeltaDegrees;
            StylusDeltaDegrees = stylusDeltaDegrees;
            RawMouseDelta = rawMouseDelta;
        }

        public Vector2 DeltaDegrees { get; }
        public LookDeviceKind Contributors { get; }
        /// <summary>Relative-event contribution, before any stateful stick contribution.</summary>
        public Vector2 PrecisionDeltaDegrees { get; }
        public Vector2 MouseDeltaDegrees { get; }
        public Vector2 TouchDeltaDegrees { get; }
        public Vector2 StylusDeltaDegrees { get; }
        public Vector2 RawMouseDelta { get; }
        public bool HasPrecisionContributor
            => (Contributors & (LookDeviceKind.Mouse | LookDeviceKind.Touch
                | LookDeviceKind.Stylus)) != 0;
    }

    /// <summary>
    /// Generic render-side look prediction.
    ///
    /// Relative devices add an event delta and are consumed once by the fixed
    /// step. Stateful devices publish angular velocity; render peeks project
    /// that velocity only over time not yet represented by simulation. A peek
    /// never mutates pending events or gameplay state.
    /// </summary>
    public class LookPredictionBuffer
    {
        public const float MaxPendingDegrees = 16384;
        public const double MaxAgeSeconds = 0.25;

        private readonly object _gate = new();
        private Vector2 _pending;
        private readonly Vector2[] _pendingByDevice = new Vector2[5];
        private readonly Vector2[] _rawByDevice = new Vector2[5];
        private readonly bool[] _hasEventByDevice = new bool[5];
        private readonly LookDeviceKind[] _pendingContributorsByDevice = new LookDeviceKind[5];
        private Vector2 _angularVelocity;
        private LookDeviceKind _pendingContributors;
        private double _lastEvent;
        private double _lastVelocity;
        private double _simulationTime;
        private bool _hasEvent;
        private bool _hasVelocity;
        private bool _hasSimulationTime;
        private long _epoch;

        private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public long Epoch
        {
            get { lock (_gate) return _epoch; }
        }

        public Vector2 AngularVelocity
        {
            get { lock (_gate) return _angularVelocity; }
        }

        public LookDeviceKind PendingContributors
        {
            get { lock (_gate) return _pendingContributors; }
        }

        public void Add(float x, float y, double? seconds = null)
            => Add(new Vector2(x, y), LookDeviceKind.Mouse, seconds);

        public void Add(Vector2 deltaDegrees, LookDeviceKind device,
            double? seconds = null)
            => AddContribution(deltaDegrees, device, device,
                device == LookDeviceKind.Mouse ? deltaDegrees : Vector2.Zero,
                seconds);

        /// <summary>
        /// Add a relative event while retaining its owning source separately
        /// from contributor metadata. The separation lets a stylus cancel
        /// remove only stylus motion from a mixed frame.
        /// </summary>
        private void AddContribution(Vector2 deltaDegrees, LookDeviceKind device,
            LookDeviceKind contributors, Vector2 rawDelta, double? seconds)
        {
            if (!float.IsFinite(deltaDegrees.X) || !float.IsFinite(deltaDegrees.Y)
                || !IsSingleDevice(device))
            {
                return;
            }
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                int index = DeviceIndex(device);
                _pendingByDevice[index].X = Math.Clamp(
                    _pendingByDevice[index].X + deltaDegrees.X,
                    -MaxPendingDegrees, MaxPendingDegrees);
                _pendingByDevice[index].Y = Math.Clamp(
                    _pendingByDevice[index].Y + deltaDegrees.Y,
                    -MaxPendingDegrees, MaxPendingDegrees);
                if (device == LookDeviceKind.Mouse)
                {
                    _rawByDevice[index].X = Math.Clamp(
                        _rawByDevice[index].X + rawDelta.X,
                        -MaxPendingDegrees, MaxPendingDegrees);
                    _rawByDevice[index].Y = Math.Clamp(
                        _rawByDevice[index].Y + rawDelta.Y,
                        -MaxPendingDegrees, MaxPendingDegrees);
                }
                _pending = SumPending();
                _lastEvent = now;
                _hasEvent = true;
                _hasEventByDevice[index] = true;
                _pendingContributorsByDevice[index] |= contributors;
                _pendingContributors = SumContributors();
            }
        }

        /// <summary>Add a frame while retaining its source metadata.</summary>
        public void Add(in LocalLookFrame frame, double? seconds = null)
        {
            if (!frame.IsFinite || !IsSingleDevice(frame.Device))
            {
                return;
            }
            AddContribution(frame.DeltaDegrees, frame.Device,
                frame.Contributors == LookDeviceKind.None ? frame.Device : frame.Contributors,
                frame.Device == LookDeviceKind.Mouse
                    ? frame.RawMouseDelta == Vector2.Zero
                        ? frame.RawDirection : frame.RawMouseDelta
                    : Vector2.Zero,
                seconds);
        }

        /// <summary>
        /// Remove pending events for the specified source only. The caller
        /// supplies its ownership/epoch gate; this method remains atomic with
        /// respect to other prediction-buffer readers.
        /// </summary>
        public void Remove(LookDeviceKind device)
        {
            lock (_gate)
            {
                bool removed = false;
                foreach (LookDeviceKind source in Sources)
                {
                    if ((device & source) == 0) continue;
                    int index = DeviceIndex(source);
                    _pendingByDevice[index] = Vector2.Zero;
                    _rawByDevice[index] = Vector2.Zero;
                    _hasEventByDevice[index] = false;
                    _pendingContributorsByDevice[index] = LookDeviceKind.None;
                    removed = true;
                }
                if (!removed) return;
                _pending = SumPending();
                _pendingContributors = SumContributors();
                _hasEvent = false;
                for (int i = 0; i < _hasEventByDevice.Length; i++)
                {
                    if (_hasEventByDevice[i])
                    {
                        _hasEvent = true;
                        break;
                    }
                }
                if (!_hasEvent) _lastEvent = 0;
            }
        }

        /// <summary>Publish stateful angular velocity without creating an event.</summary>
        public void SetAngularVelocity(Vector2 velocity, double? seconds = null)
        {
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            {
                lock (_gate)
                {
                    _angularVelocity = Vector2.Zero;
                    _hasVelocity = false;
                }
                return;
            }
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                bool wasVelocity = _hasVelocity;
                _angularVelocity = velocity;
                _lastVelocity = now;
                _hasVelocity = velocity != Vector2.Zero;
                if (!wasVelocity || !_hasSimulationTime)
                {
                    _simulationTime = now;
                    _hasSimulationTime = true;
                }
            }
        }

        public void SetStatefulVelocity(Vector2 velocity, double? seconds = null)
            => SetAngularVelocity(velocity, seconds);

        /// <summary>
        /// Read pending events plus the unsimulated stateful interval.  This is
        /// intentionally non-consuming; callers may read it at every refresh.
        /// </summary>
        public Vector2 PeekForRender(double? seconds = null)
            => PeekForRender(seconds, simulationActive: false);

        public Vector2 PeekForRender(double? seconds, bool simulationActive,
            double? simulationRemainderSeconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                Vector2 value = _pending;
                if (simulationActive && _hasVelocity && _hasSimulationTime)
                {
                    double unsimulated = UnsimulatedSeconds(seconds, now,
                        simulationRemainderSeconds);
                    value += _angularVelocity * (float)unsimulated;
                }
                return value;
            }
        }

        /// <summary>Read pending precision contributions without consuming them.</summary>
        public (Vector2 Mouse, Vector2 Touch, Vector2 Stylus) PeekPrecisionForRender(
            double? seconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                return (
                    _pendingByDevice[DeviceIndex(LookDeviceKind.Mouse)],
                    _pendingByDevice[DeviceIndex(LookDeviceKind.Touch)],
                    _pendingByDevice[DeviceIndex(LookDeviceKind.Stylus)]);
            }
        }

        /// <summary>Read pending raw mouse pixels without consuming them.</summary>
        public Vector2 PeekRawMouseForRender(double? seconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                return _rawByDevice[DeviceIndex(LookDeviceKind.Mouse)];
            }
        }

        /// <summary>
        /// Read only the stateful portion of the prediction. Relative events
        /// have a legacy gameplay/render consumer on desktop, so this view
        /// lets that path compose controller velocity without applying the
        /// same mouse event twice. Android and future hosts can continue to
        /// use <see cref="PeekForRender(double?, bool)"/> for their event-only
        /// sources.
        /// </summary>
        public Vector2 PeekStatefulForRender(double? seconds = null)
            => PeekStatefulForRender(seconds, simulationActive: false);

        public Vector2 PeekStatefulForRender(double? seconds, bool simulationActive,
            double? simulationRemainderSeconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                if (!simulationActive || !_hasVelocity || !_hasSimulationTime)
                {
                    return Vector2.Zero;
                }
                double unsimulated = UnsimulatedSeconds(seconds, now,
                    simulationRemainderSeconds);
                return _angularVelocity * (float)unsimulated;
            }
        }

        /// <summary>Compatibility spelling used by the original mouse path.</summary>
        public Vector2 Peek(double? seconds = null) => PeekForRender(seconds, false);

        /// <summary>
        /// Consume event deltas exactly once and mark the current simulation
        /// boundary. Stateful velocity remains available for render peeks.
        /// </summary>
        public Vector2 ConsumeForSimulation(double? seconds = null)
            => ConsumeFrameForSimulation(seconds).DeltaDegrees;

        public LookPredictionFrame ConsumeFrameForSimulation(double? seconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                Vector2 mouse = _pendingByDevice[DeviceIndex(LookDeviceKind.Mouse)];
                Vector2 touch = _pendingByDevice[DeviceIndex(LookDeviceKind.Touch)];
                Vector2 stylus = _pendingByDevice[DeviceIndex(LookDeviceKind.Stylus)];
                Vector2 rawMouse = _rawByDevice[DeviceIndex(LookDeviceKind.Mouse)];
                LookPredictionFrame frame = new(_pending, _pendingContributors,
                    mouse + touch + stylus, mouse, touch, stylus, rawMouse);
                _pending = Vector2.Zero;
                _pendingContributors = LookDeviceKind.None;
                _hasEvent = false;
                Array.Clear(_pendingByDevice);
                Array.Clear(_rawByDevice);
                Array.Clear(_hasEventByDevice);
                Array.Clear(_pendingContributorsByDevice);
                _simulationTime = now;
                _hasSimulationTime = true;
                return frame;
            }
        }

        /// <summary>
        /// Explicit fixed-step extraction for callers that want the current
        /// stateful velocity included in the returned simulation delta.
        /// </summary>
        public Vector2 ConsumeForSimulation(double simulationDeltaSeconds,
            bool includeStatefulVelocity, double? seconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                Vector2 value = _pending;
                if (includeStatefulVelocity && _hasVelocity
                    && float.IsFinite((float)simulationDeltaSeconds)
                    && simulationDeltaSeconds >= 0)
                {
                    value += _angularVelocity * (float)simulationDeltaSeconds;
                }
                _pending = Vector2.Zero;
                _pendingContributors = LookDeviceKind.None;
                _hasEvent = false;
                Array.Clear(_pendingByDevice);
                Array.Clear(_rawByDevice);
                Array.Clear(_hasEventByDevice);
                Array.Clear(_pendingContributorsByDevice);
                _simulationTime = now;
                _hasSimulationTime = true;
                return value;
            }
        }

        /// <summary>Move the simulation boundary without consuming an event.</summary>
        public void MarkSimulationStep(double? seconds = null)
        {
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                _simulationTime = now;
                _hasSimulationTime = true;
            }
        }

        public Vector2 Consume(double? seconds = null) => ConsumeForSimulation(seconds);

        /// <summary>
        /// Clear only the stateful contribution. Relative events and their
        /// contributor metadata remain available to the fixed-step consumer.
        /// This is used when a controller disconnects while a mouse, touch,
        /// or stylus event is still pending.
        /// </summary>
        public void ClearStatefulVelocity()
        {
            lock (_gate)
            {
                _angularVelocity = Vector2.Zero;
                _lastVelocity = 0;
                _hasVelocity = false;
                _hasSimulationTime = false;
                _simulationTime = 0;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _pending = Vector2.Zero;
                Array.Clear(_pendingByDevice);
                Array.Clear(_rawByDevice);
                Array.Clear(_hasEventByDevice);
                _angularVelocity = Vector2.Zero;
                _pendingContributors = LookDeviceKind.None;
                _lastEvent = 0;
                _lastVelocity = 0;
                _simulationTime = 0;
                _hasEvent = false;
                _hasVelocity = false;
                _hasSimulationTime = false;
                Array.Clear(_pendingContributorsByDevice);
                _epoch++;
            }
        }

        private void Expire(double now)
        {
            if (_hasEvent && now - _lastEvent > MaxAgeSeconds)
            {
                _pending = Vector2.Zero;
                _pendingContributors = LookDeviceKind.None;
                _hasEvent = false;
                Array.Clear(_pendingByDevice);
                Array.Clear(_rawByDevice);
                Array.Clear(_hasEventByDevice);
                Array.Clear(_pendingContributorsByDevice);
            }
            if (_hasVelocity && now - _lastVelocity > MaxAgeSeconds)
            {
                _angularVelocity = Vector2.Zero;
                _hasVelocity = false;
            }
        }

        private double UnsimulatedSeconds(double? seconds, double now,
            double? simulationRemainderSeconds)
        {
            double elapsed = seconds.HasValue
                ? now - _simulationTime
                : simulationRemainderSeconds ?? 0;
            return double.IsFinite(elapsed)
                ? Math.Clamp(elapsed, 0, MaxAgeSeconds) : 0;
        }

        private Vector2 SumPending()
        {
            Vector2 sum = Vector2.Zero;
            for (int i = 0; i < _pendingByDevice.Length; i++) sum += _pendingByDevice[i];
            return new Vector2(Math.Clamp(sum.X, -MaxPendingDegrees, MaxPendingDegrees),
                Math.Clamp(sum.Y, -MaxPendingDegrees, MaxPendingDegrees));
        }

        private LookDeviceKind SumContributors()
        {
            LookDeviceKind result = LookDeviceKind.None;
            for (int i = 0; i < _pendingContributorsByDevice.Length; i++)
                result |= _pendingContributorsByDevice[i];
            return result;
        }

        private static readonly LookDeviceKind[] Sources =
        {
            LookDeviceKind.Mouse,
            LookDeviceKind.GamepadStick,
            LookDeviceKind.GamepadGyro,
            LookDeviceKind.Touch,
            LookDeviceKind.Stylus
        };

        private static int DeviceIndex(LookDeviceKind device)
            => device switch
            {
                LookDeviceKind.Mouse => 0,
                LookDeviceKind.GamepadStick => 1,
                LookDeviceKind.GamepadGyro => 2,
                LookDeviceKind.Touch => 3,
                LookDeviceKind.Stylus => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(device))
            };

        private static bool IsSingleDevice(LookDeviceKind device)
        {
            int value = (int)device;
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
