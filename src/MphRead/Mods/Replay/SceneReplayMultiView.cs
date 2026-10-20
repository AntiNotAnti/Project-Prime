#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Runtime.CompilerServices;
using System.Linq;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_REPLAY_PREVIEW_FAILURE_FIXTURE
    // Only the explicit OpenGL acceptance fixture can inject a render failure.
    internal static Func<int, Exception?>? NativeReplayMultiViewFailureForTests;
#endif

    private sealed class MultiViewPass
    {
        internal int Index;
        internal int Subject;
        internal Vector3 Position, Target, Up;
        internal float Fov;
    }

    private static readonly ConditionalWeakTable<Scene, MultiViewPass> MultiViewPasses = new();
    internal bool NativeReplayMultiViewActive => MultiViewPasses.TryGetValue(this, out _);
    private bool NativeReplayMultiViewPreserveCanvas =>
        MultiViewPasses.TryGetValue(this, out var pass) && pass.Index > 0;

    // One owner for the auxiliary render state, shared by four-view and A/B comparison.
    // The normal world render already completed before an auxiliary pass starts.
    // No replay clock, simulation, camera edit, or persistent scene state is authored here.
    private sealed class ReplayAuxiliaryRenderScope : IDisposable
    {
        private readonly Scene _scene;
        internal readonly MultiViewPass Pass;
        private readonly CameraMode _mode;
        private readonly Vector3 _position, _facing, _up, _right;
        private readonly float _fov, _modelFov;
        private readonly bool _free;
        private readonly int _subject, _frustumIndex, _frustumCount;
        private readonly Matrix4 _view, _inv, _invY, _projection, _modelProjection;
        private readonly Array _planes;
        private readonly Vector2i _previewSize;
        private readonly Vector4i? _previewBounds;
        private readonly int _draw, _read, _program, _activeTexture, _clientTexture;
        private readonly int[] _viewport = new int[4];
        private bool _pushed, _registered, _disposed;

        internal ReplayAuxiliaryRenderScope(Scene scene, MultiViewPass pass)
        {
            _scene = scene;
            Pass = pass;
            _mode = scene._cameraMode;
            _position = scene._cameraPosition;
            _facing = scene._cameraFacing;
            _up = scene._cameraUp;
            _right = scene._cameraRight;
            _fov = scene._cameraFov;
            _free = scene._freeCam;
            _subject = scene.Players.MainPlayerIndex;
            _view = scene._viewMatrix;
            _inv = scene._viewInvRotMatrix;
            _invY = scene._viewInvRotYMatrix;
            _projection = scene._perspectiveMatrix;
            _modelProjection = scene._viewModelPerspectiveMatrix;
            _modelFov = scene._viewModelFov;
            _frustumIndex = scene.FrustumInfo.Index;
            _frustumCount = scene.FrustumInfo.Count;
            _planes = scene.FrustumInfo.Planes.ToArray();
            _previewSize = scene.ReplayPreviewSize;
            _previewBounds = scene.ReplayPreviewBounds;
            _draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
            _read = GL.GetInteger(GetPName.ReadFramebufferBinding);
            _program = GL.GetInteger(GetPName.CurrentProgram);
            _activeTexture = GL.GetInteger(GetPName.ActiveTexture);
            _clientTexture = GL.GetInteger(GetPName.ClientActiveTexture);
            GL.GetInteger(GetPName.Viewport, _viewport);
            try
            {
                GL.PushAttrib(AttribMask.AllAttribBits);
                _pushed = true;
                MultiViewPasses.Add(scene, pass);
                _registered = true;
            }
            catch
            {
                if (_pushed) GL.PopAttrib();
                throw;
            }
        }

        internal void RestoreCamera()
        {
            _scene._cameraMode = _mode;
            _scene._cameraPosition = _position;
            _scene._cameraFacing = _facing;
            _scene._cameraUp = _up;
            _scene._cameraRight = _right;
            _scene._cameraFov = _fov;
            _scene._freeCam = _free;
            _scene.Players.MainPlayerIndex = _subject;
            _scene._viewMatrix = _view;
            _scene._viewInvRotMatrix = _inv;
            _scene._viewInvRotYMatrix = _invY;
            _scene._perspectiveMatrix = _projection;
            _scene._viewModelPerspectiveMatrix = _modelProjection;
            _scene._viewModelFov = _modelFov;
            _scene.FrustumInfo.Index = _frustumIndex;
            _scene.FrustumInfo.Count = _frustumCount;
            Array.Copy(_planes, _scene.FrustumInfo.Planes, _planes.Length);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                RestoreCamera();
                _scene.ReplayPreviewSize = _previewSize;
                _scene.ReplayPreviewBounds = _previewBounds;
                // Other tiles replace pooled draw items. Rebuild the original
                // picture's retained items without overwriting the composed tiles.
                Pass.Index = 0;
                if (_registered) _scene.OnDrawFrame();
            }
            finally
            {
                try { RestoreCamera(); }
                finally
                {
                    if (_registered) MultiViewPasses.Remove(_scene);
                    try { if (_pushed) GL.PopAttrib(); }
                    finally
                    {
                        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _draw);
                        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _read);
                        GL.UseProgram(_program);
                        GL.ActiveTexture((TextureUnit)_activeTexture);
                        OpenTK.Graphics.OpenGL.GL.ClientActiveTexture((TextureUnit)_clientTexture);
                        GL.Viewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
                    }
                }
            }
        }
    }

    private bool ApplyNativeReplayMultiViewCamera()
    {
        if (!MultiViewPasses.TryGetValue(this, out var pass)) return false;
        if (pass.Index == 0) return true;
        if (pass.Index == 2)
        {
            Players.MainPlayerIndex = pass.Subject;
            _cameraMode = CameraMode.Player;
            _freeCam = false;
            return true;
        }
        SetReplicaCamera(pass.Position, pass.Target, pass.Fov);
        _cameraFov = MathHelper.DegreesToRadians(pass.Fov);
        _cameraUp = pass.Up;
        _cameraRight = Vector3.Cross(_cameraFacing, pass.Up).Normalized();
        _viewMatrix = Matrix4.LookAt(pass.Position, pass.Target, pass.Up);
        _viewInvRotMatrix = Matrix4.Transpose(_viewMatrix.ClearTranslation());
        return true;
    }

    private int NativeReplayTargetPovSlot(int primary)
    {
        if (ReplayCombatDiagnostics.Selected is { } selected && ReplayPoses is { } poses
            && poses.TryCombatDiagnostic(selected, out var shot)
            && Services is ReplaySceneServices replay
            && replay.State.MatchesLife(shot.Fact.VictimSlot, shot.Fact.VictimGeneration, shot.Fact.VictimLifeId)
            && Players.Items[shot.Fact.VictimSlot].LoadFlags.TestFlag(LoadFlags.Spawned))
            return shot.Fact.VictimSlot;
        for (int offset = 1; offset < Players.Items.Count; offset++)
        {
            int slot = (primary + offset) % Players.Items.Count;
            if (Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Spawned)) return slot;
        }
        return primary;
    }

    // A false return means no four-view was rendered (seeking/export/unavailable).
    // Render failures throw to the native Studio boundary, which disables the
    // optional preview without tearing down the replay presentation owner.
    internal bool DrawNativeReplayFourViews(Vector4i bounds, Vector2i framebuffer)
    {
        if (!DemoPlayback.Owns(this) || !ReferenceEquals(DemoPlayback.PresentationScene, this)
            || ReplayController.IsSeeking || ReplayVideoExporter.Active || NativeReplayMultiViewActive
            || bounds.Z < 4 || bounds.W < 4) return false;

        int subject = Players.MainPlayerIndex;
        var view = _viewMatrix;
        var projection = _perspectiveMatrix;
        bool primaryDecoded = ReplayCameraPresentationMath.TryDecode(view, projection, out var primary);
        var position = _cameraPosition;
        var facing = _cameraFacing;
        var up = _cameraUp;
        var target = Players.Main.Position + Vector3.UnitY;
        var actorFacing = Players.Main.FacingVector;
        var pass = new MultiViewPass { Subject = NativeReplayTargetPovSlot(subject) };
        using (var scope = new ReplayAuxiliaryRenderScope(this, pass))
        {
            ReplayPreviewSize = framebuffer;
            int leftWidth = bounds.Z / 2, topHeight = bounds.W / 2;
            for (int index = 0; index < 4; index++)
            {
                scope.RestoreCamera();
                pass.Index = index;
                pass.Fov = index == 0 && primaryDecoded ? MathHelper.RadiansToDegrees(primary.Fov) : 70;
                pass.Position = index switch
                {
                    0 => primaryDecoded ? primary.Position : position,
                    1 => target - actorFacing * 5 + Vector3.UnitY * 2,
                    2 => target + new Vector3(8, 4, 0),
                    _ => target + new Vector3(0, 30, 15)
                };
                pass.Target = index == 0 ? pass.Position + facing : target;
                pass.Up = index == 0 ? up : Vector3.UnitY;
                bool rightTile = index % 2 != 0, bottomTile = index / 2 != 0;
                ReplayPreviewBounds = new(
                    bounds.X + (rightTile ? leftWidth : 0),
                    bounds.Y + (bottomTile ? topHeight : 0),
                    rightTile ? bounds.Z - leftWidth : leftWidth,
                    bottomTile ? bounds.W - topHeight : topHeight);
                try
                {
                    OnDrawFrame();
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_REPLAY_PREVIEW_FAILURE_FIXTURE
                    if (NativeReplayMultiViewFailureForTests?.Invoke(index) is { } injectedFailure)
                        throw injectedFailure;
#endif
                    if (!OnRenderFrame())
                        throw new InvalidOperationException("The scene renderer did not complete the tile.");
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    throw new InvalidOperationException(
                        $"Replay auxiliary camera {index + 1}/4 failed at recording frame {ReplayController.CurrentFrame}.", error);
                }
            }
        }
        return true;
    }
}
#endif
