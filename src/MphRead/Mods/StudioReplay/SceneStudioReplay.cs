using System;
using System.Collections.Generic;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Hud;
using MphRead.Mods.Chat;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    internal bool? StudioReplayGameHud { get; set; }
    internal Mods.Network.ReplayCombatDiagnostic? StudioReplayCombat { get; set; }
    internal bool? StudioReplayCombatRays { get; set; }
    internal bool? StudioReplayShooterView { get; set; }
    internal IReadOnlyList<(Vector3 Position, Vector3 Color, float Size)>? StudioReplayOverlayPoints { get; set; }
    internal string? StudioReplayOverlayText { get; set; }
    internal float StudioReplayOverlayProgress { get; set; }
    private HudObjectInstance? _studioReplayFont;
    internal void DrawStudioReplayHud()
    {
        if (StudioReplayOverlayText == null) return;
        if (_studioReplayFont == null)
        {
            _studioReplayFont = new HudObjectInstance(ChatFont.Cell, ChatFont.Cell) { Enabled = true };
            _studioReplayFont.SetPaletteData([new ColorRgba(), new ColorRgba(255, 255, 255, 255)], this);
            _studioReplayFont.SetCharacterData(ChatFont.Pixels, this);
        }
        DrawHudFlatBox(4, 174, 252, 190, new Vector4(0, 0, 0, .72f));
        DrawHudFlatBox(7, 185, 249, 187, new Vector4(.2f, .24f, .28f, 1));
        DrawHudFlatBox(7, 185, 7 + 242 * Math.Clamp(StudioReplayOverlayProgress, 0, 1), 187, new Vector4(.2f, .85f, .6f, 1));
        float x = 7;
        foreach (char character in StudioReplayOverlayText)
        {
            int index = ChatFont.Index(character); if (index < 0) continue;
            _studioReplayFont.PositionX = x / 256; _studioReplayFont.PositionY = 177f / 192;
            _studioReplayFont.SetData(index, new ColorRgba(210, 255, 225, 255), this);
            DrawHudObject(_studioReplayFont, mode: 1, scale: .55f);
            x += ChatFont.Widths[index] * .55f; if (x > 245) break;
        }
    }
    internal void DrawStudioReplayOverlays()
    {
        if (StudioReplayOverlayPoints == null) return;
        foreach (var point in StudioReplayOverlayPoints)
            AddSingleParticle(SingleType.Fuzzball, point.Position, point.Color, alpha: .8f, scale: point.Size);
    }
    internal Vector3 AdjustStudioCameraPosition(Vector3 anchor, Vector3 desired)
    {
        CollisionResult collision = default;
        if ((desired - anchor).LengthSquared < 10000
            && CollisionDetection.CheckBetweenPoints(anchor, desired, TestFlags.Players, this, ref collision))
            return anchor + (desired - anchor) * Math.Max(0, collision.Distance - .05f);
        return desired;
    }
    internal void SetStudioReplayCamera(Vector3 position, Vector3 target, Vector3 up, float fov)
    {
        SetReplicaCamera(position, target, fov);
        if (!float.IsFinite(up.LengthSquared) || up.LengthSquared < .00001f) return;
        up = up.Normalized();
        if (MathF.Abs(Vector3.Dot(_cameraFacing, up)) > .999f) return;
        _cameraUp = up; _cameraRight = Vector3.Cross(_cameraFacing, up).Normalized();
        _viewMatrix = Matrix4.LookAt(position, target, up);
        _viewInvRotMatrix = Matrix4.Transpose(_viewMatrix.ClearTranslation());
    }
    internal void SetStudioReplayPlayerCamera(int slot)
    {
        if (!Services.IsReplica) throw new InvalidOperationException("Studio cameras require a private replica scene.");
        Players.MainPlayerIndex = Math.Clamp(slot, 0, Players.Items.Count - 1);
        _cameraMode = CameraMode.Player;
        // TransformCamera writes this scene's view uniform. A previous viewport
        // or postprocess pass may have left a different scene/program bound.
        GL.UseProgram(_shaderProgramId);
        global::MphRead.Entities.PlayerEntity main = Players.Main;
        if (StudioReplayGameHud == true && !Mods.Headless.Active
            && main.LoadFlags.TestFlag(global::MphRead.Entities.LoadFlags.Active))
        {
            if (!main.HudReady) main.SetUpHud();
            main.PrepareReplicaHudPresentation();
        }
        TransformCamera(); UpdateCameraPosition();
    }
}
