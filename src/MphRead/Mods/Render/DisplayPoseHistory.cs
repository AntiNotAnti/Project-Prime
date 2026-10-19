using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>
/// The existing scene and actor identities which permit draw-only pose carry.
/// This is a witness to canonical state, never a second gameplay lifetime.
/// </summary>
internal readonly record struct DisplayPoseLifetime(
    long SceneGeneration,
    object? RoomOwner,
    ulong AuthorityEpoch = 0,
    ushort MatchId = 0,
    ushort SlotGeneration = 0,
    ushort LifeId = 0,
    byte Form = 0,
    long TimelineGeneration = 0,
    bool Eligible = true,
    bool Replica = false,
    byte Hunter = 0)
{
    internal bool Matches(in DisplayPoseLifetime other) => SceneGeneration == other.SceneGeneration
        && ReferenceEquals(RoomOwner, other.RoomOwner)
        && AuthorityEpoch == other.AuthorityEpoch && MatchId == other.MatchId
        && SlotGeneration == other.SlotGeneration && LifeId == other.LifeId
        && Form == other.Form && TimelineGeneration == other.TimelineGeneration
        && Eligible == other.Eligible && Replica == other.Replica && Hunter == other.Hunter;
}

/// <summary>
/// Per-entity value snapshots of the last two completed canonical transforms.
/// Only the existing simulation capture hook writes these values. Sampling
/// cannot enumerate entities, advance a clock, predict motion, or write back
/// into simulation, collision, networking, or replay capture.
/// </summary>
internal struct DisplayPoseHistory
{
    private Matrix4 _previous, _current;
    private DisplayPoseLifetime _lifetime;
    private ulong _step;
    private bool _valid, _hasPrevious;

    internal void Reset(in Matrix4 canonical, in DisplayPoseLifetime lifetime, ulong simulationStep)
    {
        _previous = _current = canonical;
        _lifetime = lifetime;
        _step = simulationStep;
        _valid = true;
        _hasPrevious = false;
    }

    internal void Capture(in Matrix4 canonical, in DisplayPoseLifetime lifetime, ulong simulationStep,
        float teleportDistanceSquared = 16f)
    {
        // A repeated tick is a paused/replaced sample, not another simulation
        // step. Gaps, clock rewinds, new rooms/lives/forms and teleports all
        // restart from the canonical transform without carrying the old pose.
        if (!_valid || !lifetime.Eligible || !_lifetime.Matches(lifetime)
            || _step == ulong.MaxValue || simulationStep != _step + 1
            || !Finite(canonical) || !Finite(_current)
            || (canonical.Row3.Xyz - _current.Row3.Xyz).LengthSquared > teleportDistanceSquared)
        {
            Reset(canonical, lifetime, simulationStep);
            return;
        }
        _previous = _current;
        _current = canonical;
        _step = simulationStep;
        _hasPrevious = true;
    }

    private readonly bool CanSample(in Matrix4 canonical, in DisplayPoseLifetime lifetime,
        ulong completedSteps, double alpha) => _valid && _hasPrevious && lifetime.Eligible
        && _lifetime.Matches(lifetime) && completedSteps > 0 && _step == completedSteps - 1
        // Match Fruity's useful guard: a pose changed after its capture is no
        // longer the completed step's pose, even if the movement was small.
        && canonical == _current && double.IsFinite(alpha);

    internal readonly bool TryGetPosition(in Matrix4 canonical, in DisplayPoseLifetime lifetime,
        ulong completedSteps, double alpha, out Vector3 position)
    {
        position = canonical.Row3.Xyz;
        if (!CanSample(canonical, lifetime, completedSteps, alpha)) return false;
        position = Vector3.Lerp(_previous.Row3.Xyz, _current.Row3.Xyz,
            (float)Math.Clamp(alpha, 0, 1));
        return true;
    }

    internal readonly bool TryGetTransform(in Matrix4 canonical, in DisplayPoseLifetime lifetime,
        ulong completedSteps, double alpha, out Matrix4 transform)
    {
        transform = canonical;
        if (!CanSample(canonical, lifetime, completedSteps, alpha)) return false;
        float t = (float)Math.Clamp(alpha, 0, 1);
        if (t <= 0) { transform = _previous; return true; }
        if (t >= 1 || _previous == _current) { transform = _current; return true; }
        if (_previous.Row2.Xyz.LengthSquared < 0.000001f || _current.Row2.Xyz.LengthSquared < 0.000001f
            || _previous.Row1.Xyz.LengthSquared < 0.000001f || _current.Row1.Xyz.LengthSquared < 0.000001f)
            return false;

        Vector3 scale = Vector3.Lerp(_previous.ExtractScale(), _current.ExtractScale(), t);
        Quaternion rotation = Quaternion.Slerp(_previous.ExtractRotation(), _current.ExtractRotation(), t).Normalized();
        Matrix4 result = Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation);
        result.Row3.Xyz = Vector3.Lerp(_previous.Row3.Xyz, _current.Row3.Xyz, t);
        if (!Finite(result)) return false;
        transform = result;
        return true;
    }

    private static bool Finite(in Matrix4 matrix) => Finite(matrix.Row0) && Finite(matrix.Row1)
        && Finite(matrix.Row2) && Finite(matrix.Row3);
    private static bool Finite(Vector4 row) => float.IsFinite(row.X) && float.IsFinite(row.Y)
        && float.IsFinite(row.Z) && float.IsFinite(row.W);
}
