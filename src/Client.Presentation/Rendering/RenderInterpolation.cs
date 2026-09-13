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
            public bool HasState;
            public readonly Dictionary<ModelInstance, ModelPoseHistory> Models = new();
            public PlayerBipedPoseHistory? Biped;
            public ModelPoseHistory? Alt;
            public Matrix4[] AltTransforms = Array.Empty<Matrix4>();
            public Matrix4[] AltPose = Array.Empty<Matrix4>();
        }
        private readonly Dictionary<EntityBase, PoseTrack> _poses = new();
        private readonly List<EntityBase> _removedPoses = new();
        private readonly SimulationPoseHistory _cameraHistory = new();
        private readonly ScalarPoseHistory _cameraFovHistory = new();
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
        public void ResetRenderLook()
        {
            RenderLook?.Reset();
            Mods.Input.GamepadInput.CancelPlatformStylus();
            Mods.Input.GamepadHaptics.Stop();
            // Camera ownership and prediction share the same lifecycle as the
            // mouse accumulator. A pause, focus loss, room transition or
            // spectator handoff must not carry controller velocity over the
            // boundary into the next local view.
            Mods.Input.GamepadInput.ResetLook();
        }

        /// <summary>
        /// Submit a mouse event to the shared ownership gate while retaining
        /// the raw event in the legacy render accumulator. The latter remains
        /// the gameplay source so PlayerInput keeps its established units and
        /// zoom/FOV scaling; the coordinator only owns source metadata and
        /// same-frame precedence.
        /// </summary>
        public void SubmitMouseLook(Vector2 rawDelta)
            => SubmitRelativeLook(LookDeviceKind.Mouse, rawDelta);

        /// <summary>Submit an Android touch aim delta to the same ownership gate.</summary>
        public void SubmitTouchLook(Vector2 rawDelta)
            => SubmitRelativeLook(LookDeviceKind.Touch, rawDelta);

        private void SubmitRelativeLook(LookDeviceKind device, Vector2 rawDelta)
        {
            if (!CanCaptureSimulationLook || !float.IsFinite(rawDelta.X)
                || !float.IsFinite(rawDelta.Y) || rawDelta.LengthSquared <= 0)
            {
                return;
            }
            PlayerEntity? player = World.LocalPlayer;
            if (player == null)
            {
                return;
            }
            // Keep this event in the same unzoomed angular units as the
            // legacy RenderLook buffer. The fixed gameplay path applies the
            // current FOV ratio in UpdateAimX/Y, and the render path applies
            // it once while composing its preview.
            Vector2 degrees = RenderLookAccumulator.AimDegrees(rawDelta,
                player.Controls.MouseSensitivity,
                player.Controls.InvertMouseX,
                player.Controls.InvertMouseY);
            Mods.Input.GamepadInput.LookCoordinator.Submit(
                new LocalLookFrame(device, degrees, rawDelta,
                    rawDelta.Length));
        }
        public bool CanCaptureSimulationLook => ControlsPlayer && !Mods.SpectatorMode.IsSpectating
            && Mods.ClientInputState.WindowFocused && !Mods.ClientInputState.PauseOpen
            && !Mods.Chat.ChatBox.Composing && !ShowCursor
            && !BottomScreen.DesktopSessionActive
            && !FrameAdvance;
        public bool CanCaptureRenderLook => CanCaptureSimulationLook
            && World.LocalPlayer!.Health > 0
            && World.LocalPlayer!.CameraType == CameraType.First
            && !World.LocalPlayer!.IsAltForm
            && !World.LocalPlayer!.IsMorphing && !World.LocalPlayer!.IsUnmorphing && !World.LocalPlayer!.Flags1.TestFlag(PlayerFlags1.NoAimInput);
        private bool InterpolationEnabled => Timing.Active && !FrameAdvance
            && !Mods.SpectatorMode.IsSpectating && !Mods.Network.ReplayPlayback.IsActive && World.CameraSequences.Current == null;
        // Biped smoothing is skeletal presentation only.  It deliberately has
        // its own eligibility gate: observers and replay still benefit from
        // local pose interpolation, while their world/camera paths must not be
        // switched onto the generic entity interpolation delta.
        private bool SkeletalInterpolationEnabled => Timing.Active && !FrameAdvance
            && !Mods.Network.ReplayPlayback.IsSeeking;
        private bool IsLocal(PlayerEntity player) => !World.Services.IsReplica || player.SlotIndex == World.Services.LocalSlot;
        private static bool Tracks(EntityBase entity) => entity is PlayerEntity or PlatformEntity or DoorEntity
            or BombEntity or BeamProjectileEntity or ItemInstanceEntity or FhItemEntity;
        internal static bool InterpolatesModelPose(EntityType type)
            => type is EntityType.Door or EntityType.Platform
                or EntityType.ItemInstance or EntityType.FhItemInstance;
        private void ResetPoseHistory()
        {
            _poseGeneration++;
            _poses.Clear();
            _particlePoses.Clear();
            _cameraHistory.Reset();
            _cameraFovHistory.Reset();
            ResetRenderLook();
        }
        private void CaptureSimulationPoses()
        {
            long corrections = Mods.Network.AuthoritativePlay.Current?.Prediction.HardCorrections ?? 0;
            int viewState = HashCode.Combine(CameraMode, Mods.SpectatorMode.IsSpectating,
                Mods.Network.ReplayPlayback.IsActive, World.CameraSequences.Current);
            if (_poseRoom != World.RoomId || _poseMain != World.LocalPlayerSlot
                || _timingGeneration != Timing.Discontinuities || corrections != _correctionGeneration || viewState != _viewState)
                ResetPoseHistory();
            if (_poseTick == World.FrameCount && _poses.Count != 0) return;
            _poseRoom = World.RoomId;
            _poseMain = World.LocalPlayerSlot;
            _timingGeneration = Timing.Discontinuities;
            _correctionGeneration = corrections;
            _viewState = viewState;
            _poseTick = World.FrameCount;
            foreach (EntityBase entity in World.Entities)
            {
                if (!Tracks(entity)
                    || entity is BeamProjectileEntity projectile && (projectile.Lifespan <= 0
                        || projectile.Flags.TestFlag(BeamFlags.Continuous) || projectile.Flags.TestFlag(BeamFlags.Collided))) continue;
                if (!_poses.TryGetValue(entity, out PoseTrack? track)) _poses.Add(entity, track = new());
                int state = entity is PlayerEntity player ? HashCode.Combine(player.Health == 0,
                    player.IsAltForm, player.LoadFlags, player.CameraType, player.IsMorphing,
                    player.IsUnmorphing, player.Hunter, player.PresentationPoseEpoch)
                    : entity is BeamProjectileEntity beam ? HashCode.Combine(beam.Generation, beam.Lifespan > 0, beam.Flags.TestFlag(BeamFlags.Collided)) : 0;
                bool discontinuity = !track.HasState || track.State != state;
                // Remote world movement is owned by SnapshotInterpolation and
                // BeginRemotePresentation.  Keep only their skeletal history;
                // capturing a generic world history here would make it too
                // easy for a later draw path to interpolate the root twice.
                if (entity is not PlayerEntity remote || IsLocal(remote))
                    track.History.Capture(entity.Transform, _poseTick, _poseGeneration, discontinuity);
                if (entity is PlayerEntity biped && biped._bipedModel1 != null && biped._bipedModel2 != null)
                {
                    track.Biped ??= new PlayerBipedPoseHistory(biped._bipedModel2.Model);
                    track.Biped.SetModel(biped._bipedModel2.Model);
                    track.Biped.Capture(biped._bipedModel1.AnimInfo, biped._bipedModel2.AnimInfo,
                        PlayerEntity.GetBipedPitch(biped._facingVector), _poseTick, _poseGeneration, discontinuity);
                }
                if (entity is PlayerEntity alternate && alternate.IsAltForm
                    && alternate._altModel != null)
                {
                    Model altModel = alternate._altModel.Model;
                    if (track.Alt == null || !ReferenceEquals(track.Alt.Model, altModel))
                    {
                        track.Alt = new ModelPoseHistory(altModel);
                        track.AltTransforms = new Matrix4[altModel.Nodes.Count];
                        track.AltPose = new Matrix4[altModel.Nodes.Count];
                    }
                    int mode = SamplePlayerAltPose(alternate,
                        track.AltTransforms, track.AltPose);
                    track.Alt.CaptureResolved(alternate._altModel.AnimInfo,
                        track.AltPose, _poseTick, _poseGeneration,
                        discontinuity, mode);
                }
                if (InterpolatesModelPose(entity.Type))
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
                track.HasState = true;
                track.Seen = _poseTick;
            }
            _removedPoses.Clear();
            foreach (var pair in _poses) if (pair.Value.Seen != _poseTick) _removedPoses.Add(pair.Key);
            foreach (EntityBase removed in _removedPoses) _poses.Remove(removed);
            int cameraState = HashCode.Combine(World.LocalPlayer!.Health == 0, World.LocalPlayer!.IsAltForm,
                World.LocalPlayer!.IsMorphing, World.LocalPlayer!.IsUnmorphing, World.LocalPlayer!.CameraType, World.CameraSequences.Current);
            if (World.LocalPlayer!.Health > 0 && !World.LocalPlayer!.IsAltForm
                && !World.LocalPlayer!.IsMorphing && !World.LocalPlayer!.IsUnmorphing)
            {
                _cameraHistory.Capture(World.LocalPlayer!.CameraInfo.ViewMatrix.Inverted(), _poseTick, _poseGeneration, cameraState != _cameraState);
                _cameraFovHistory.Capture(World.LocalPlayer!.CameraInfo.Fov, _poseTick,
                    _poseGeneration, cameraState != _cameraState);
            }
            else
            {
                _cameraHistory.Reset();
                _cameraFovHistory.Reset();
            }
            _cameraState = cameraState;
        }
        private void BeginEntitySubmission(EntityBase entity)
        {
            _submissionDelta = Matrix4.Identity;
            _submissionInterpolated = false;
            if (entity is PlayerEntity remote && !IsLocal(remote)) return;
            if (!InterpolationEnabled || !_poses.TryGetValue(entity, out PoseTrack? track)
                || entity is PlayerEntity transitional && (transitional.Health == 0 || transitional.IsAltForm
                    || transitional.IsMorphing || transitional.IsUnmorphing)) return;
            _submissionDelta = entity is PlayerEntity player && player.IsMainPlayer && player.CameraType == CameraType.First
                ? player.CameraInfo.ViewMatrix * _viewMatrix.Inverted() : track.History.Delta(Timing.RenderAlpha);
            _submissionInterpolated = true;
        }
        internal bool ResolveNodeSubmission(EntityBase entity, ModelInstance inst, out Matrix4[] nodes, out float[] stack)
        {
            nodes = Array.Empty<Matrix4>();
            stack = Array.Empty<float>();
            if (!InterpolationEnabled || !InterpolatesModelPose(entity.Type)
                || !_poses.TryGetValue(entity, out PoseTrack? track)
                || !track.Models.TryGetValue(inst, out ModelPoseHistory? history)) return false;
            history.Resolve(Timing.RenderAlpha, out nodes, out stack);
            // These copied matrices already include the interpolated world root.
            _submissionInterpolated = false;
            return true;
        }

        internal bool ResolvePlayerBipedSubmission(PlayerEntity player, ModelInstance inst,
            in Matrix4 worldRoot, out Matrix4[] nodes, out float[] stack)
        {
            nodes = Array.Empty<Matrix4>();
            stack = Array.Empty<float>();
            if (!SkeletalInterpolationEnabled || !_poses.TryGetValue(player, out PoseTrack? track)
                || track.Biped is not { HasSamples: true } history
                || !ReferenceEquals(history.Model, inst.Model)) return false;
            // A local player's generic entity path may already have selected a
            // render delta. Fold that delta into the single skeletal root before
            // handing matrices to the draw stack; AddRenderItem must not apply
            // it again to either the node transform or the skinning stack.
            Matrix4 resolvedRoot = _submissionInterpolated
                ? worldRoot * _submissionDelta : worldRoot;
            history.Resolve(Timing.RenderAlpha, resolvedRoot, out nodes, out stack);
            _submissionDelta = Matrix4.Identity;
            _submissionInterpolated = false;
            return true;
        }

        internal bool ResolvePlayerAltSubmission(PlayerEntity player,
            ModelInstance inst, out Matrix4[] nodes, out float[] stack)
        {
            nodes = Array.Empty<Matrix4>();
            stack = Array.Empty<float>();
            if (!SkeletalInterpolationEnabled || !player.IsAltForm
                || !_poses.TryGetValue(player, out PoseTrack? track)
                || track.Alt is not { HasSamples: true } history
                || !ReferenceEquals(history.Model, inst.Model))
            {
                return false;
            }
            SamplePlayerAltPose(player, track.AltTransforms, track.AltPose);
            history.Resolve(Timing.RenderAlpha, track.AltPose,
                out nodes, out stack);
            // The returned matrices already contain the resolved player root.
            _submissionDelta = Matrix4.Identity;
            _submissionInterpolated = false;
            return true;
        }

        private static int SamplePlayerAltPose(PlayerEntity player,
            Matrix4[] transforms, Matrix4[] poses)
        {
            Model model = player._altModel.Model;
            if (transforms.Length != model.Nodes.Count
                || poses.Length != model.Nodes.Count)
            {
                throw new ArgumentException("Alternate-form pose buffers do not match the model.");
            }

            if (player.Hunter == Hunter.Kanden)
            {
                for (int i = 0; i < poses.Length; i++)
                {
                    poses[i] = i < player._kandenSegMtx.Length
                        ? player._kandenSegMtx[i]
                        : model.Nodes[i].Animation;
                }
                return 1;
            }

            Matrix4 root = player._modelTransform;
            root.Row3.Xyz = player.Position;
            if (player.Hunter == Hunter.Spire
                && player.Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                Matrix4 attackRoot = PlayerEntity.GetTransformMatrix(
                    player._spireAltFacing, player._spireAltUp);
                NodePoseSampler.Sample(model, player._altModel.AnimInfo,
                    attackRoot, transforms, poses, useNodeTransform: false);
                if (poses.Length > 0)
                {
                    poses[0] = root;
                    for (int i = 1; i < poses.Length; i++)
                    {
                        poses[i].Row3.Xyz += player.Position;
                    }
                }
                return 2;
            }

            NodePoseSampler.Sample(model, player._altModel.AnimInfo,
                root, transforms, poses);
            return 0;
        }

        private void EndEntitySubmission() { _submissionDelta = Matrix4.Identity; _submissionInterpolated = false; }
        private Matrix4 SubmissionTransform(Matrix4 transform) => _submissionInterpolated ? transform * _submissionDelta : transform;
        private void ApplyRenderCamera()
        {
            if (!ControlsPlayer || Mods.SpectatorMode.IsSpectating || Mods.Network.ReplayPlayback.IsActive) return;
            Matrix4 camera = World.LocalPlayer!.CameraInfo.ViewMatrix.Inverted();
            if (InterpolationEnabled && _cameraHistory.HasSamples) camera.Row3.Xyz = _cameraHistory.Resolve(Timing.RenderAlpha).Row3.Xyz;
            if (InterpolationEnabled && _cameraFovHistory.HasSamples)
            {
                float fov = _cameraFovHistory.Resolve(Timing.RenderAlpha);
                if (float.IsFinite(fov) && fov > 0) _cameraFov = MathHelper.DegreesToRadians(fov);
            }
            if (CanCaptureRenderLook)
            {
                PlayerEntity player = World.LocalPlayer!;
                float normalFov = Fixed.ToFloat(player.Values.NormalFov) * 2;
                float zoom = player.EquipInfo.Zoomed && normalFov != 0 ? player.CameraInfo.Fov / normalFov : 1;
                // Controller look is stateful rather than an event stream. It
                // is predicted from the latest processed velocity only for
                // the render interval not represented by fixed simulation;
                // no synthetic mouse event is inserted or consumed here.
                LocalLookFrame predicted = Mods.Input.GamepadInput.LookCoordinator
                    .PeekForRender(null, Timing.Active);
                bool cameraBlocksInput = World.CameraSequences.Current?.Flags
                    .TestFlag(CamSeqFlags.BlockInput) == true;
                bool keyboardAimActive = player.Controls.KeyboardAim
                    && (player.Controls.AimLeft.IsDown || player.Controls.AimRight.IsDown
                        || player.Controls.AimUp.IsDown || player.Controls.AimDown.IsDown);
                Vector2 aim = Vector2.Zero;
                if (RenderLook != null)
                {
                    // The desktop accumulator is the legacy mouse source and
                    // is already converted with mouse sensitivity/FOV. It is
                    // still subject to the original mouse gates.
                    if (player.Controls.MouseAim && !keyboardAimActive
                        && !cameraBlocksInput)
                    {
                        aim = RenderLookAccumulator.AimDegrees(
                            RenderLook.PeekForRender(null, simulationActive: false),
                            player.Controls.MouseSensitivity,
                            player.Controls.InvertMouseX ^ player.Controls.InvertAimX,
                            player.Controls.InvertMouseY ^ player.Controls.InvertAimY, zoom);
                    }
                    // Touch/stylus events do not use the desktop raw
                    // accumulator, so they remain visible in a mixed desktop
                    // frame without replaying a mouse event.
                    Vector2 precision = predicted.TouchDeltaDegrees
                        + predicted.StylusDeltaDegrees;
                    if (!cameraBlocksInput && precision != Vector2.Zero)
                    {
                        aim += RenderLookAccumulator.ApplySimulationAimTransforms(
                            precision, player.Controls.InvertAimX,
                            player.Controls.InvertAimY, zoom);
                    }
                }
                else
                {
                    Vector2 precision = predicted.PrecisionDeltaDegrees;
                    if (player.Controls.MouseAim == false || keyboardAimActive)
                    {
                        precision -= predicted.MouseDeltaDegrees;
                    }
                    if (!cameraBlocksInput && precision != Vector2.Zero)
                    {
                        // Coordinator precision is already in source units;
                        // this is the one render-side application of the
                        // simulation's InvertAim/FOV transforms for Android.
                        aim = RenderLookAccumulator.ApplySimulationAimTransforms(
                            precision, player.Controls.InvertAimX,
                            player.Controls.InvertAimY, zoom);
                    }
                }
                Vector2 controllerAim = predicted.ControllerDeltaDegrees;
                if (cameraBlocksInput)
                {
                    controllerAim = Vector2.Zero;
                }
                else if (controllerAim != Vector2.Zero)
                {
                    // Match UpdateAimX/Y's inversion/FOV exactly once, then
                    // apply the controller-only zoom preference to the player
                    // stick contribution. Precision assistance is never in
                    // this value and cannot be amplified by it.
                    controllerAim = RenderLookAccumulator.ApplySimulationAimTransforms(
                        controllerAim, player.Controls.InvertAimX,
                        player.Controls.InvertAimY, zoom);
                    if (player.EquipInfo.Zoomed)
                    {
                        controllerAim *= Mods.InputSettings.GamepadZoomMultiplier;
                    }
                }
                aim += controllerAim;
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
