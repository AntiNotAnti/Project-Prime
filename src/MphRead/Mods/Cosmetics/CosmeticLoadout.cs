namespace MphRead.Mods.Cosmetics
{
    public sealed record CosmeticLoadout(string SkinKey, string ArmorEffectKey, string DeathEffectKey)
    {
        public static readonly CosmeticLoadout Default = new("skin.default", "armor.none", "death.classic");
    }
}
