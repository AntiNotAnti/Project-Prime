using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
namespace MphRead
{
    public partial class ScenePresentation
    {
        private sealed class PoseTrack
        {
            public readonly SimulationPoseHistory History = new();
            public ulong Seen;
            public int State;
            public readonly Dictionary<ModelInstance, ModelPoseHistory> Models = new();
        }
        private readonly Dictionary<EntityBase, PoseTrack> _poses = new();
        private readonly List<EntityBase> _removedPoses = new();
        private readonly SimulationPoseHistory _cameraHistory = new();
        private long _poseGeneration;
        private long _timingGeneration;
        private long _correctionGeneration;
        private int _cameraState;
        private int _viewState;
        private int _poseRoom = -1, _poseMain = -1;
        private ulong _poseTick = ulong.MaxValue;
        private Matrix4 _submissionDelta = Matrix4.Identity;
        private bool _submissionInterpolated;
        public RenderLookAccumulator? RenderLook { get; private set; }
        public void EnableDesktopLook() => RenderLook = new();
        public void ResetRenderLook() => RenderLook?.Reset();
        public bool CanCaptureSimulationLook => ControlsPlayer && !Mods.SpectatorMode.IsSpectating
            && !Mods.ClientInputState.PauseOpen && !Mods.Chat.ChatBox.Composing && !ShowCursor
            && !FrameAdvance;
        public bool CanCaptureRenderLook => CanCaptureSimulationLook
            && CameraSequence.Current == null && PlayerEntity.Main.Health > 0
            && PlayerEntity.Main.CameraType == CameraType.First
            && PlayerEntity.Main.Controls.MouseAim && !PlayerEntity.Main.IsAltForm
            && !PlayerEntity.Main.IsMorphing && !PlayerEntity.Main.IsUnmorphing && !PlayerEntity.Main.Flags1.TestFlag(PlayerFlags1.NoAimInput);
        private bool InterpolationEnabled => FrameTiming.Active && !FrameAdvance
            && !Mods.SpectatorMode.IsSpectating && !Mods.Network.DemoPlayback.IsActive && CameraSequence.Current == null;
        private bool IsLocal(PlayerEntity player) => !World.Services.IsReplica || player.SlotIndex == World.Services.LocalSlot;
        private static bool Tracks(EntityBase entity) => entity is PlayerEntity or PlatformEntity or DoorEntity
            or BombEntity or BeamProjectileEntity or ItemInstanceEntity;
        private void ResetPoseHistory()
        {
            _poseGeneration++;
            _poses.Clear();
            _particlePoses.Clear();
            _cameraHistory.Reset();
            ResetRenderLook();
        }
        private void CaptureSimulationPoses()
        {
            long corrections = Mods.Network.AuthoritativePlay.Current?.Prediction.HardCorrections ?? 0;
            int viewState = HashCode.Combine(CameraMode, Mods.SpectatorMode.IsSpectating,
                Mods.Network.DemoPlayback.IsActive, CameraSequence.Current);
            if (_poseRoom != World.RoomId || _poseMain != PlayerEntity.MainPlayerIndex
                || _timingGeneration != FrameTiming.Discontinuities || corrections != _correctionGeneration || viewState != _viewState)
                ResetPoseHistory();
            if (_poseTick == World.FrameCount && _poses.Count != 0) return;
            _poseRoom = World.RoomId;
            _poseMain = PlayerEntity.MainPlayerIndex;
            _timingGeneration = FrameTiming.Discontinuities;
            _correctionGeneration = corrections;
            _viewState = viewState;
            _poseTick = World.FrameCount;
            foreach (EntityBase entity in World.Entities)
            {
                if (!Tracks(entity) || entity is PlayerEntity remote && !IsLocal(remote)
                    || entity is BeamProjectileEntity projectile && (projectile.Lifespan <= 0
                        || projectile.Flags.TestFlag(BeamFlags.Continuous) || projectile.Flags.TestFlag(BeamFlags.Collided))) continue;
                if (!_poses.TryGetValue(entity, out PoseTrack? track)) _poses.Add(entity, track = new());
                int state = entity is PlayerEntity player ? HashCode.Combine(player.Health == 0,
                    player.IsAltForm, player.LoadFlags, player.CameraType, player.IsMorphing, player.IsUnmorphing)
                    : entity is BeamProjectileEntity beam ? HashCode.Combine(beam.Generation, beam.Lifespan > 0, beam.Flags.TestFlag(BeamFlags.Collided)) : 0;
                track.History.Capture(entity.Transform, _poseTick, _poseGeneration, track.State != state);
                if (entity is DoorEntity or PlatformEntity)
                {
                    for (int i = 0; i < entity._models.Count; i++)
                    {
                        ModelInstance inst = entity._models[i];
                        if (!track.Models.TryGetValue(inst, out ModelPoseHistory? modelPose))
                            track.Models.Add(inst, modelPose = new(inst.Model));
                        modelPose.Capture(inst.AnimInfo, entity.GetModelTransform(inst, i), _poseTick, _poseGeneration);
                    }
                }
                track.State = state;
                track.Seen = _poseTick;
            }
            _removedPoses.Clear();
            foreach (var pair in _poses) if (pair.Value.Seen != _poseTick) _removedPoses.Add(pair.Key);
            foreach (EntityBase removed in _removedPoses) _poses.Remove(removed);
            int cameraState = HashCode.Combine(PlayerEntity.Main.Health == 0, PlayerEntity.Main.IsAltForm,
                PlayerEntity.Main.IsMorphing, PlayerEntity.Main.IsUnmorphing, PlayerEntity.Main.CameraType, CameraSequence.Current);
            if (PlayerEntity.Main.Health > 0 && !PlayerEntity.Main.IsAltForm
                && !PlayerEntity.Main.IsMorphing && !PlayerEntity.Main.IsUnmorphing)
                _cameraHistory.Capture(PlayerEntity.Main.CameraInfo.ViewMatrix.Inverted(), _poseTick, _poseGeneration, cameraState != _cameraState);
            else _cameraHistory.Reset();
            _cameraState = cameraState;
        }
        private void BeginEntitySubmission(EntityBase entity)
        {
            _submissionDelta = Matrix4.Identity;
            _submissionInterpolated = false;
            if (!InterpolationEnabled || !_poses.TryGetValue(entity, out PoseTrack? track)
                || entity is PlayerEntity transitional && (transitional.Health == 0 || transitional.IsAltForm
                    || transitional.IsMorphing || transitional.IsUnmorphing)) return;
            _submissionDelta = entity is PlayerEntity player && player.IsMainPlayer && player.CameraType == CameraType.First
                ? player.CameraInfo.ViewMatrix * _viewMatrix.Inverted() : track.History.Delta(FrameTiming.RenderAlpha);
            _submissionInterpolated = true;
        }
        internal bool ResolveNodeSubmission(EntityBase entity, ModelInstance inst, out Matrix4[] nodes, out float[] stack)
        {
            nodes = Array.Empty<Matrix4>();
            stack = Array.Empty<float>();
            if (!InterpolationEnabled || entity is not (DoorEntity or PlatformEntity) || !_poses.TryGetValue(entity, out PoseTrack? track)
                || !track.Models.TryGetValue(inst, out ModelPoseHistory? history)) return false;
            history.Resolve(FrameTiming.RenderAlpha, out nodes, out stack);
            // These copied matrices already include the interpolated world root.
            _submissionInterpolated = false;
            return true;
        }
        private void EndEntitySubmission() { _submissionDelta = Matrix4.Identity; _submissionInterpolated = false; }
        private Matrix4 SubmissionTransform(Matrix4 transform) => _submissionInterpolated ? transform * _submissionDelta : transform;
        private void ApplyRenderCamera()
        {
            if (!ControlsPlayer || Mods.SpectatorMode.IsSpectating || Mods.Network.DemoPlayback.IsActive) return;
            Matrix4 camera = PlayerEntity.Main.CameraInfo.ViewMatrix.Inverted();
            if (InterpolationEnabled && _cameraHistory.HasSamples) camera.Row3.Xyz = _cameraHistory.Resolve(FrameTiming.RenderAlpha).Row3.Xyz;
            if (RenderLook != null && CanCaptureRenderLook)
            {
                PlayerEntity player = PlayerEntity.Main;
                float normalFov = Fixed.ToFloat(player.Values.NormalFov) * 2;
                float zoom = player.EquipInfo.Zoomed && normalFov != 0 ? player.CameraInfo.Fov / normalFov : 1;
                Vector2 aim = RenderLookAccumulator.AimDegrees(RenderLook.Peek(), player.Controls.MouseSensitivity,
                    player.Controls.InvertMouseX ^ player.Controls.InvertAimX,
                    player.Controls.InvertMouseY ^ player.Controls.InvertAimY, zoom);
                if (player.Controls.KeyboardAim && (player.Controls.AimLeft.IsDown || player.Controls.AimRight.IsDown
                    || player.Controls.AimUp.IsDown || player.Controls.AimDown.IsDown)) aim = Vector2.Zero;
                float pitch = MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(player._gunVec1.Y, -1, 1)));
                camera = RenderLookAccumulator.ApplyCameraLook(camera, aim, pitch);
            }
            camera.Row3.Xyz += Mods.Network.AuthoritativePlay.Current?.VisualOffset ?? Vector3.Zero;
            _cameraPosition = camera.Row3.Xyz;
            _viewMatrix = camera.Inverted();
        }
        public static void TransformCopiedStack(float[] stack, int count, Matrix4 delta)
        {
            for (int index = 0; index < count; index++)
            {
                int i = index * 16;
                Matrix4 value = new(new Vector4(stack[i], stack[i+1], stack[i+2], stack[i+3]),
                    new Vector4(stack[i+4], stack[i+5], stack[i+6], stack[i+7]),
                    new Vector4(stack[i+8], stack[i+9], stack[i+10], stack[i+11]),
                    new Vector4(stack[i+12], stack[i+13], stack[i+14], stack[i+15]));
                value *= delta;
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) stack[i+row*4+col] = value[row,col];
            }
        }
    }
}
