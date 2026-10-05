using System;
using System.Globalization;
using OpenTK.Mathematics;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>
    /// Exact transforms for splitting one native 30 Hz state transition into two
    /// 60 Hz substeps. These helpers are deliberately math-only: production
    /// movement does not use them until the shadow harness proves the surrounding
    /// operation ordering and collision state are equivalent.
    /// </summary>
    internal static class NativeStepMath
    {
        internal static float HalfStepMultiplier(float nativeMultiplier)
        {
            if (!float.IsFinite(nativeMultiplier) || nativeMultiplier < 0)
                throw new ArgumentOutOfRangeException(nameof(nativeMultiplier));
            return MathF.Sqrt(nativeMultiplier);
        }

        internal static float HalfStepLerp(float nativeCoefficient)
        {
            if (!float.IsFinite(nativeCoefficient) || nativeCoefficient < 0 || nativeCoefficient > 1)
                throw new ArgumentOutOfRangeException(nameof(nativeCoefficient));
            return 1 - MathF.Sqrt(1 - nativeCoefficient);
        }

        /// <summary>
        /// If the native update is (v + impulse30) * multiplier30, returns the
        /// equal impulse to apply before each of two exact half-step multipliers.
        /// This is diagnostic/reference math only; real player traction has more
        /// state around it and must be shadow-validated before adopting it.
        /// </summary>
        internal static float CoupledHalfStepImpulse(float nativeImpulse, float nativeMultiplier)
        {
            if (!float.IsFinite(nativeImpulse))
                throw new ArgumentOutOfRangeException(nameof(nativeImpulse));
            float halfMultiplier = HalfStepMultiplier(nativeMultiplier);
            if (halfMultiplier == 0)
                return 0;
            return nativeImpulse * halfMultiplier / (1 + halfMultiplier);
        }

        internal static (float Position, float Velocity) NativeSemiImplicit(
            float position, float velocity, float nativeGravity)
        {
            velocity += nativeGravity;
            position += velocity;
            return (position, velocity);
        }

        /// <summary>
        /// Mirrors Project Prime's current pair:
        /// v += g/2; p += v/2; repeated twice.
        /// Kept here so the known gravity-position mismatch stays executable
        /// rather than existing only as prose in the audit document.
        /// </summary>
        internal static (float Position, float Velocity) CurrentGravityPair(
            float position, float velocity, float nativeGravity)
        {
            velocity += nativeGravity / 2;
            position += velocity / 2;
            velocity += nativeGravity / 2;
            position += velocity / 2;
            return (position, velocity);
        }
    }

    /// <summary>
    /// Normalized state compared at an equivalent native 30 Hz boundary.
    /// Contact fields are part of the contract now so the collision slice can
    /// populate them without changing the comparer/report format later.
    /// </summary>
    internal readonly record struct MovementBoundarySnapshot(
        ulong SimulationFrame,
        int Slot,
        int Hunter,
        bool AltForm,
        bool Grounded,
        bool Standing,
        bool SpireClimbing,
        int StandingEntityId,
        Vector3 Position,
        Vector3 Velocity,
        Vector3 Facing,
        float Gravity,
        int Slipperiness,
        Vector3 ContactNormal,
        float ContactPushout)
    {
        internal bool IsNativeBoundary => (SimulationFrame & 1UL) == 0;
    }

    internal readonly record struct MovementBoundaryDifference(
        ulong SimulationFrame,
        float PositionError,
        float VelocityError,
        float FacingError,
        float GravityError,
        float ContactNormalError,
        float ContactPushoutError,
        bool AltFormMismatch,
        bool GroundedMismatch,
        bool StandingMismatch,
        bool SpireClimbingMismatch,
        bool StandingEntityMismatch,
        bool SlipperinessMismatch)
    {
        internal bool DiscreteMatch => !AltFormMismatch && !GroundedMismatch
            && !StandingMismatch && !SpireClimbingMismatch
            && !StandingEntityMismatch && !SlipperinessMismatch;

        internal bool WithinTolerance(
            float positionTolerance = 1f / 4096f,
            float velocityTolerance = 1f / 4096f,
            float facingTolerance = 1f / 4096f,
            float scalarTolerance = 1f / 4096f)
        {
            return DiscreteMatch
                && PositionError <= positionTolerance
                && VelocityError <= velocityTolerance
                && FacingError <= facingTolerance
                && GravityError <= scalarTolerance
                && ContactNormalError <= scalarTolerance
                && ContactPushoutError <= scalarTolerance;
        }
    }

    internal static class MovementShadowComparer
    {
        internal static MovementBoundaryDifference Compare(
            in MovementBoundarySnapshot current,
            in MovementBoundarySnapshot reference)
        {
            if (current.SimulationFrame != reference.SimulationFrame)
                throw new InvalidOperationException("Movement shadow snapshots are from different simulation frames.");
            if (current.Slot != reference.Slot)
                throw new InvalidOperationException("Movement shadow snapshots are from different player slots.");
            if (current.Hunter != reference.Hunter)
                throw new InvalidOperationException("Movement shadow snapshots are from different hunters.");

            return new MovementBoundaryDifference(
                current.SimulationFrame,
                (current.Position - reference.Position).Length,
                (current.Velocity - reference.Velocity).Length,
                (current.Facing - reference.Facing).Length,
                MathF.Abs(current.Gravity - reference.Gravity),
                (current.ContactNormal - reference.ContactNormal).Length,
                MathF.Abs(current.ContactPushout - reference.ContactPushout),
                current.AltForm != reference.AltForm,
                current.Grounded != reference.Grounded,
                current.Standing != reference.Standing,
                current.SpireClimbing != reference.SpireClimbing,
                current.StandingEntityId != reference.StandingEntityId,
                current.Slipperiness != reference.Slipperiness);
        }

        internal static string Format(in MovementBoundaryDifference difference)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"frame={difference.SimulationFrame} pos={difference.PositionError:0.000000} "
                + $"vel={difference.VelocityError:0.000000} facing={difference.FacingError:0.000000} "
                + $"gravity={difference.GravityError:0.000000} contactN={difference.ContactNormalError:0.000000} "
                + $"pushout={difference.ContactPushoutError:0.000000} discrete={(difference.DiscreteMatch ? "MATCH" : "MISMATCH")}");
        }
    }

    /// <summary>
    /// Bounded summary used by the upcoming Samus/Spire shadow runner. It keeps
    /// the first meaningful divergence and maxima without retaining an unbounded
    /// per-frame trace in memory.
    /// </summary>
    internal sealed class MovementShadowAccumulator
    {
        internal int Compared { get; private set; }
        internal int OutsideTolerance { get; private set; }
        internal MovementBoundaryDifference? FirstDifference { get; private set; }
        internal float MaxPositionError { get; private set; }
        internal float MaxVelocityError { get; private set; }
        internal float MaxFacingError { get; private set; }

        internal MovementBoundaryDifference Observe(
            in MovementBoundarySnapshot current,
            in MovementBoundarySnapshot reference)
        {
            if (!current.IsNativeBoundary || !reference.IsNativeBoundary)
                throw new InvalidOperationException("Movement shadow comparison must occur at a native 30 Hz boundary.");

            MovementBoundaryDifference difference = MovementShadowComparer.Compare(current, reference);
            Compared++;
            MaxPositionError = MathF.Max(MaxPositionError, difference.PositionError);
            MaxVelocityError = MathF.Max(MaxVelocityError, difference.VelocityError);
            MaxFacingError = MathF.Max(MaxFacingError, difference.FacingError);
            if (!difference.WithinTolerance())
            {
                OutsideTolerance++;
                FirstDifference ??= difference;
            }
            return difference;
        }

        internal string Summary()
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"compared={Compared} outside={OutsideTolerance} "
                + $"maxPos={MaxPositionError:0.000000} maxVel={MaxVelocityError:0.000000} "
                + $"maxFacing={MaxFacingError:0.000000}");
        }
    }

    /// <summary>Content-free deterministic checks for the movement shadow contract.</summary>
    internal static class MovementShadowCheck
    {
        internal static int Run()
        {
            int checks = 0;
            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new InvalidOperationException("[movementshadowcheck] FAIL " + name);
                Console.WriteLine("[movementshadowcheck] PASS " + name);
                checks++;
            }
            void Near(float actual, float expected, float epsilon, string name)
                => Check(MathF.Abs(actual - expected) <= epsilon, name);

            try
            {
                foreach (float native in new[] { 0f, 0.679932f, 0.849854f, 0.879883f, 0.964844f, 0.979980f, 1f })
                {
                    float half = NativeStepMath.HalfStepMultiplier(native);
                    Near(half * half, native, 0.000002f, $"half multiplier squares to native {native:0.000000}");
                }

                foreach (float native in new[] { 0f, 0.1f, 0.3f, 0.8f, 1f })
                {
                    float half = NativeStepMath.HalfStepLerp(native);
                    float value = 0;
                    value += (1 - value) * half;
                    value += (1 - value) * half;
                    Near(value, native, 0.000002f, $"half lerp composes to native {native:0.000000}");
                }

                const float velocity = 1.25f;
                const float impulse = 0.17f;
                const float multiplier = 0.879883f;
                float halfMultiplier = NativeStepMath.HalfStepMultiplier(multiplier);
                float halfImpulse = NativeStepMath.CoupledHalfStepImpulse(impulse, multiplier);
                float splitVelocity = (velocity + halfImpulse) * halfMultiplier;
                splitVelocity = (splitVelocity + halfImpulse) * halfMultiplier;
                float nativeVelocity = (velocity + impulse) * multiplier;
                Near(splitVelocity, nativeVelocity, 0.000002f, "coupled impulse composes with damping");

                const float gravity = -245 / 4096f;
                var nativeGravity = NativeStepMath.NativeSemiImplicit(0, 0, gravity);
                var currentGravity = NativeStepMath.CurrentGravityPair(0, 0, gravity);
                Near(currentGravity.Velocity, nativeGravity.Velocity, 0.0000001f,
                    "current gravity pair preserves native boundary velocity");
                Near(currentGravity.Position - nativeGravity.Position, -gravity / 4, 0.0000001f,
                    "current gravity pair exposes the known quarter-gravity position error");

                var referenceStart = new NativeMovementReferenceState(
                    Vector3.Zero, new Vector3(0.2f, 0, 0), Vector3.UnitZ);
                var referenceInput = new NativeMovementReferenceInput(
                    new Vector3(0.2f, 0, 0), 0.3f, 0.9f,
                    true, -0.1f, 0.3f, Vector3.UnitX);
                NativeMovementReferenceState referenceStep =
                    NativeMovementReference.Step(referenceStart, referenceInput);
                Near(referenceStep.Velocity.X, 0.27f, 0.000002f,
                    "native reference applies speed cap before horizontal damping");
                Near(referenceStep.Velocity.Y, -0.1f, 0.000002f,
                    "native reference applies one native gravity increment");
                Near(referenceStep.Position.X, 0.27f, 0.000002f,
                    "native reference uses semi-implicit boundary position");
                Check(MathF.Abs(referenceStep.Facing.Length - 1) <= 0.000002f
                    && referenceStep.Facing.X > 0 && referenceStep.Facing.Z > 0,
                    "native reference normalizes facing convergence");

                var baseline = new MovementBoundarySnapshot(
                    120, 1, 0, false, true, true, false, -1,
                    new Vector3(1, 2, 3), new Vector3(0.1f, 0.2f, 0.3f), Vector3.UnitZ,
                    gravity, 0, Vector3.UnitY, 0.01f);
                MovementBoundaryDifference exact = MovementShadowComparer.Compare(baseline, baseline);
                Check(exact.DiscreteMatch && exact.WithinTolerance(), "identical boundary snapshots match");

                var inside = baseline with { Position = baseline.Position + new Vector3(1f / 8192f, 0, 0) };
                Check(MovementShadowComparer.Compare(inside, baseline).WithinTolerance(),
                    "1/8192 position delta stays inside normalized tolerance");

                var outside = baseline with { Position = baseline.Position + new Vector3(1f / 2048f, 0, 0) };
                Check(!MovementShadowComparer.Compare(outside, baseline).WithinTolerance(),
                    "1/2048 position delta exceeds normalized tolerance");

                var discrete = baseline with { SpireClimbing = true };
                Check(!MovementShadowComparer.Compare(discrete, baseline).DiscreteMatch,
                    "discrete climbing mismatch cannot hide behind float tolerance");

                var accumulator = new MovementShadowAccumulator();
                accumulator.Observe(baseline, baseline);
                accumulator.Observe(outside, baseline);
                Check(accumulator.Compared == 2 && accumulator.OutsideTolerance == 1
                    && accumulator.FirstDifference.HasValue,
                    "accumulator keeps bounded first-divergence evidence");

                Console.WriteLine($"[movementshadowcheck] {checks} checks passed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
