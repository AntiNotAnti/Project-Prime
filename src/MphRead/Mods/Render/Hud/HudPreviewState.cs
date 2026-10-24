namespace MphRead.Mods.Render.Hud;

public enum HudPreviewScenario { Healthy, LowHealth, NoAmmo, Combat, Objective, Spectator, Zoomed, CaptureZone, CaptureTheFlag, Payload, Oddball }
public readonly record struct HudPreviewState(int Health, int Ammo, bool Combat, bool Objective, bool Spectator)
{
    public static bool ElementVisible(string id, HudPreviewScenario scenario) => id switch
    {
        "match.rounds" => scenario is HudPreviewScenario.CaptureZone,
        "match.respawn" => scenario is HudPreviewScenario.CaptureZone or HudPreviewScenario.CaptureTheFlag or HudPreviewScenario.Payload or HudPreviewScenario.Oddball,
        "combat.abilityCooldown" => scenario == HudPreviewScenario.CaptureZone,
        _ when id.StartsWith("match.ctf", System.StringComparison.Ordinal) || id == "match.flagAction" => scenario == HudPreviewScenario.CaptureTheFlag,
        _ when id.StartsWith("match.oddball", System.StringComparison.Ordinal) => scenario == HudPreviewScenario.Oddball,
        _ => true
    };
    public static HudPreviewState For(HudPreviewScenario scenario) => scenario switch
    {
        HudPreviewScenario.LowHealth => new(15,39,false,false,false),
        HudPreviewScenario.NoAmmo => new(74,0,false,false,false),
        HudPreviewScenario.Combat => new(45,39,true,false,false),
        HudPreviewScenario.Objective => new(74,39,false,true,false),
        HudPreviewScenario.CaptureZone => new(220,80,true,true,false),
        HudPreviewScenario.CaptureTheFlag or HudPreviewScenario.Payload or HudPreviewScenario.Oddball => new(74,39,true,true,false),
        HudPreviewScenario.Spectator => new(74,39,false,false,true),
        _ => new(99,80,false,false,false)
    };
}
