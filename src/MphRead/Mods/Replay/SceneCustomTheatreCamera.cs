using System;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    internal void CopyCustomTheatreView(Scene previous)
    {
        _freeCam = previous._freeCam;
        _cameraPosition = previous._cameraPosition; _cameraFacing = previous._cameraFacing;
        _cameraUp = previous._cameraUp; _cameraRight = previous._cameraRight;
        _cameraFov = previous._cameraFov;
    }

    private bool ApplyCustomTheatreCamera()
    {
        if (!DemoPlayback.IsCustomPlayback || !DemoPlayback.Owns(this)) return false;
        var mode = ReplayCamera.Mode;
        if (mode == ReplayCameraMode.FirstPerson && SetCustomInspectionFirstPerson(DemoPlayback.CustomSubjectSlot))
        { _freeCam = false; return true; }
        ClearCustomInspectionFirstPerson();
        if (mode == ReplayCameraMode.Free)
        {
            _freeCam = true; _cameraMode = CameraMode.Roam;
            if (_cameraFacing.LengthSquared < .00001f) _cameraFacing = -Vector3.UnitZ;
            SetReplicaCamera(_cameraPosition, _cameraPosition + _cameraFacing, ReplayCamera.FieldOfView);
            return true;
        }
        var actor = Players.Items[0];
        foreach (var candidate in Players.Items)
            if (candidate.SlotIndex == DemoPlayback.CustomSubjectSlot) { actor = candidate; break; }
        Vector3 target = actor.Position + Vector3.UnitY;
        Vector3 offset = new(3, 2, 4);
        if (mode == ReplayCameraMode.Overview) offset = new(0, 30, 15);
        else if (mode == ReplayCameraMode.Orbit)
        {
            float angle = (float)(DemoPlayback.Session.Transport.PresentationFrame(ReplayRenderAlpha) / 60 * .4);
            offset = new(MathF.Sin(angle) * 5, 2, MathF.Cos(angle) * 5);
        }
        // This inspection camera stays render-only. It does not establish a
        // global spectator, mutate MainPlayerIndex or run a retail HUD/audio owner.
        _freeCam = true;
        SetReplicaCamera(target + offset, target, ReplayCamera.FieldOfView);
        return true;
    }
}
