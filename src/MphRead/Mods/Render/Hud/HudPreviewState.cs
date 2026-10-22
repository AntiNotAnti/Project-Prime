namespace MphRead.Mods.Render.Hud;

public enum HudPreviewScenario { Healthy, LowHealth, NoAmmo, Combat, Objective, Spectator, Zoomed }
public readonly record struct HudPreviewState(int Health, int Ammo, bool Combat, bool Objective, bool Spectator)
{
    public static HudPreviewState For(HudPreviewScenario scenario) => scenario switch
    {
        HudPreviewScenario.LowHealth => new(15,39,false,false,false),
        HudPreviewScenario.NoAmmo => new(74,0,false,false,false),
        HudPreviewScenario.Combat => new(45,39,true,false,false),
        HudPreviewScenario.Objective => new(74,39,false,true,false),
        HudPreviewScenario.Spectator => new(74,39,false,false,true),
        _ => new(99,80,false,false,false)
    };
}
