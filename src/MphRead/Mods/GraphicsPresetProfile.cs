namespace MphRead.Mods
{
    /// <summary>
    /// Shared, immutable preset recipe. Reading a recipe must not change live
    /// renderer state: the launcher edits a draft until the user saves it.
    /// Higher tiers buy cleaner sampling, not a progressively stronger grade.
    /// </summary>
    internal sealed record GraphicsPresetProfile
    {
        public int ResolutionScale { get; init; } = 100;
        public bool Lighting { get; init; } = true;
        public bool Fog { get; init; } = true;
        public bool TextureFiltering { get; init; } = false;
        public bool TextureMipmaps { get; init; } = false;
        public int TextureAnisotropy { get; init; } = 1;
        public TextureUpscaleMode TextureUpscale { get; init; } = TextureUpscaleMode.Off;
        public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Off;
        public TranslucencyMode Translucency { get; init; } = TranslucencyMode.Native;
        public int SharpenStrength { get; init; } = 0;
        public bool Bloom { get; init; } = false;
        public int BloomIntensity { get; init; } = 60;
        public ColorGradeProfile ColorGrade { get; init; } = ColorGradeProfile.Original;
        public int Gamma { get; init; } = 100;
        public int Contrast { get; init; } = 100;
        public int Saturation { get; init; } = 100;
        public bool EnhancedLighting { get; init; } = false;
        public bool AdvancedMaterials { get; init; } = false;
        public bool DeferredPbr { get; init; } = false;
        public ShadowQuality Shadows { get; init; } = ShadowQuality.Off;
        public AmbientOcclusionQuality AmbientOcclusion { get; init; } = AmbientOcclusionQuality.Off;
        public bool ContactShadows { get; init; } = false;
        public bool EnhancedFog { get; init; } = false;
        public bool VolumetricFog { get; init; } = false;
        public bool InternalHdr { get; init; } = false;
        public bool Reflections { get; init; } = false;
        public bool DynamicGlow { get; init; } = false;

        public static GraphicsPresetProfile? Get(GraphicsPreset preset)
        {
            var original = new GraphicsPresetProfile();
            // Keep native resolution even on the cheap path: sub-native FXAA
            // softens the cartridge textures before sharpening can recover them.
            var performance = original with
            {
                TextureFiltering = true, TextureMipmaps = true, TextureAnisotropy = 4,
                AntiAliasing = AntiAliasingMode.Fxaa, Translucency = TranslucencyMode.Fast,
                SharpenStrength = 10
            };
            // Preserve authored colors, fog and lighting. Glow lowers the bloom
            // threshold globally; PBR/SSR assume materials the cartridge lacks.
            // Those artistic choices remain available through Custom settings.
            var enhanced = performance with
            {
                TextureAnisotropy = 16, AntiAliasing = AntiAliasingMode.Smaa,
                Translucency = TranslucencyMode.Native,
                SharpenStrength = 10, Bloom = true, BloomIntensity = 20,
                AdvancedMaterials = true, AmbientOcclusion = AmbientOcclusionQuality.Low
            };
            var ultra = enhanced with
            {
                ResolutionScale = 150, SharpenStrength = 5,
                Shadows = ShadowQuality.High
            };
            return preset switch
            {
                GraphicsPreset.Original => original,
                GraphicsPreset.Performance => performance,
                GraphicsPreset.Enhanced => enhanced,
                GraphicsPreset.Ultra => ultra,
                GraphicsPreset.Extreme => ultra with
                {
                    ResolutionScale = 200, SharpenStrength = 0, Shadows = ShadowQuality.Ultra
                },
                _ => null
            };
        }
    }
}
