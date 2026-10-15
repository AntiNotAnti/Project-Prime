using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead;

public partial class Scene
{
    private ReplayCameraTrack? _studioCameraTrack;
    private ReplayCameraMode _studioCameraMode;
    internal Action<Scene>? StudioReplayOverlay { get; set; }
    internal bool? StudioReplayGameHud { get; set; }
    internal int? StudioOutputFramebuffer { get; set; }
    internal void StudioReplayPrepare(ReplayCameraTrack track, double frame, float alpha = 1,
        ReplayCameraMode mode = ReplayCameraMode.FirstPerson, int slot = 0)
    {
        ReplayPresentationFrame = frame;
        ReplayRenderAlpha = alpha;
        _studioCameraTrack = track;
        _studioCameraMode = mode;
        if (slot >= 0 && slot < Players.Items.Count) Players.MainPlayerIndex = slot;
    }
    // Called at the canonical camera preparation point, after invalidating the
    // previous picture's first-person pose. All camera state belongs to this scene.
    internal void StudioReplayApplyPresentationCamera() { StudioReplayApplyCamera(); TransformCamera(); UpdateCameraPosition(); }
    private void StudioReplayApplyCamera()
    {
        var player = Players.Main;
        if (player.LoadFlags.TestFlag(LoadFlags.Active) && !player.HudReady) player.SetUpHud();
        if (_studioCameraTrack?.Sample(ReplayPresentationFrame, out var key) == true)
        {
            StudioReplayFreeCamera(true);
            _cameraPosition = key.Position;
            _cameraFacing = Vector3.Transform(-Vector3.UnitZ, key.Rotation).Normalized();
            _cameraUp = Vector3.Transform(Vector3.UnitY, key.Rotation).Normalized();
            if (key.LookAtSlot >= 0 && key.LookAtSlot < Players.Items.Count)
            {
                var target = Players.Items[key.LookAtSlot];
                if (target.LoadFlags.TestFlag(LoadFlags.Active) && target.LoadFlags.TestFlag(LoadFlags.Spawned))
                {
                    var direction = target.ReplayDrawTransform.Row3.Xyz + Vector3.UnitY - _cameraPosition;
                    if (direction.LengthSquared > 0.0001f) _cameraFacing = direction.Normalized();
                }
            }
            _cameraUp = Vector3.Transform(_cameraUp, Quaternion.FromAxisAngle(_cameraFacing, key.Roll));
            var right = Vector3.Cross(_cameraFacing, _cameraUp);
            if (right.LengthSquared < 0.0001f) right = Vector3.Cross(_cameraFacing, MathF.Abs(_cameraFacing.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ);
            _cameraRight = right.Normalized();
            _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing).Normalized();
            _cameraFov = key.Fov;
            return;
        }
        StudioReplayFreeCamera(_studioCameraMode != ReplayCameraMode.FirstPerson);
        if (_studioCameraMode is not (ReplayCameraMode.Chase or ReplayCameraMode.Orbit)) return;
        Vector3 targetPosition = player.ReplayDrawTransform.Row3.Xyz + Vector3.UnitY * 1.5f;
        Vector3 facing = player.CameraInfo.Facing;
        if (ReplayPoses?.SamplePresented(player.SlotIndex, ReplayRenderAlpha, out _, out var replicaFacing) == true) facing = replicaFacing;
        if (_studioCameraMode == ReplayCameraMode.Orbit)
        {
            float angle = (float)(ReplayPresentationFrame / 60 * 0.4);
            facing = new(MathF.Sin(angle), 0, MathF.Cos(angle));
        }
        facing.Y = 0;
        if (facing.LengthSquared < 0.001f) facing = -Vector3.UnitZ;
        Vector3 desired = targetPosition - facing.Normalized() * 4;
        CollisionResult collision = default;
        if (CollisionDetection.CheckBetweenPoints(targetPosition, desired, TestFlags.Players, this, ref collision))
            desired = targetPosition + (desired - targetPosition) * Math.Max(0, collision.Distance - 0.05f);
        _cameraPosition = desired;
        Vector3 view = targetPosition - desired;
        if (view.LengthSquared > 0.0001f) _cameraFacing = view.Normalized();
        _cameraRight = Vector3.Cross(_cameraFacing, Vector3.UnitY).Normalized();
        _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing).Normalized();
    }
    private void StudioReplayFreeCamera(bool on)
    {
        // SetFreeCamera notifies Game's global spectator controller. Private
        // documents establish the identical scene fields without that side effect.
        _freeCam = on;
        _cameraMode = on ? CameraMode.Roam : CameraMode.Player;
        _inputMode = on ? InputMode.CameraOnly : InputMode.All;
    }
    internal ReplayCameraKeyframe StudioReplayCapture(uint frame)
        => new(frame, _cameraPosition, ReplayCameraTrack.FacingRotation(_cameraFacing), _cameraFov);
}
