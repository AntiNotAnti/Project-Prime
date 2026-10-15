using System.Collections.Generic;
namespace MphRead.Mods.Render.Hud;

public static class HudProfileDefaults
{
    public static readonly string[] ElementIds = { "core.crosshair", "core.health", "core.ammo", "core.weapons", "core.radar", "match.score", "match.timer", "combat.killFeed", "combat.notifications", "combat.opponent", "match.objective", "system.diagnostics", "core.healthGauge", "core.ammoGauge" };
    public static readonly string[] Presets = { "Classic", "Project Prime", "Halo", "Approved 4:3", "Competitive", "Minimal", "Duel", "Accessibility", "Broadcast" };
    public static Dictionary<string, HudElementLayout> Elements() => new()
    {
        ["core.crosshair"] = new() { Anchor = HudAnchor.Center, Locked = true, Layer = 3 },
        ["core.health"] = new() { Anchor = HudAnchor.BottomLeft, OffsetX = 11.25f, OffsetY = -123.75f },
        ["core.ammo"] = new() { Anchor = HudAnchor.BottomRight, OffsetX = -337.5f, OffsetY = -123.75f },
        ["core.weapons"] = new() { Anchor = HudAnchor.TopLeft, OffsetX = 11.25f, OffsetY = 258.75f },
        ["core.radar"] = new() { Anchor = HudAnchor.TopRight, OffsetX = -141.849f, OffsetY = 169.974f },
        ["match.score"] = new() { OffsetX = 22.5f, OffsetY = 67.5f },
        ["match.timer"] = new() { Anchor = HudAnchor.TopCenter, OffsetY = 56.25f },
        ["combat.killFeed"] = new() { Anchor = HudAnchor.TopRight, OffsetX = -540, OffsetY = 351.5625f },
        ["combat.notifications"] = new() { Anchor = HudAnchor.TopCenter, OffsetY = 180 },
        ["combat.opponent"] = new() { Enabled = false, Anchor = HudAnchor.BottomCenter, OffsetX = -262.5f, OffsetY = -241.875f },
        ["match.objective"] = new() { Anchor = HudAnchor.TopLeft },
        ["system.diagnostics"] = new() { Anchor = HudAnchor.TopRight, OffsetX = -22.5f, OffsetY = 11.25f },
        ["core.healthGauge"] = new() { Anchor=HudAnchor.BottomLeft,OffsetX=22.5f,OffsetY=-33.75f },
        ["core.ammoGauge"] = new() { Anchor=HudAnchor.BottomRight,OffsetX=-326.25f,OffsetY=-33.75f }
    };
    public static HudProfile Create(string name)
    {
        var p = new HudProfile { Name = name, BasePreset = name, Mode = name == "Classic" ? HudMode.Classic : name == "Project Prime" ? HudMode.ProjectPrime : HudMode.Custom };
        switch (name)
        {
            case "Halo":
                ApplyHaloTheme(p, approved43: false);
                break;
            case "Approved 4:3":
                ApplyHaloTheme(p, approved43: true);
                break;
            case "Competitive": HudRadarStyles.Apply(p,HudRadarStyle.Competitive); p.Crosshair = CrosshairProfile.FromLegacy(CrosshairStyle.Dot, CrosshairSize.Small); break;
            case "Minimal": HudRadarStyles.Apply(p,HudRadarStyle.Minimal); p.Elements["core.weapons"].Enabled = false; p.Elements["combat.notifications"].Enabled = false; break;
            case "Duel": p.Elements["core.radar"].Enabled = false; break;
            case "Accessibility":
                HudRadarStyles.Apply(p,HudRadarStyle.Tactical); p.Radar.BlipScale=1.3f; p.Radar.BackgroundOpacity=.85f;
                p.GlobalScale = 1.5f; p.Crosshair.Outline = 2;
                foreach (var e in p.Elements.Values) { e.OffsetX *= 1.5f; e.OffsetY *= 1.5f; }
                p.Elements["core.weapons"].Scale = 2 / 3f;
                p.Elements["core.weapons"].OffsetY = 180;
                p.Elements["match.score"].Anchor = HudAnchor.TopCenter;
                p.Elements["match.score"].OffsetX = -180;
                p.Elements["match.score"].OffsetY = 40;
                break;
            case "Broadcast": HudRadarStyles.Apply(p,HudRadarStyle.Tactical); p.Elements["match.score"].Scale = 1.5f; break;
        }
        return p;
    }

    private static void ApplyHaloTheme(HudProfile p, bool approved43)
    {
        // This is an original, palette-led visual preset, not imported art.
        // Actual HP/ammunition, native weapon icons and semantic hit facts
        // remain sourced from the game. In particular no 'shield' resource.
        HudRadarStyles.Apply(p, HudRadarStyle.Tactical);
        p.Radar.BackgroundOpacity = .34f;
        p.Radar.BlipScale = 1.1f;
        p.RadarOutlines = true;
        p.Health.Native = false;
        p.Ammo.Native = false;
        p.Health.FullColor = "#7DDEFF";
        p.Health.WarningColor = "#FFC87A";
        p.Health.DangerColor = "#FF7470";
        p.Ammo.FullColor = "#A0EAFF";
        p.Inventory.SelectedColor = "#D3F2FF";
        p.Crosshair.HealthColor = false;
        p.Crosshair.Color = "#EBF8FF";
        p.Crosshair.Outline = 1.25f;
        p.Crosshair.OutlineColor = "#10243B";
        p.Elements["match.timer"].Color = "#DAF2FF";
        p.Elements["match.score"].Color = "#C2E9FF";
        p.Elements["combat.killFeed"].Color = "#D7ECFF";
        p.Theme = new HudThemeProfile
        {
            Background = "#051124",
            Panel = "#0B2342",
            Accent = "#B5E9FF",
            Muted = "#4A84B5",
            Text = "#D7F1FF",
            Pressed = "#F2FDFF",
            Warning = "#FF7470",
            HealthTrack = "#1B3853",
            PanelOpacity = .95f,
            AccentOpacity = .96f,
            MutedOpacity = .55f,
            DecorationOpacity = .08f
        };
        if (approved43)
        {
            // Approved 4:3 is a theme/layout preset, not a second viewport
            // implementation. The physical aspect still belongs to DsLayout.
            p.SafeArea = .025f;
            p.Theme.Background = "#07182A";
            p.Theme.Panel = "#0E2B45";
            p.Theme.Accent = "#E2F6FF";
            p.Theme.Muted = "#638CAA";
            p.Theme.DecorationOpacity = .06f;
            p.Elements["combat.killFeed"].Scale = 1.08f;
        }
    }
}
