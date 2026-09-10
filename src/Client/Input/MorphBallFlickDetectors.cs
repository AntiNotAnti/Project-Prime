using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Direction-only input primitives for the Samus Morph Ball boost.
    /// Directions use screen coordinates: X is right-positive and Y is
    /// down-positive. These types deliberately stop before gameplay/network
    /// intent so the authoritative core remains the only owner of boost
    /// activation and charge.
    /// </summary>
    public static class MorphBallFlickDirection
    {
        public static bool TryNormalize(Vector2 value, out Vector2 direction)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                direction = Vector2.Zero;
                return false;
            }
            float lengthSquared = value.LengthSquared;
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0)
            {
                direction = Vector2.Zero;
                return false;
            }
            float inverseLength = 1 / MathF.Sqrt(lengthSquared);
            direction = value * inverseLength;
            return float.IsFinite(direction.X) && float.IsFinite(direction.Y);
        }

        /// <summary>Convert the right stick's up-positive sample to screen Y.</summary>
        public static bool TryFromRightStick(Vector2 upPositive, out Vector2 direction)
            => TryNormalize(new Vector2(upPositive.X, -upPositive.Y), out direction);
    }

    /// <summary>
    /// Shared client-side eligibility gate for input gesture producers. The
    /// authoritative simulation still validates the queued intent and the
    /// Samus ability; this gate prevents stale or suppressed local input from
    /// accumulating in a detector between fixed ticks.
    /// </summary>
    public static class MorphBallBoostEligibility
    {
        public static bool IsEligible(PlayerEntity? player, bool enabled = true)
            => enabled && player != null
                && player.IsMainPlayer
                && player.Hunter == Hunter.Samus
                && player.LoadFlags.TestFlag(LoadFlags.Active)
                && player.Health > 0
                && player.IsAltForm
                && !player.IsMorphing
                && !player.IsUnmorphing
                && MphRead.Mods.ClientInputState.WindowFocused
                && !MphRead.Mods.ClientInputState.PauseOpen
                && !MphRead.Mods.Chat.ChatBox.Composing
                && !MphRead.Mods.SpectatorMode.IsSpectating;
    }

    /// <summary>
    /// Fixed-step right-stick flick detector. A deliberate neutral sample
    /// arms a single activation window; once fired, the detector remains
    /// latched until the stick returns to the release threshold.
    /// </summary>
    public struct MorphBallStickFlickDetector
    {
        public const float NeutralThreshold = .25f;
        public const float ActivationThreshold = .80f;
        public const float ReleaseThreshold = .35f;
        public const double ActivationWindowSeconds = .140;

        private bool _armed;
        private bool _latched;
        private double _neutralAt;
        private Vector2 _pendingDirection;

        public bool IsArmed => _armed;
        public bool IsLatched => _latched;

        /// <summary>
        /// Observe one immutable right-stick snapshot from a fixed simulation
        /// tick. Returns true only on the activation edge.
        /// </summary>
        public bool ObserveFixedTick(Vector2 upPositive, double timestamp,
            bool eligible = true)
        {
            if (!eligible || !float.IsFinite(upPositive.X)
                || !float.IsFinite(upPositive.Y) || !double.IsFinite(timestamp))
            {
                Reset();
                return false;
            }

            float magnitude = upPositive.Length;
            if (!float.IsFinite(magnitude))
            {
                Reset();
                return false;
            }

            if (_latched)
            {
                if (magnitude <= ReleaseThreshold)
                {
                    _latched = false;
                    _pendingDirection = Vector2.Zero;
                    if (magnitude <= NeutralThreshold)
                    {
                        _armed = true;
                        _neutralAt = timestamp;
                    }
                }
                return false;
            }

            if (magnitude <= NeutralThreshold)
            {
                // Refresh the observation while neutral so holding a stick at
                // rest does not make the next deliberate flick expire merely
                // because the player waited before moving.
                _armed = true;
                _neutralAt = timestamp;
                return false;
            }

            if (_armed && timestamp >= _neutralAt
                && timestamp - _neutralAt <= ActivationWindowSeconds
                && magnitude >= ActivationThreshold
                && MorphBallFlickDirection.TryFromRightStick(upPositive,
                    out Vector2 direction))
            {
                _latched = true;
                _armed = false;
                _pendingDirection = direction;
                return true;
            }

            if (!_armed || timestamp < _neutralAt
                || timestamp - _neutralAt > ActivationWindowSeconds)
            {
                _armed = false;
            }
            return false;
        }

        public bool TakeDirection(out Vector2 direction)
        {
            direction = _pendingDirection;
            _pendingDirection = Vector2.Zero;
            return direction != Vector2.Zero;
        }

        public void Reset()
        {
            _armed = false;
            _latched = false;
            _neutralAt = 0;
            _pendingDirection = Vector2.Zero;
        }
    }

    /// <summary>
    /// Raw relative mouse flick detector. Samples are retained in a bounded
    /// rolling window, so high-refresh input does not change the gesture's
    /// timing semantics or allocate on the input path.
    /// </summary>
    public sealed class MorphBallMouseFlickDetector
    {
        public const float DefaultDistance = 50;
        public const double WindowSeconds = .120;
        public const double QuietRearmSeconds = .120;

        private const int Capacity = 128;
        private readonly float[] _x = new float[Capacity];
        private readonly float[] _y = new float[Capacity];
        private readonly double[] _time = new double[Capacity];
        private int _count;
        private int _oldest;
        private Vector2 _sum;
        private double _lastSample;
        private bool _hasSample;
        private bool _latched;
        private Vector2 _pendingDirection;
        private float _distance;

        public MorphBallMouseFlickDetector(float distance = DefaultDistance)
        {
            _distance = SanitizeDistance(distance);
        }

        public float Distance => _distance;

        public bool Observe(Vector2 rawDelta, double timestamp, bool eligible = true)
        {
            if (!eligible || !float.IsFinite(rawDelta.X)
                || !float.IsFinite(rawDelta.Y) || !double.IsFinite(timestamp))
            {
                Reset();
                return false;
            }
            if (_hasSample && timestamp < _lastSample)
            {
                Reset();
            }
            if (_hasSample && timestamp - _lastSample >= QuietRearmSeconds)
            {
                ClearWindow();
                _latched = false;
            }
            _lastSample = timestamp;
            _hasSample = true;
            if (rawDelta.LengthSquared <= 0)
            {
                return false;
            }

            Add(rawDelta, timestamp);
            RemoveExpired(timestamp - WindowSeconds);
            if (_latched || _sum.Length < _distance)
            {
                return false;
            }
            if (!MorphBallFlickDirection.TryNormalize(_sum,
                out Vector2 direction))
            {
                return false;
            }
            _latched = true;
            _pendingDirection = direction;
            return true;
        }

        public bool TakeDirection(out Vector2 direction)
        {
            direction = _pendingDirection;
            _pendingDirection = Vector2.Zero;
            return direction != Vector2.Zero;
        }

        public void Reset()
        {
            ClearWindow();
            _lastSample = 0;
            _hasSample = false;
            _latched = false;
            _pendingDirection = Vector2.Zero;
        }

        private void Add(Vector2 delta, double timestamp)
        {
            if (_count == Capacity)
            {
                _sum.X -= _x[_oldest];
                _sum.Y -= _y[_oldest];
                _oldest = (_oldest + 1) % Capacity;
                _count--;
            }
            int index = (_oldest + _count) % Capacity;
            _x[index] = delta.X;
            _y[index] = delta.Y;
            _time[index] = timestamp;
            _sum += delta;
            _count++;
        }

        private void RemoveExpired(double oldestAllowed)
        {
            while (_count > 0 && _time[_oldest] < oldestAllowed)
            {
                _sum.X -= _x[_oldest];
                _sum.Y -= _y[_oldest];
                _oldest = (_oldest + 1) % Capacity;
                _count--;
            }
        }

        private void ClearWindow()
        {
            _count = 0;
            _oldest = 0;
            _sum = Vector2.Zero;
        }

        private static float SanitizeDistance(float value)
            => float.IsFinite(value) ? Math.Clamp(value, 1, 10000)
                : DefaultDistance;
    }

    /// <summary>
    /// Pointer-contact flick detector shared by touch and stylus gesture
    /// recognition. One contact can emit at most one direction.
    /// </summary>
    public sealed class MorphBallPointerFlickDetector
    {
        public const float FlickDistanceDp = 50;
        public const long FlickWindowMs = 120;
        // 128 samples cover 1 kHz pointer delivery for the complete gesture
        // window while remaining a fixed, allocation-free upper bound.
        private const int Capacity = 128;

        private readonly float[] _x = new float[Capacity];
        private readonly float[] _y = new float[Capacity];
        private readonly long[] _time = new long[Capacity];
        private int _count;
        private int _newest = -1;
        private bool _contact;
        private bool _fired;
        private long _lastTimestamp;
        private bool _hasTimestamp;
        private float _density = 1;
        private Vector2 _pendingDirection;

        public float Density
        {
            get => _density;
            set => _density = float.IsFinite(value) && value > 0 ? value : 1;
        }

        public void Begin(float x, float y, long timestamp)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                Reset();
                return;
            }
            _contact = true;
            _fired = false;
            _pendingDirection = Vector2.Zero;
            ResetSamples();
            _lastTimestamp = timestamp;
            _hasTimestamp = true;
            Add(x, y, timestamp);
        }

        public bool Move(float x, float y, long timestamp,
            out Vector2 direction)
        {
            direction = Vector2.Zero;
            if (!_contact || _fired) return false;
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                Reset();
                return false;
            }
            if (_hasTimestamp && timestamp < _lastTimestamp)
            {
                // A reordered platform event must not join samples from a
                // later event. Keep the contact alive, but re-arm from this
                // sample as a new gesture history.
                ResetSamples();
                _pendingDirection = Vector2.Zero;
                _fired = false;
            }
            _lastTimestamp = timestamp;
            _hasTimestamp = true;
            Add(x, y, timestamp);
            (float distance, Vector2 delta) = Displacement(timestamp);
            if (distance <= FlickDistanceDp * _density) return false;
            if (!MorphBallFlickDirection.TryNormalize(delta, out direction))
            {
                return false;
            }
            _fired = true;
            _pendingDirection = direction;
            return true;
        }

        public void End()
        {
            _contact = false;
            _hasTimestamp = false;
            ResetSamples();
        }

        public bool TakeDirection(out Vector2 direction)
        {
            direction = _pendingDirection;
            _pendingDirection = Vector2.Zero;
            return direction != Vector2.Zero;
        }

        public void Reset()
        {
            _contact = false;
            _fired = false;
            _pendingDirection = Vector2.Zero;
            _lastTimestamp = 0;
            _hasTimestamp = false;
            ResetSamples();
        }

        private void Add(float x, float y, long timestamp)
        {
            _newest = (_newest + 1) % Capacity;
            _x[_newest] = x;
            _y[_newest] = y;
            _time[_newest] = timestamp;
            if (_count < Capacity) _count++;
        }

        private (float Distance, Vector2 Delta) Displacement(long timestamp)
        {
            if (_count < 2) return default;
            float newestX = _x[_newest];
            float newestY = _y[_newest];
            float best = 0;
            Vector2 bestDelta = Vector2.Zero;
            for (int i = 1; i < _count; i++)
            {
                int index = (_newest - i + Capacity) % Capacity;
                if (i > 1 && timestamp - _time[index] > FlickWindowMs) break;
                Vector2 delta = new(newestX - _x[index], newestY - _y[index]);
                float square = delta.LengthSquared;
                if (square > best)
                {
                    best = square;
                    bestDelta = delta;
                }
            }
            return (MathF.Sqrt(best), bestDelta);
        }

        private void ResetSamples()
        {
            _count = 0;
            _newest = -1;
        }
    }
}
