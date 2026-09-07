using System;
using MphRead.Formats;

namespace MphRead.Mods.Network
{
    public enum LagCompensationMode { None, HistoricalTrace, ProjectileCatchUp, HomingProjectileCatchUp }

    /// <summary>Actual Spawn results, after partial-charge interpolation and affinity selection.</summary>
    public readonly record struct BeamMechanics(BeamType Beam, BeamType BeamKind, bool Continuous,
        bool InstantArea, float Homing, float Speed, float Lifespan);

    public readonly record struct LagCompensationTime(uint Tick, uint RewindTicks, uint AllowedTicks, bool Clamped);

    /// <summary>Server timing limits. An input timestamp is only a hint within a measured RTT budget.</summary>
    public static class LagCompensationPolicy
    {
        public const uint MaxRewindTicks = 15; // 250 ms at the authoritative 60 Hz.
        public const uint InterpolationDelayTicks = 6; // The fixed 100 ms remote render delay.
        public const uint SchedulingAllowanceTicks = 2;
        public const uint MaxProjectileFastForwardTicks = 15;

        public static LagCompensationTime ResolveTick(uint currentTick, uint viewServerTick, double measuredRttMs)
        {
            // The view timestamp already includes presentation delay and any
            // bounded extrapolation/hold. Delay affects only the server-owned
            // plausibility budget; it must not be subtracted a second time.
            uint allowance = InterpolationDelayTicks;
            if (Double.IsFinite(measuredRttMs) && measuredRttMs > 0)
            {
                // Clamp before conversion: even a stalled or maliciously delayed
                // pong must not overflow an integer or expand the history window.
                double oneWayTicks = Math.Min(MaxRewindTicks, Math.Ceiling(measuredRttMs * 0.03));
                allowance = Math.Min(MaxRewindTicks,
                    (uint)oneWayTicks + InterpolationDelayTicks + SchedulingAllowanceTicks);
            }
            uint requested = unchecked(currentTick - viewServerTick);
            // Future and exactly half-range timestamps are not historical time.
            bool future = requested >= 0x80000000u;
            uint rewind = future ? 0 : Math.Min(requested, allowance);
            return new LagCompensationTime(unchecked(currentTick - rewind), rewind, allowance,
                future || requested != rewind);
        }

        public static LagCompensationMode GetMode(in BeamMechanics mechanics)
        {
            if ((uint)mechanics.Beam > 8 || mechanics.BeamKind != mechanics.Beam
                || mechanics.Continuous || mechanics.InstantArea
                || !Single.IsFinite(mechanics.Homing) || mechanics.Homing < 0
                || !Single.IsFinite(mechanics.Speed) || mechanics.Speed <= 0
                || !Single.IsFinite(mechanics.Lifespan) || mechanics.Lifespan <= 0)
                return LagCompensationMode.None;
            // These three multiplayer variants have independently verified
            // historical acquisition and steering. Continuous and area paths
            // were excluded above; other homing metadata remains opt-out.
            if (mechanics.Homing > 0)
                return mechanics.Beam is BeamType.PowerBeam or BeamType.VoltDriver or BeamType.Missile
                    ? LagCompensationMode.HomingProjectileCatchUp : LagCompensationMode.None;
            // Retail Imperialist crosses its entire 200-unit range in two
            // swept steps. Other ordinary moving beams keep normal physics.
            return mechanics.Beam == BeamType.Imperialist ? LagCompensationMode.HistoricalTrace
                : LagCompensationMode.ProjectileCatchUp;
        }
    }
}
