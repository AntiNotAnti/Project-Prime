using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal enum ReplayDirectorShotKind { KillerPov, VictimPov, Shoulder, Chase, Elevated, Fixed, Orbit }
internal readonly record struct ReplayDirectorPov(Vector3 Position, Vector3 Facing);
internal readonly record struct ReplayDirectorShot(ReplayDirectorShotKind Kind, Vector3 Position,
    Vector3 Facing, float FovDegrees, float Score);

/// <summary>Presentation proposals only. The caller owns identity validity and world collision queries.</summary>
internal static class ReplayDirectorShots
{
    internal const int MaximumCandidates = 7;
    internal const float MaximumDegreesPerSecond = 120;
    private static readonly string[] Descriptions = { "Canonical killer POV", "Verified victim POV", "Shoulder · clearance verified",
        "Chase · clearance verified", "Elevated · clearance verified", "Authored fixed camera · clearance verified", "Orbit · clearance verified" };
    internal static string Describe(ReplayDirectorShotKind kind) => Descriptions[(int)kind];

    // isClear must test the supplied segment against world geometry. A fixed
    // arena camera and victim POV are offered only when supplied by the caller.
    internal static int Generate(Span<ReplayDirectorShot> output, Vector3 actorPosition,
        Vector3 actorFacing, Vector3 target, double canonicalFrame, ReplayCameraMode style,
        float fovDegrees, ReplayDirectorShotKind? previous, Func<Vector3, Vector3, bool> isClear,
        ReplayDirectorPov? victim = null, Vector3? fixedArenaPosition = null, bool preferVictim = false)
    {
        ArgumentNullException.ThrowIfNull(isClear);
        if (output.Length < MaximumCandidates) throw new ArgumentException("Seven candidate slots required.", nameof(output));
        if (!Finite(actorPosition) || !Finite(target) || !double.IsFinite(canonicalFrame)
            || !Direction(actorFacing, out actorFacing)) return 0;
        float fov = float.IsFinite(fovDegrees) ? Math.Clamp(fovDegrees, 40, 120) : 80;
        Vector3 horizontal = new(actorFacing.X, 0, actorFacing.Z);
        if (!Direction(horizontal, out horizontal)) horizontal = -Vector3.UnitZ;
        Vector3 right = Vector3.Cross(horizontal, Vector3.UnitY).Normalized();
        int count = 0;
        Add(output, ref count, ReplayDirectorShotKind.KillerPov, actorPosition, actorFacing, fov,
            style == ReplayCameraMode.FirstPerson ? 100 : 55, previous);
        if (victim is { } pov && Finite(pov.Position) && Direction(pov.Facing, out var victimFacing))
            Add(output, ref count, ReplayDirectorShotKind.VictimPov, pov.Position, victimFacing, fov, preferVictim ? 112 : 50, previous);
        TryAdd(output, ref count, ReplayDirectorShotKind.Shoulder, target - horizontal * 2 + right * .8f,
            target, fov, style == ReplayCameraMode.Chase ? 84 : 68, previous, isClear);
        TryAdd(output, ref count, ReplayDirectorShotKind.Chase, target - horizontal * 4,
            target, fov, style == ReplayCameraMode.Chase ? 90 : 70, previous, isClear);
        TryAdd(output, ref count, ReplayDirectorShotKind.Elevated, target - horizontal * 5 + Vector3.UnitY * 5,
            target, fov, style == ReplayCameraMode.Overview ? 90 : 65, previous, isClear);
        if (fixedArenaPosition is { } fixedPosition)
            TryAdd(output, ref count, ReplayDirectorShotKind.Fixed, fixedPosition, target, fov,
                style == ReplayCameraMode.Overview ? 86 : 60, previous, isClear);
        double angle = canonicalFrame / 60 * .4;
        var orbit = new Vector3((float)Math.Sin(angle), 0, (float)Math.Cos(angle));
        TryAdd(output, ref count, ReplayDirectorShotKind.Orbit, target - orbit * 4 + Vector3.UnitY,
            target, fov, style == ReplayCameraMode.Orbit ? 90 : 62, previous, isClear);
        return count;
    }

