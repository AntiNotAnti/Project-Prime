using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Presentation-only values from the local solver. Negative efficiency means
// no air wish input (or no applicable acceleration measurement) this tick.
internal readonly record struct MovementTelemetrySample(float Speed, float StrafeEfficiency, bool Airborne);

internal static class MovementTelemetryHud
{
    private static readonly ConditionalWeakTable<Scene, FontState> Fonts = new();

    internal static (string Speed, string Efficiency) Format(MovementTelemetrySample sample)
    {
        string speed = float.IsFinite(sample.Speed) && sample.Speed >= 0
            ? sample.Speed.ToString("0.0", CultureInfo.InvariantCulture) : "--";
        string efficiency = sample.Airborne && float.IsFinite(sample.StrafeEfficiency) && sample.StrafeEfficiency >= 0
            ? (Math.Clamp(sample.StrafeEfficiency, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
        return ("SPEED " + speed + " U/S", "AIR EFF " + efficiency);
    }

    internal static void Draw(Scene scene)
    {
        if (!MovementModelRules.UsesMomentumSolver(scene.GameState.MovementModel)
            || !scene.MomentumMovementActive || scene.Services.IsReplica || DemoPlayback.IsActive
            || scene.SideScene || scene.IsReplayLab || scene.AimTrainer != null || scene.GameState.MenuPause
            || scene.CameraMode != CameraMode.Player || scene.MovementTelemetry is not { } sample)
            return;
        PlayerEntity? player = scene.Players.Main;
        if (player == null || player.IsBot || !player.ModMomentumEligible || !player.LoadFlags.TestFlag(LoadFlags.Active)
            || !scene.MomentumOwnsPlayer(player) || NetHooks.IsPuppet(player)) return;

        var lines = Format(sample);
        FontState state = Fonts.GetValue(scene, owner => new FontState(owner));
        scene.DrawHudFlatBox(78, 166, 178, 182, new Vector4(.02f, .07f, .11f, .65f));
        state.Text(scene, 82, 168, lines.Speed);
        state.Text(scene, 82, 175, lines.Efficiency);
    }

    private sealed class FontState
    {
        private readonly HudObjectInstance _font = new(ChatFont.Cell, ChatFont.Cell) { Enabled = true };
        internal FontState(Scene scene)
        {
            _font.SetPaletteData(new[] { new ColorRgba(), new ColorRgba(255, 255, 255, 255) }, scene);
            _font.SetCharacterData(ChatFont.Pixels, scene);
        }
        internal void Text(Scene scene, float x, float y, string text)
        {
            const float scale = .45f;
            _font.Alpha = 1;
            foreach (char ch in text)
            {
                int glyph = ChatFont.Index(ch);
                if (glyph < 0) continue;
                _font.PositionX = x / 256;
                _font.PositionY = y / 192;
                _font.SetData(glyph, new ColorRgba(220, 243, 255, 255), scene);
                scene.DrawHudObject(_font, mode: 1, scale: scale);
                x += ChatFont.Widths[glyph] * scale;
            }
        }
    }
}
