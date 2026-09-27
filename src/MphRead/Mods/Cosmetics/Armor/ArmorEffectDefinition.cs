using OpenTK.Mathematics;
namespace MphRead.Mods.Cosmetics.Armor
{
    public sealed record ArmorEffectDefinition(string Key, ushort WireId, string DisplayName)
        : CosmeticDefinition(Key, WireId, DisplayName)
    {
        public SurfaceStyle ShaderStyle { get; init; }
        public Vector3 PrimaryColor { get; init; }
        public Vector3 SecondaryColor { get; init; }
        public float Intensity { get; init; } = 0.75f;
        public float PulseSpeed { get; init; } = 2;
        public float ScrollSpeed { get; init; } = 1;
        public ParticleStyle ParticleStyle { get; init; }
        public float ParticleRate { get; init; } = 5;
        public bool DynamicLight { get; init; }
        public float DynamicLightRadius { get; init; } = 2;
        public float DynamicLightIntensity { get; init; } = 0.15f;
        public string? AudioLoop { get; init; }
        public float FirstPersonIntensity { get; init; } = 0.5f;
        public bool FirstPersonParticles { get; init; }
        public bool SupportsAltForm { get; init; } = true;
        public float AltFormIntensity { get; init; } = 0.7f;
    }
}
