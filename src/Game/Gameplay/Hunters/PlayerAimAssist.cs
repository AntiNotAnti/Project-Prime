using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// Conservative local controller assistance. It changes only the local
    /// look delta before the existing aim update; projectile, collision and
    /// network formats remain untouched.
    /// </summary>
    internal sealed class PlayerAimAssist
    {
        internal const float AcquireConeDegrees = 5f;
        internal const float RetainConeDegrees = 6.5f;
        internal const float FrictionOuterConeDegrees = 6f;
        internal const float FrictionStrongConeDegrees = 2f;
        internal const float MaxSlowdown = 0.30f;
        internal const float RotationConeDegrees = 4.5f;
        internal const float MaxYawRate = 30f;
        internal const float MaxPitchRate = 22f;
        internal const float MinimumStickIntent = 0.08f;
        private const float MaximumDistance = 80f;

        private PlayerEntity? _retained;
        private float _acquisitionSeconds;

        public void Reset()
        {
            _retained = null;
            _acquisitionSeconds = 0;
        }

        public Vector2 Process(PlayerEntity owner, in LocalLookFrame frame,
            float deltaSeconds)
        {
            Vector2 input = frame.DeltaDegrees;
            if (!Eligible(owner, frame, deltaSeconds))
            {
                Reset();
                return input;
            }

            PlayerEntity? target = SelectTarget(owner, out Vector2 error,
                out float angularDistance);
            if (target == null)
            {
                _retained = null;
                _acquisitionSeconds = Math.Min(_acquisitionSeconds + deltaSeconds, 60);
                return input;
            }
            bool acquired = target != _retained;
            float acquisitionMilliseconds = acquired
                ? _acquisitionSeconds * 1000 : float.NaN;
            _retained = target;
            if (acquired) _acquisitionSeconds = 0;
            if (owner.Controls.InvertAimX) error.X *= -1;
            if (owner.Controls.InvertAimY) error.Y *= -1;

            Vector2 result = ApplyAssistance(input, error, angularDistance,
                frame.Magnitude, frame.AimAssistStrength, deltaSeconds);
            float friction = FrictionMultiplier(angularDistance,
                frame.AimAssistStrength);
            float rotation = (result - input * friction).Length;
            owner.ModScene.Services.ObserveAimAssist(owner, acquired,
                acquisitionMilliseconds, angularDistance, rotation, friction);
            return result;
        }

        internal static Vector2 ApplyAssistance(Vector2 input, Vector2 error,
            float angularDistance, float stickMagnitude, float strength,
            float deltaSeconds)
        {
            strength = float.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 0;
            float friction = FrictionMultiplier(angularDistance, strength);
            input *= friction;

            if (stickMagnitude >= MinimumStickIntent
                && angularDistance <= RotationConeDegrees)
            {
                float coneScale = 1f - angularDistance / RotationConeDegrees;
                float intentScale = Math.Clamp((stickMagnitude - MinimumStickIntent)
                    / (1f - MinimumStickIntent), 0, 1);
                float scale = strength * SmoothStep(coneScale)
                    * (0.35f + 0.65f * intentScale);
                input.X += ClampToward(error.X, MaxYawRate * deltaSeconds * scale);
                input.Y += ClampToward(error.Y, MaxPitchRate * deltaSeconds * scale);
            }
            return input;
        }

        internal static float FrictionMultiplier(float angularDistance, float strength)
        {
            strength = float.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 0;
            float proximity = 1f - Math.Clamp(
                (angularDistance - FrictionStrongConeDegrees)
                    / (FrictionOuterConeDegrees - FrictionStrongConeDegrees), 0, 1);
            return 1f - MaxSlowdown * strength * SmoothStep(proximity);
        }

        private static bool Eligible(PlayerEntity owner, in LocalLookFrame frame,
            float deltaSeconds)
        {
            return frame.AimAssistEnabled && frame.AimAssistStrength > 0
                && frame.Device == LookDeviceKind.GamepadStick
                && !frame.HasPrecisionContributor
                && (frame.Contributors & LookDeviceKind.GamepadGyro) == 0
                && frame.Magnitude >= MinimumStickIntent
                && float.IsFinite(deltaSeconds) && deltaSeconds > 0
                && owner.ModInPlay && !owner.IsBot
                && owner.SlotIndex == owner.ModScene.LocalPlayerSlot
                && !owner.ModScene.Services.DesiredSpectating
                && !owner.Flags1.TestFlag(PlayerFlags1.NoAimInput)
                && !owner.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
                && owner.ModScene.CameraSequences.Current == null
                && !owner.ModScene.FrameAdvance && !owner.ModScene.FrameAdvanceLastFrame;
        }

        private PlayerEntity? SelectTarget(PlayerEntity owner, out Vector2 error,
            out float angularDistance)
        {
            error = Vector2.Zero;
            angularDistance = float.MaxValue;
            Candidate best0 = default;
            Candidate best1 = default;
            Candidate best2 = default;
            int bestCount = 0;
            float maxDistanceSquared = MaximumDistance * MaximumDistance;
            for (int i = 0; i < owner.ModScene.Players.Count; i++)
            {
                PlayerEntity candidate = owner.ModScene.Players[i];
                if (candidate == owner || !candidate.ModInPlay
                    || !candidate.LoadFlags.TestFlag(LoadFlags.Spawned)
                    || !IsOpponent(owner.ModScene.Match.Rules.Teams,
                        owner.TeamIndex, candidate.TeamIndex))
                {
                    continue;
                }
                float distanceSquared = (candidate.ModAimTarget
                    - owner.CameraInfo.Position).LengthSquared;
                if (!(distanceSquared > 0) || distanceSquared > maxDistanceSquared)
                {
                    continue;
                }
                (float X, float Y) aim = owner.ModAimDeltaTowards(candidate.ModAimTarget);
                float angle = MathF.Sqrt(aim.X * aim.X + aim.Y * aim.Y);
                float cone = candidate == _retained
                    ? RetainConeDegrees : AcquireConeDegrees;
                if (!float.IsFinite(angle) || angle > cone)
                {
                    continue;
                }
                // Angular error dominates. Retention and distance settle only
                // close contests, preventing adjacent targets from flickering.
                float score = angle + MathF.Sqrt(distanceSquared) * 0.0025f
                    - (candidate == _retained ? 0.35f : 0);
                Insert(ref best0, ref best1, ref best2, ref bestCount,
                    new Candidate(candidate, new Vector2(aim.X, aim.Y), angle, score));
            }

            for (int i = 0; i < bestCount; i++)
            {
                Candidate candidate = i == 0 ? best0 : i == 1 ? best1 : best2;
                CollisionResult hit = default;
                if (!CollisionDetection.CheckBetweenPoints(owner.CameraInfo.Position,
                    candidate.Player!.ModAimTarget, TestFlags.None, owner.ModScene, ref hit))
                {
                    error = candidate.Error;
                    angularDistance = candidate.Angle;
                    return candidate.Player;
                }
            }
            return null;
        }

        private static void Insert(ref Candidate first, ref Candidate second,
            ref Candidate third, ref int count, in Candidate value)
        {
            if (count == 0 || value.Score < first.Score)
            {
                third = second;
                second = first;
                first = value;
            }
            else if (count == 1 || value.Score < second.Score)
            {
                third = second;
                second = value;
            }
            else if (count == 2 || value.Score < third.Score)
            {
                third = value;
            }
            if (count < 3) count++;
        }

        private static float ClampToward(float error, float maximum)
            => Math.Clamp(error, -maximum, maximum);

        internal static bool IsOpponent(bool teams, int ownerTeam, int candidateTeam)
            => !teams || candidateTeam != ownerTeam;

        private static float SmoothStep(float value)
            => value * value * (3f - 2f * value);

        private readonly record struct Candidate(PlayerEntity? Player,
            Vector2 Error, float Angle, float Score);
    }

    public partial class PlayerEntity
    {
        private readonly PlayerAimAssist _aimAssist = new();
        internal Scene ModScene => _scene;
    }
}
