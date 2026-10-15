using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Hud;

/// <summary>
/// Authored, local-only stacked HUD colors. Profiles own the values and
/// HudRuntimeProfile publishes pre-parsed, immutable GL-ready colors.
/// Important colors (including real-health warning) retain independent slots.
/// </summary>
public sealed class HudThemeProfile
{
    public string Background { get; set; } = "#040913";
    public string Panel { get; set; } = "#071423";
    public string Accent { get; set; } = "#9CDBFA";
    public string Muted { get; set; } = "#3B78A8";
    public string Text { get; set; } = "#D7EBFA";
    public string Pressed { get; set; } = "#EBF2FF";
    public string Warning { get; set; } = "#FF7470";
    public string HealthTrack { get; set; } = "#213347";
    public float PanelOpacity { get; set; } = 1f;
    public float AccentOpacity { get; set; } = .81f;
    public float MutedOpacity { get; set; } = .44f;
    public float DecorationOpacity { get; set; } = .065f;
    public float HealthTrackOpacity { get; set; } = .76f;

    public void Validate()
    {
        Background = HudColor.Normalize(Background);
        Panel = HudColor.Normalize(Panel);
        Accent = HudColor.Normalize(Accent);
        Muted = HudColor.Normalize(Muted);
        Text = HudColor.Normalize(Text);
        Pressed = HudColor.Normalize(Pressed);
        Warning = HudColor.Normalize(Warning);
        HealthTrack = HudColor.Normalize(HealthTrack);
        PanelOpacity = HudProfile.Clamp(PanelOpacity, 0f, 1f, 1f);
        AccentOpacity = HudProfile.Clamp(AccentOpacity, 0f, 1f, .81f);
        MutedOpacity = HudProfile.Clamp(MutedOpacity, 0f, 1f, .44f);
        DecorationOpacity = HudProfile.Clamp(DecorationOpacity, 0f, .5f, .065f);
        HealthTrackOpacity = HudProfile.Clamp(HealthTrackOpacity, 0f, 1f, .76f);
    }
}

/// <summary>Immutable render presentation; never parses JSON or hex per frame.</summary>
public readonly struct HudThemeRuntime
{
    public readonly Vector4 Background, Panel, Accent, Muted, Text, Pressed;
    public readonly Vector4 Warning, HealthTrack, Decoration;

    public HudThemeRuntime(HudThemeProfile profile)
    {
        profile.Validate();
        static Vector4 Color(string hex, float alpha)
        {
            var c = HudColor.Parse(hex);
            return new Vector4(c.X, c.Y, c.Z, alpha);
        }
        Background = Color(profile.Background, 1f);
        Panel = Color(profile.Panel, profile.PanelOpacity);
        Accent = Color(profile.Accent, profile.AccentOpacity);
        Muted = Color(profile.Muted, profile.MutedOpacity);
        Text = Color(profile.Text, 1f);
        Pressed = Color(profile.Pressed, 1f);
        Warning = Color(profile.Warning, .92f);
        HealthTrack = Color(profile.HealthTrack, profile.HealthTrackOpacity);
        Decoration = Color(profile.Muted, profile.DecorationOpacity);
    }
}
