#if !MPHREAD_SERVER
using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal readonly record struct ReplayPresentedCameraPose(Vector3 Position, Quaternion Rotation, float Fov, float Roll);
internal sealed record ReplayPresentedCamera(long Revision, object Transport, long SeekGeneration, uint Frame,
    string LogicalPath, string PlaybackPath, object Settings, ReplayPresentedCameraPose Pose);

/// <summary>Presentation-only storage lives outside Scene so it cannot enter replay schemas, hashes or archives.</summary>
internal sealed class ReplayCameraPresentationCache<TScene> where TScene : class
{
    private readonly ConditionalWeakTable<TScene, ReplayPresentedCamera> _frames = new();
    private readonly ConditionalWeakTable<TScene, ReplayPresentedCamera> _rendered = new();
    private long _revision;
    public void Publish(TScene scene, ReplayPresentedCamera presented)
    { _frames.Remove(scene); _frames.Add(scene, presented with { Revision = ++_revision }); }
    public void DiscardRendered(TScene scene) => _rendered.Remove(scene);
    public void QueueRendered(TScene scene, ReplayPresentedCamera presented)
    { _rendered.Remove(scene); _rendered.Add(scene, presented); }
    public bool TryTakeRendered(TScene scene, out ReplayPresentedCamera? presented)
    {
        if (!_rendered.TryGetValue(scene, out var candidate)) { presented = null; return false; }
        _rendered.Remove(scene); presented = candidate; return true;
    }
    public bool Publish(TScene scene, object transport, long seek, uint frame, string logical, string playback,
        object settings, Matrix4 view, Matrix4 projection)
    {
        _frames.Remove(scene);
        if (!ReplayCameraPresentationMath.TryDecode(view, projection, out var pose)) return false;
        Publish(scene, new(0, transport, seek, frame, logical, playback, settings, pose));
        return true;
    }
    public bool TryRead(TScene scene, object transport, long seek, uint frame, string logical, string playback,
        object settings, out ReplayPresentedCamera? presented)
    {
        if (_frames.TryGetValue(scene, out var saved) && ReferenceEquals(saved.Transport, transport)
            && saved.SeekGeneration == seek && saved.Frame == frame && saved.LogicalPath == logical
            && saved.PlaybackPath == playback && Equals(saved.Settings, settings))
        { presented = saved; return true; }
        presented = null; return false;
    }
}

internal static class ReplayCameraPresentationMath
{
    /// <summary>Decode the exact rendered view/projection; roll is separated from yaw/pitch to avoid applying it twice.</summary>
    public static bool TryDecode(Matrix4 view, Matrix4 projection, out ReplayPresentedCameraPose pose)
    {
        pose = default;
        if (!Finite(view) || !Finite(projection) || Math.Abs(view.Determinant) < .000001f
            || projection.M22 <= 0 || Math.Abs(projection.M34 + 1) > .001f) return false;
        Matrix4 world = Matrix4.Invert(view);
        Vector3 position = world.Row3.Xyz, right = world.Row0.Xyz, up = world.Row1.Xyz, back = world.Row2.Xyz;
        if (!Finite(world) || Math.Abs(right.LengthSquared - 1) > .001f || Math.Abs(up.LengthSquared - 1) > .001f
            || Math.Abs(back.LengthSquared - 1) > .001f || Math.Abs(Vector3.Dot(up, back)) > .001f
            || Math.Abs(Vector3.Dot(right, up)) > .001f || Math.Abs(Vector3.Dot(right, back)) > .001f) return false;
        Vector3 facing = -back.Normalized();
        float yaw = MathF.Atan2(-facing.X, -facing.Z), pitch = MathF.Asin(Math.Clamp(facing.Y, -1, 1));
        Quaternion rotation = (Quaternion.FromAxisAngle(Vector3.UnitY, yaw)
            * Quaternion.FromAxisAngle(Vector3.UnitX, pitch)).Normalized();
        Vector3 levelUp = Vector3.Transform(Vector3.UnitY, rotation);
        float roll = MathF.Atan2(Vector3.Dot(facing, Vector3.Cross(levelUp, up)), Vector3.Dot(levelUp, up));
        float fov = 2 * MathF.Atan(1 / projection.M22);
        if (!float.IsFinite(fov) || fov <= 0 || fov >= MathF.PI || !float.IsFinite(roll)) return false;
        pose = new(position, rotation, fov, roll); return true;
    }
    private static bool Finite(Matrix4 matrix)
    {
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (!float.IsFinite(matrix[row, column])) return false;
        return true;
    }
}
#endif
