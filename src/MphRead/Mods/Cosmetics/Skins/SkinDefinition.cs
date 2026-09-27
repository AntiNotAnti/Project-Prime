namespace MphRead.Mods.Cosmetics.Skins
{
    public sealed record SkinDefinition(string Key, ushort WireId, string DisplayName, Hunter? Hunter)
        : CosmeticDefinition(Key, WireId, DisplayName)
    {
        public string? AlbedoSet { get; init; }
        public string? NormalSet { get; init; }
        public string? SpecularSet { get; init; }
        public string? EmissiveSet { get; init; }
        public string? AltFormAssets { get; init; }
        public string? GunAssets { get; init; }
        public string? TurretAssets { get; init; }
        public TeamAccentPolicy TeamAccentPolicy { get; init; } = TeamAccentPolicy.AccentAndOutline;
        // Built-in material treatments need no redistributed cartridge textures.
        public int SurfaceTreatment { get; init; }
    }
}
