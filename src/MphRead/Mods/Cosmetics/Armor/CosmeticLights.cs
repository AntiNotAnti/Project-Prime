using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
namespace MphRead
{
    public partial class Scene
    {
        private readonly DynamicLightCandidate[] _cosmeticLights = new DynamicLightCandidate[4];
        private void CollectCosmeticLights(ref int count)
        {
            if (!RenderOptions.ShowCustomCosmetics || RenderOptions.CosmeticQuality < CosmeticEffectQuality.Medium) return;
            int limit = RenderOptions.CosmeticQuality == CosmeticEffectQuality.High ? 4 : 2;
            if (OperatingSystem.IsAndroid()) limit = Math.Min(limit, 2);
            int found = 0;
            foreach (var entity in Entities)
            {
                if (entity is not PlayerEntity player) continue;
                float distance = (player.Position - _cameraPosition).LengthSquared;
                if (distance > 18 * 18) continue;
                DynamicLightCandidate candidate;
                if (player.Health <= 0)
                {
                    if (!player.CosmeticDeathLight(out var color, out float strength)) continue;
                    candidate = new(distance, player.Position, color, 2, strength);
                }
                else
                {
                    if (!player.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson) || player.CosmeticMaterial(false).Effect == 0) continue;
                    var armor = CosmeticRuntime.Get(this, player.SlotIndex, player.Hunter, player.IsMainPlayer).Armor;
                    if (!armor.DynamicLight) continue;
                    candidate = new(distance, player.Position, armor.PrimaryColor,
                        armor.DynamicLightRadius, armor.DynamicLightIntensity);
                }
                int at = found;
                while (at > 0 && _cosmeticLights[at - 1].Distance > distance)
                { if (at < limit) _cosmeticLights[at] = _cosmeticLights[at - 1]; at--; }
                if (at < limit) { _cosmeticLights[at] = candidate; found = Math.Min(found + 1, limit); }
            }
            for (int i = 0; i < found && count < _dynamicLightScratch.Length; i++)
                _dynamicLightScratch[count++] = _cosmeticLights[i];
        }
    }
}
