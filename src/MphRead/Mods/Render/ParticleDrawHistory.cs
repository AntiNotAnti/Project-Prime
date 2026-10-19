using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Completed native particle values only. Sampling never evaluates an effect
// function, advances a particle, queries collision, or consumes random numbers.
internal readonly record struct ParticleDrawPose(Vector3 Position, Vector3 Speed,
    Vector3 Color, float Alpha, float Scale, float Rotation)
{
    internal bool Finite => Valid(Position) && Valid(Speed) && Valid(Color)
        && float.IsFinite(Alpha) && float.IsFinite(Scale) && float.IsFinite(Rotation);
    private static bool Valid(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

internal readonly record struct ParticleDrawLifetime(long Scene, long Timeline,
    object Owner, float Birth, int Particle, int Draw, int SetVecs, uint Flags)
{
    internal bool Valid => Owner != null && float.IsFinite(Birth);
    internal bool Matches(in ParticleDrawLifetime other) => Scene == other.Scene && Timeline == other.Timeline
        && ReferenceEquals(Owner, other.Owner) && Birth == other.Birth && Particle == other.Particle
        && Draw == other.Draw && SetVecs == other.SetVecs && Flags == other.Flags;
}

internal struct ParticleDrawHistory
{
    private ParticleDrawPose _previous, _current;
    private ParticleDrawLifetime _lifetime;
    private ulong _frame;
    private bool _valid, _carry;

    internal void Capture(in ParticleDrawPose pose, in ParticleDrawLifetime lifetime, ulong frame)
    {
        bool consecutive = _valid && _frame != ulong.MaxValue && frame == _frame + 1
            && lifetime.Valid && _lifetime.Matches(lifetime) && _current.Finite && pose.Finite;
        _previous = consecutive ? _current : pose;
        _current = pose;
        _lifetime = lifetime;
        _frame = frame;
        _valid = true;
        _carry = consecutive;
    }

    internal readonly ParticleDrawPose Sample(in ParticleDrawPose canonical,
        in ParticleDrawLifetime lifetime, ulong frame, double alpha)
    {
        if (!_valid || !_carry || _frame != frame || !lifetime.Valid || !_lifetime.Matches(lifetime)
            || _current != canonical || !double.IsFinite(alpha)) return canonical;
        float t = (float)Math.Clamp(alpha, 0, 1);
        if (t <= 0) return _previous;
        if (t >= 1) return canonical;
        // Only rotating quads interpret Rotation as degrees. Streak draw
        // functions use this field as a length and must keep linear sampling.
        float rotationDelta = lifetime.Draw is 4 or 5
            ? MathF.IEEERemainder(_current.Rotation - _previous.Rotation, 360f)
            : _current.Rotation - _previous.Rotation;
        var result = new ParticleDrawPose(
            Vector3.Lerp(_previous.Position, _current.Position, t),
            Vector3.Lerp(_previous.Speed, _current.Speed, t),
            Vector3.Lerp(_previous.Color, _current.Color, t),
            _previous.Alpha + (_current.Alpha - _previous.Alpha) * t,
            _previous.Scale + (_current.Scale - _previous.Scale) * t,
            _previous.Rotation + rotationDelta * t);
        return result.Finite ? result : canonical;
    }
}
