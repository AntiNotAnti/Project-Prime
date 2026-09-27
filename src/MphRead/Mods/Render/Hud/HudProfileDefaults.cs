using System.Collections.Generic;
namespace MphRead.Mods.Render.Hud;

public static class HudProfileDefaults
{
    public static readonly string[] ElementIds = { "core.crosshair", "core.health", "core.ammo", "core.weapons", "core.radar", "match.score", "match.timer", "combat.killFeed", "combat.notifications", "combat.opponent", "match.objective", "system.diagnostics", "core.healthGauge", "core.ammoGauge" };
    public static readonly string[] Presets = { "Classic", "Project Prime", "Competitive", "Minimal", "Duel", "Accessibility", "Broadcast" };
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
            case "Competitive": p.Crosshair = CrosshairProfile.FromLegacy(CrosshairStyle.Dot, CrosshairSize.Small); break;
            case "Minimal": p.Elements["core.weapons"].Enabled = false; p.Elements["combat.notifications"].Enabled = false; break;
            case "Duel": p.Elements["core.radar"].Enabled = false; break;
            case "Accessibility":
                p.GlobalScale = 1.5f; p.Crosshair.Outline = 2;
                foreach (var e in p.Elements.Values) { e.OffsetX *= 1.5f; e.OffsetY *= 1.5f; }
                p.Elements["core.weapons"].Scale = 2 / 3f;
                p.Elements["core.weapons"].OffsetY = 180;
                p.Elements["match.score"].Anchor = HudAnchor.TopCenter;
                p.Elements["match.score"].OffsetX = -180;
                p.Elements["match.score"].OffsetY = 40;
                break;
            case "Broadcast": p.Elements["match.score"].Scale = 1.5f; break;
        }
        return p;
    }
}
