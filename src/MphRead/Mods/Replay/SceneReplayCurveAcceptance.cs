using System;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // Read-only probes for the asset-backed export diagnostic. These never enter
    // simulation/checkpoint state or change the camera being validated.
    internal Mods.Replay.ReplayCameraKeyframe CurveAcceptanceCamera() => new(0,
        _cameraPosition, Mods.Replay.ReplayCameraTrack.FacingRotation(_cameraFacing), _cameraFov);

    internal bool CurveAcceptanceMatches(Mods.Replay.ReplayCameraKeyframe key)
    {
        static bool Finite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
        if(!Finite(key.Position)||!float.IsFinite(key.Rotation.LengthSquared)||key.Rotation.LengthSquared<.0001f
            ||!float.IsFinite(key.Fov)||!float.IsFinite(key.Roll)||key.Fov<=0||key.Fov>=MathF.PI
            ||!Finite(_cameraPosition)||!Finite(_cameraFacing)||!Finite(_cameraUp)||!float.IsFinite(_cameraFov))return false;
        var facing = Vector3.Transform(-Vector3.UnitZ, key.Rotation).Normalized();
        var up = Vector3.Transform(Vector3.UnitY, key.Rotation).Normalized();
        up = Vector3.Transform(up, Quaternion.FromAxisAngle(facing, key.Roll)).Normalized();
        if ((_cameraPosition - key.Position).Length > .0005f || (_cameraFacing - facing).Length > .0005f
            || (_cameraUp - up).Length > .0005f || Math.Abs(_cameraFov - key.Fov) > .0001f) return false;
        Matrix4 expected = Matrix4.LookAt(key.Position, key.Position + facing, up);
        Matrix4 projection=GetPerspectiveMatrix(key.Fov);
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
            if(!float.IsFinite(_viewMatrix[row,column])||!float.IsFinite(_perspectiveMatrix[row,column])
                ||!float.IsFinite(expected[row,column])||!float.IsFinite(projection[row,column])
                ||Math.Abs(_viewMatrix[row,column]-expected[row,column])>.001f
                ||Math.Abs(_perspectiveMatrix[row,column]-projection[row,column])>.001f)return false;
        return true;
    }
}
