using System;
using MphRead.Mods.Cosmetics;
namespace MphRead.Mods.Render;

// Presentation-only admission. It never changes the accepted death/cause,
// actor lifetime, native gameplay object or user-selected disappearance clock.
internal static class CorpsePresentationBudget
{
    internal static int Capacity(CosmeticEffectQuality quality)=>quality switch
    {CosmeticEffectQuality.Low=>2,CosmeticEffectQuality.Medium=>4,CosmeticEffectQuality.High=>8,_=>0};
    internal static float Distance(CosmeticEffectQuality quality)=>quality switch
    {CosmeticEffectQuality.Low=>24,CosmeticEffectQuality.Medium=>48,CosmeticEffectQuality.High=>72,_=>0};
    internal static bool Within(CosmeticEffectQuality quality,float squaredDistance)
    {float limit=Distance(quality);return limit>0&&float.IsFinite(squaredDistance)&&squaredDistance>=0&&squaredDistance<=limit*limit;}
}
