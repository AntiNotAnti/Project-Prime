using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>A processed gamepad look sample in degrees per second.</summary>
    public readonly struct GamepadLookSample
    {
        public GamepadLookSample(Vector2 angularVelocity, Vector2 direction,
            float magnitude, float responseMagnitude, float boostProgress)
        {
            AngularVelocity = angularVelocity;
            Direction = direction;
            Magnitude = magnitude;
            ResponseMagnitude = responseMagnitude;
            BoostProgress = boostProgress;
        }

        public Vector2 AngularVelocity { get; }
        public Vector2 Direction { get; }
        public float Magnitude { get; }
        public float ResponseMagnitude { get; }
        public float BoostProgress { get; }
        public bool IsActive => ResponseMagnitude > 0;
    }

    /// <summary>
    /// Radial, symmetric gamepad look curve with fixed-step outer-ring boost.
    /// <see cref="Evaluate"/> is side-effect free for native/render-rate
    /// sampling; <see cref="Advance"/> is the only method that advances boost
    /// timing.
    /// </summary>
    public struct GamepadLookProcessor
    {
        public const float DefaultInnerDeadzone = 0.10f;
        public const float DefaultOuterDeadzone = 0.02f;
        public const float DefaultExponent = 1.60f;
        public const float DefaultYawRate = 300;
        public const float DefaultPitchRate = 240;
        public const float DefaultOuterBoostStart = 0.95f;
        public const float DefaultOuterYawBoost = 150;
        public const float DefaultOuterPitchBoost = 80;
        public const float DefaultBoostDelaySeconds = 0.18f;
        public const float DefaultBoostRampSeconds = 0.12f;
        public const float DefaultAntiDeadzone = 0.02f;
        public const float DefaultSmoothingSeconds = 0.018f;

        private float _outerSeconds;
        private Vector2 _boostDirection;
        private Vector2 _smoothedVelocity;
        private bool _hasSmoothedVelocity;

        public GamepadLookProcessor()
            : this(DefaultInnerDeadzone, DefaultOuterDeadzone, DefaultExponent,
                DefaultYawRate, DefaultPitchRate, DefaultOuterBoostStart,
                DefaultOuterYawBoost, DefaultOuterPitchBoost,
                DefaultBoostDelaySeconds, DefaultBoostRampSeconds)
        {
        }

        public GamepadLookProcessor(float innerDeadzone = DefaultInnerDeadzone,
            float outerDeadzone = DefaultOuterDeadzone,
            float exponent = DefaultExponent,
            float yawRate = DefaultYawRate,
            float pitchRate = DefaultPitchRate,
            float outerBoostStart = DefaultOuterBoostStart,
            float outerYawBoost = DefaultOuterYawBoost,
            float outerPitchBoost = DefaultOuterPitchBoost,
            float boostDelaySeconds = DefaultBoostDelaySeconds,
            float boostRampSeconds = DefaultBoostRampSeconds,
            bool outerBoostEnabled = true,
            float horizontalSensitivity = 1,
            float verticalSensitivity = 1,
            bool invertY = false,
            float zoomMultiplier = 1,
            float antiDeadzone = DefaultAntiDeadzone,
            float smoothingSeconds = DefaultSmoothingSeconds)
        {
            _outerSeconds = 0;
            _boostDirection = Vector2.Zero;
            _smoothedVelocity = Vector2.Zero;
            _hasSmoothedVelocity = false;
            Configure(innerDeadzone, outerDeadzone, exponent, yawRate, pitchRate,
                outerBoostStart, outerYawBoost, outerPitchBoost, boostDelaySeconds,
                boostRampSeconds, outerBoostEnabled, horizontalSensitivity,
                verticalSensitivity, invertY, zoomMultiplier, antiDeadzone,
                smoothingSeconds);
        }

        public float InnerDeadzone { get; private set; }
        public float OuterDeadzone { get; private set; }
        public float Exponent { get; private set; }
        public float YawRate { get; private set; }
        public float PitchRate { get; private set; }
        public float OuterBoostStart { get; private set; }
        public float OuterYawBoost { get; private set; }
        public float OuterPitchBoost { get; private set; }
        public float BoostDelaySeconds { get; private set; }
        public float BoostRampSeconds { get; private set; }
        public bool OuterBoostEnabled { get; private set; }
        public float HorizontalSensitivity { get; private set; }
        public float VerticalSensitivity { get; private set; }
        public bool InvertY { get; private set; }
        public float ZoomMultiplier { get; private set; }
        public float AntiDeadzone { get; private set; }
        public float SmoothingSeconds { get; private set; }
        public readonly float OuterSeconds => _outerSeconds;

        /// <summary>
        /// Refresh tuning while retaining the sustained outer-ring timer.
        /// Settings are validated here as well as at the persistence boundary
        /// so callers cannot inject NaN or an invalid threshold ordering.
        /// </summary>
        public void Configure(float innerDeadzone, float outerDeadzone,
            float exponent, float yawRate, float pitchRate, float outerBoostStart,
            float outerYawBoost, float outerPitchBoost, float boostDelaySeconds,
            float boostRampSeconds, bool outerBoostEnabled,
            float horizontalSensitivity, float verticalSensitivity, bool invertY,
            float zoomMultiplier, float antiDeadzone = DefaultAntiDeadzone,
            float smoothingSeconds = DefaultSmoothingSeconds)
        {
            InnerDeadzone = SanitizeDeadzone(innerDeadzone, DefaultInnerDeadzone);
            OuterDeadzone = SanitizeDeadzone(outerDeadzone, DefaultOuterDeadzone,
                1 - InnerDeadzone);
            Exponent = SanitizePositive(exponent, DefaultExponent, 0.05f, 8);
            YawRate = SanitizePositive(yawRate, DefaultYawRate, 0, 2000);
            PitchRate = SanitizePositive(pitchRate, DefaultPitchRate, 0, 2000);
            OuterBoostStart = Math.Clamp(SanitizePositive(outerBoostStart,
                DefaultOuterBoostStart, 0, 1), 0, 1);
            OuterYawBoost = SanitizePositive(outerYawBoost, DefaultOuterYawBoost, 0, 2000);
            OuterPitchBoost = SanitizePositive(outerPitchBoost, DefaultOuterPitchBoost, 0, 2000);
            BoostDelaySeconds = SanitizePositive(boostDelaySeconds,
                DefaultBoostDelaySeconds, 0, 10);
            BoostRampSeconds = SanitizePositive(boostRampSeconds,
                DefaultBoostRampSeconds, 0.001f, 10);
            OuterBoostEnabled = outerBoostEnabled;
            HorizontalSensitivity = SanitizePositive(horizontalSensitivity, 1, 0.01f, 10);
            VerticalSensitivity = SanitizePositive(verticalSensitivity, 1, 0.01f, 10);
            InvertY = invertY;
            ZoomMultiplier = SanitizePositive(zoomMultiplier, 1, 0.01f, 10);
            AntiDeadzone = SanitizePositive(antiDeadzone, DefaultAntiDeadzone, 0, .5f);
            SmoothingSeconds = SanitizePositive(smoothingSeconds,
                DefaultSmoothingSeconds, 0, .25f);
        }

        /// <summary>Evaluate the current stick without changing boost timing.</summary>
        public readonly GamepadLookSample Evaluate(Vector2 raw, float zoomScale = 1,
            bool zoomed = false)
        {
            StickSample processed = StickProcessor.Process(raw, InnerDeadzone,
                OuterDeadzone, Exponent, AntiDeadzone);
            if (!processed.IsActive)
            {
                return default;
            }

            // Evaluate is intentionally side-effect free, but reversing the
            // direction must still invalidate the old direction's boost in
            // the preview. Advance() performs the actual timer reset on the
            // next fixed sample.
            float boost = IsMeaningfulReversal(processed.Direction)
                ? 0 : CurrentBoost(processed);
            GamepadLookSample target = MakeSample(processed, boost, zoomScale, zoomed);
            return WithVelocity(target, PreviewSmoothedVelocity(
                target.AngularVelocity, 1f / 60f, target.ResponseMagnitude));
        }

        /// <summary>
        /// Process one fixed simulation step.  Only this path changes the
        /// sustained outer-ring timer.
        /// </summary>
        public GamepadLookSample Advance(Vector2 raw, float deltaSeconds,
            float zoomScale = 1, bool zoomed = false)
        {
            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            {
                Reset();
                return default;
            }

            StickSample processed = StickProcessor.Process(raw, InnerDeadzone,
                OuterDeadzone, Exponent, AntiDeadzone);
            if (!processed.IsActive)
            {
                Reset();
                return default;
            }

            bool inOuter = processed.Magnitude >= OuterBoostStart;
            if (!inOuter)
            {
                ResetBoostState();
            }
            else
            {
                Vector2 direction = processed.Direction;
                if (IsMeaningfulReversal(direction))
                {
                    // A meaningful reversal must reacquire the boost delay.
                    _outerSeconds = 0;
                }
                _boostDirection = direction;
                _outerSeconds = Math.Clamp(_outerSeconds + deltaSeconds, 0, 60);
            }

            float boost = CurrentBoost(processed);
            GamepadLookSample target = MakeSample(processed, boost, zoomScale, zoomed);
            Vector2 velocity = AdvanceSmoothedVelocity(target.AngularVelocity,
                deltaSeconds, target.ResponseMagnitude);
            return WithVelocity(target, velocity);
        }

        /// <summary>Alias used by callers that model a processor as a stepper.</summary>
        public GamepadLookSample Process(Vector2 raw, float deltaSeconds,
            float zoomScale = 1, bool zoomed = false)
            => Advance(raw, deltaSeconds, zoomScale, zoomed);

        public void Reset()
        {
            ResetBoostState();
            _smoothedVelocity = Vector2.Zero;
            _hasSmoothedVelocity = false;
        }

        private void ResetBoostState()
        {
            _outerSeconds = 0;
            _boostDirection = Vector2.Zero;
        }

        private Vector2 AdvanceSmoothedVelocity(Vector2 target, float deltaSeconds,
            float responseMagnitude)
        {
            Vector2 value = PreviewSmoothedVelocity(target, deltaSeconds,
                responseMagnitude);
            _smoothedVelocity = value;
            _hasSmoothedVelocity = true;
            return value;
        }

        private readonly Vector2 PreviewSmoothedVelocity(Vector2 target,
            float deltaSeconds, float responseMagnitude)
        {
            if (SmoothingSeconds <= 0 || !_hasSmoothedVelocity
                || Vector2.Dot(target, _smoothedVelocity) < 0)
            {
                return target;
            }

            // Noise is most visible during small precision corrections. Fade
            // filtering out toward full deflection so fast turns retain their
            // authored acceleration and do not feel delayed.
            float speed = Math.Clamp(responseMagnitude, 0, 1);
            float timeConstant = SmoothingSeconds * (1 - .85f * speed * speed);
            if (timeConstant <= .0001f) return target;
            float seconds = float.IsFinite(deltaSeconds)
                ? Math.Clamp(deltaSeconds, 0, .25f) : 0;
            float alpha = 1 - MathF.Exp(-seconds / timeConstant);
            return _smoothedVelocity + (target - _smoothedVelocity) * alpha;
        }

        private static GamepadLookSample WithVelocity(GamepadLookSample sample,
            Vector2 velocity)
            => new(velocity, sample.Direction, sample.Magnitude,
                sample.ResponseMagnitude, sample.BoostProgress);

        private readonly float CurrentBoost(StickSample processed)
        {
            if (!OuterBoostEnabled || processed.Magnitude < OuterBoostStart
                || _outerSeconds <= BoostDelaySeconds)
            {
                return 0;
            }
            return Math.Clamp((_outerSeconds - BoostDelaySeconds) / BoostRampSeconds, 0, 1);
        }

        private readonly bool IsMeaningfulReversal(Vector2 direction)
            => _boostDirection.LengthSquared > 0
                && Vector2.Dot(direction, _boostDirection) < -0.25f;

        private readonly GamepadLookSample MakeSample(StickSample processed,
            float boostProgress, float zoomScale, bool zoomed)
        {
            // The caller supplies the existing FOV ratio. The user controller
            // zoom multiplier is an additional scale, but only while the
            // player is actually zoomed; applying it to the unzoomed sample
            // would silently change the normal look speed.
            float zoom = SanitizePositive(zoomScale, 1, 0.01f, 10)
                * (zoomed ? ZoomMultiplier : 1);
            float yaw = YawRate + OuterYawBoost * boostProgress;
            float pitch = PitchRate + OuterPitchBoost * boostProgress;
            float x = -processed.Direction.X * processed.ResponseMagnitude
                * yaw * HorizontalSensitivity * zoom;
            float y = processed.Direction.Y * processed.ResponseMagnitude
                * pitch * VerticalSensitivity * zoom * (InvertY ? -1 : 1);
            Vector2 velocity = new(x, y);
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            {
                return default;
            }
            return new GamepadLookSample(velocity, processed.Direction,
                processed.Magnitude, processed.ResponseMagnitude, boostProgress);
        }

        private static float SanitizeDeadzone(float value, float fallback,
            float maximum = 0.99f)
            => !float.IsFinite(value) ? fallback
                : Math.Clamp(value, 0, Math.Clamp(maximum, 0, 0.99f));

        private static float SanitizePositive(float value, float fallback,
            float minimum, float maximum)
            => !float.IsFinite(value) ? fallback : Math.Clamp(value, minimum, maximum);
    }
}
