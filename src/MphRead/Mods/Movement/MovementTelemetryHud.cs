using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Presentation-only values from the local solver. Negative efficiency means
// no air wish input (or no applicable acceleration measurement) this tick.
internal readonly record struct MovementTelemetrySample(float Speed, float StrafeEfficiency, bool Airborne,
    float TotalSpeed = float.NaN, bool Grounded = false, bool Crouched = false, bool Sliding = false)
{
    // Completed physical contact replaces the incoming solver contact phase.
    internal static MovementTelemetrySample Completed(float speed, float totalSpeed, float strafeEfficiency,
        bool grounded, bool crouched, bool sliding)
        => new(speed, grounded ? -1 : strafeEfficiency, !grounded, totalSpeed, grounded, crouched, sliding);
}

internal static class MovementTelemetryHud
{
    private static readonly ConditionalWeakTable<Scene, FontState> Fonts = new();

    internal static (string Speed, string Efficiency, string Details) Format(MovementTelemetrySample sample)
    {
        string speed = float.IsFinite(sample.Speed) && sample.Speed >= 0
            ? sample.Speed.ToString("0.0", CultureInfo.InvariantCulture) : "--";
        string efficiency = sample.Airborne && float.IsFinite(sample.StrafeEfficiency) && sample.StrafeEfficiency >= 0
            ? (Math.Clamp(sample.StrafeEfficiency, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
        string total = float.IsFinite(sample.TotalSpeed) && sample.TotalSpeed >= 0
            ? sample.TotalSpeed.ToString("0.0", CultureInfo.InvariantCulture) : "--";
        string state = !sample.Grounded ? "AIR" : sample.Sliding ? "SLIDING" : sample.Crouched ? "CROUCH" : "GROUND";
        return ("HORIZ " + speed + " U/S", "AIR EFF " + efficiency, "TOTAL " + total + " U/S | " + state);
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

        using var layout = scene.PushHudElement(HudProfileDefaults.MovementTelemetryIndex, 128, 166,
            player.HudElementVisible(HudProfileDefaults.MovementTelemetryIndex));
        FontState state = Fonts.GetValue(scene, owner => new FontState(owner));
        var lines = state.Lines.Get(sample);
        scene.DrawHudFlatBox(78, 166, 178, 166 + HudProfileDefaults.MovementTelemetryHeight, new Vector4(.02f, .07f, .11f, .65f));
        state.Text(scene, 82, 168, lines.Speed);
        state.Text(scene, 82, 175, lines.Details);
        state.Text(scene, 82, 182, lines.Efficiency);
    }

    // Caches only presentation text; render frequency cannot advance telemetry.
    internal sealed class TextCache
    {
        private MovementTelemetrySample? _source;
        private (string Speed, string Efficiency, string Details) _lines;
        internal (string Speed, string Efficiency, string Details) Get(MovementTelemetrySample sample)
        {
            if (_source is not { } source || source != sample)
            {
                _lines = Format(sample);
                _source = sample;
            }
            return _lines;
        }
    }

    private sealed class FontState
    {
        internal TextCache Lines { get; } = new();
        private readonly HudObjectInstance _font = new(ChatFont.Cell, ChatFont.Cell) { Enabled = true };
        internal FontState(Scene scene)
        {
            _font.SetPaletteData(new[] { new ColorRgba(), new ColorRgba(255, 255, 255, 255) }, scene);
            _font.SetCharacterData(ChatFont.Pixels, scene);
        }
        internal void Text(Scene scene, float x, float y, string text)
        {
            float scale = .45f * (HudProfiles.Runtime.Mode == HudMode.Custom ? HudProfiles.Runtime.TextScale : 1);
            _font.Alpha = 1;
            foreach (char ch in text)
            {
                int glyph = ChatFont.Index(ch);
                if (glyph < 0) continue;
                _font.PositionX = x / 256;
                _font.PositionY = y / 192;
                _font.SetData(glyph, scene.HudTextColor(new ColorRgba(220, 243, 255, 255))!.Value, scene);
                scene.DrawHudObject(_font, mode: 1, scale: scale, isText: true);
                x += ChatFont.Widths[glyph] * scale;
            }
        }
    }
}
