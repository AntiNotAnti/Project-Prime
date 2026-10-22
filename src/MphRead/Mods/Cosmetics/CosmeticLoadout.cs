namespace MphRead.Mods.Cosmetics
{
    public sealed record CosmeticLoadout(string SkinKey, string ArmorEffectKey, string DeathEffectKey,
        string BeamEffectKey = "beam.ion_fracture", byte BeamColor = 0)
    {
        public static readonly CosmeticLoadout Default = new("skin.default", "armor.none", "death.classic");
    }
}