    private static void Add(Span<ReplayDirectorShot> output, ref int count, ReplayDirectorShotKind kind,
        Vector3 position, Vector3 facing, float fov, float score, ReplayDirectorShotKind? previous)
        => output[count++] = new(kind, position, facing, fov, score + (previous == kind ? 8 : 0));

    private static void TryAdd(Span<ReplayDirectorShot> output, ref int count, ReplayDirectorShotKind kind,
        Vector3 position, Vector3 target, float fov, float score, ReplayDirectorShotKind? previous,
        Func<Vector3, Vector3, bool> isClear)
    {
        if (!IsClear(position, target, isClear)) return;
        Vector3 facing = (target - position).Normalized();
        Add(output, ref count, kind, position, facing, fov, score, previous);
    }

    internal static bool IsClear(Vector3 position, Vector3 target, Func<Vector3, Vector3, bool> isClear)
    {
        if (!Finite(position) || !Finite(target) || !Direction(target - position, out var facing)) return false;
        Vector3 right = Vector3.Cross(facing, Vector3.UnitY);
        if (!Direction(right, out right)) return false;
        Vector3 up = Vector3.Cross(right, facing).Normalized();
        return isClear(target, position)
            && Clearance(isClear, target, position, right * .25f)
            && Clearance(isClear, target, position, -right * .25f)
            && Clearance(isClear, target, position, up * .25f)
            && Clearance(isClear, target, position, -up * .25f);
    }

    internal static bool SweepClear(Vector3 from, Vector3 to, Func<Vector3, Vector3, bool> isClear)
    {
        if (!Finite(from) || !Finite(to)) return false;
        Vector3 direction = to - from;
        if (!Direction(direction, out direction)) direction = -Vector3.UnitZ;
        Vector3 right = Vector3.Cross(direction, Vector3.UnitY);
        if (!Direction(right, out right)) right = Vector3.UnitX;
        Vector3 up = Vector3.Cross(right, direction).Normalized();
        return isClear(from, to)
            && SweepOffset(isClear, from, to, right * .25f)
            && SweepOffset(isClear, from, to, -right * .25f)
            && SweepOffset(isClear, from, to, up * .25f)
            && SweepOffset(isClear, from, to, -up * .25f);
    }
    private static bool SweepOffset(Func<Vector3, Vector3, bool> clear, Vector3 from, Vector3 to, Vector3 offset)
        => clear(from + offset, to + offset) && clear(from, from + offset) && clear(to, to + offset);

    private static bool Clearance(Func<Vector3, Vector3, bool> isClear, Vector3 target, Vector3 position, Vector3 offset)
        => isClear(target + offset, position + offset) && isClear(position, position + offset);

    /// <summary>Rotate at most 120 degrees per canonical second. Reversed/zero time holds.</summary>
    internal static Vector3 LimitFacing(Vector3 previous, Vector3 desired, double deltaFrames)
    {
        if (!Direction(previous, out previous)) return Direction(desired, out desired) ? desired : -Vector3.UnitZ;
        if (!Direction(desired, out desired) || !double.IsFinite(deltaFrames) || deltaFrames <= 0) return previous;
        float dot = Math.Clamp(Vector3.Dot(previous, desired), -1, 1);
        double angle = Math.Acos(dot);
        double limit = Math.Min(Math.PI, deltaFrames / 60 * Math.PI * MaximumDegreesPerSecond / 180);
        if (angle <= limit || angle < 1e-7) return desired;
        Vector3 axis = Vector3.Cross(previous, desired);
        if (!Direction(axis, out axis))
        {
            axis = Vector3.Cross(previous, Math.Abs(previous.Y) < .9f ? Vector3.UnitY : Vector3.UnitX).Normalized();
        }
        float c = (float)Math.Cos(limit), s = (float)Math.Sin(limit);
        return (previous * c + Vector3.Cross(axis, previous) * s + axis * Vector3.Dot(axis, previous) * (1 - c)).Normalized();
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Direction(Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        if (!Finite(value) || !float.IsFinite(value.LengthSquared) || value.LengthSquared < .000001f) return false;
        normalized = value.Normalized(); return true;
    }
}
