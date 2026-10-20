using MphRead.Mods.Cosmetics;

namespace MphRead.Mods.Launcher;

/// <summary>One effects policy for the chamber shader and foreground layers.</summary>
public readonly record struct MenuEnvironmentBudget(
    int Detail, int ParticleCount, int TelemetryRows, int ServiceLamps,
    float Atmosphere, bool Animate, bool HunterMotifs)
{
    public static MenuEnvironmentBudget For(CosmeticEffectQuality quality, bool reduced, bool android = false)
    {
        if (android && quality > CosmeticEffectQuality.Low) quality = CosmeticEffectQuality.Low;
        return quality switch
        {
            CosmeticEffectQuality.Off => new(0, 0, 0, 0, 0, false, false),
            CosmeticEffectQuality.Low => new(1, 4, 3, 2, .35f, !reduced, false),
            CosmeticEffectQuality.High => new(3, 18, 7, 8, 1f, !reduced, true),
            _ => new(2, 10, 5, 4, .65f, !reduced, true)
        };
    }
}
