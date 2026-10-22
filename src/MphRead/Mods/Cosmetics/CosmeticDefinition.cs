using OpenTK.Mathematics;
namespace MphRead.Mods.Cosmetics
{
    public enum CosmeticRarity { Standard, Rare, Elite, Legendary }
    public enum UnlockType { Default, Always, CareerKills, CareerWins, Headshots, Achievement, Developer, Event }
    public enum TeamAccentPolicy { Native, AccentOnly, OutlineOnly, AccentAndOutline }
    public enum SkinContext { Biped, ViewModel, AltForm, Halfturret }
    public enum CosmeticEffectQuality { Off, Low, Medium, High }
    public enum CosmeticLod { Near, Medium, Far, Hidden }
    public enum SurfaceStyle { None, EmissionPulse, Fresnel, AnimatedMask, NoiseDissolve, Scanline, EnergyCracks, Hologram, ColorFlow, Distortion }
    public enum ParticleStyle { None, Sparks, Embers, Vapor, Ice, Wisps, Motes, Pixels, Shards }
    public abstract record CosmeticDefinition(string Key, ushort WireId, string DisplayName)
    {
        public string Description { get; init; } = "";
        public string Collection { get; init; } = "Founders";
        public string UnlockText { get; init; } = "Available to all hunters";
        public CosmeticRarity Rarity { get; init; } = CosmeticRarity.Standard;
        public UnlockType Unlock { get; init; } = UnlockType.Always;
        public int SortOrder { get; init; }
        public string? PreviewIcon { get; init; }
    }
}
