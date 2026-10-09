using System;

namespace MphRead.Mods.Render
{
    internal static class GraphicsOptionsCheck
    {
        public static int Run()
        {
            try
            {
                void Check(bool ok, string name)
                {
                    if (!ok) throw new InvalidOperationException(name);
                }

                RenderOptions.TextureReplacements = true;
                RenderOptions.CharacterModelReplacements = true;
                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
                Check(RenderOptions.Preset == GraphicsPreset.Original
                    && RenderOptions.ResolutionScale == 100
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Off
                    && RenderOptions.Shadows == ShadowQuality.Off
                    && !RenderOptions.TextureReplacements
                    && !RenderOptions.CharacterModelReplacements
                    && !RenderOptions.PostProcessingEnabled,
                    "original preset remains the unprocessed compatibility path");

                foreach (var preset in new[] { GraphicsPreset.Performance, GraphicsPreset.Enhanced,
                    GraphicsPreset.Ultra, GraphicsPreset.Extreme })
                {
                    // Poison every optional effect first: a preset must clear
                    // a previous custom/maximal look, not inherit its switches.
                    RenderOptions.DeferredPbr = RenderOptions.Reflections = true;
                    RenderOptions.EnhancedFog = RenderOptions.VolumetricFog = true;
                    RenderOptions.InternalHdr = RenderOptions.DynamicGlow = true;
                    RenderOptions.ContactShadows = RenderOptions.EnhancedLighting = true;
                    RenderOptions.ColorGrade = ColorGradeProfile.Cinematic;
                    RenderOptions.Contrast = 150; RenderOptions.Saturation = 200;
                    RenderOptions.TextureUpscale = TextureUpscaleMode.Scale4x;
                    RenderOptions.ApplyGraphicsPreset(preset);
                    Check(!RenderOptions.DeferredPbr && !RenderOptions.Reflections
                        && !RenderOptions.EnhancedFog && !RenderOptions.VolumetricFog
                        && !RenderOptions.InternalHdr && !RenderOptions.DynamicGlow
                        && !RenderOptions.ContactShadows && !RenderOptions.EnhancedLighting
                        && RenderOptions.ColorGrade == ColorGradeProfile.Original
                        && RenderOptions.Gamma == 100 && RenderOptions.Contrast == 100
                        && RenderOptions.Saturation == 100
                        && RenderOptions.TextureUpscale == TextureUpscaleMode.Off,
                        $"{preset} clears stacked lighting, atmosphere and grading");
                    // Every shared draft field must also reach the live renderer.
                    var profile = GraphicsPresetProfile.Get(preset)!;
                    foreach (var field in typeof(GraphicsPresetProfile).GetProperties())
                    {
                        Check(Equals(field.GetValue(profile),
                            typeof(RenderOptions).GetProperty(field.Name)!.GetValue(null)),
                            $"{preset} applies {field.Name} consistently");
                    }
                }

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Performance);
                Check(RenderOptions.ResolutionScale == 100
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Fxaa
                    && RenderOptions.TextureAnisotropy == 4
                    && !RenderOptions.NeedsReadableDepth && !RenderOptions.Bloom,
                    "performance preserves native detail without depth effects");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Enhanced);
                Check(RenderOptions.ResolutionScale == 100
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Smaa
                    && RenderOptions.AmbientOcclusion == AmbientOcclusionQuality.Low
                    && RenderOptions.Bloom && RenderOptions.BloomIntensity == 20
                    && RenderOptions.Shadows == ShadowQuality.Off
                    && RenderOptions.AdvancedMaterials && RenderOptions.NeedsReadableDepth,
                    "enhanced uses subtle bloom and occlusion at native resolution");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Ultra);
                Check(RenderOptions.ResolutionScale == 150
                    && RenderOptions.Shadows == ShadowQuality.High,
                    "ultra spends quality on supersampling and shadows");
                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme);
                Check(RenderOptions.ResolutionScale == 200
                    && RenderOptions.Shadows == ShadowQuality.Ultra
                    && RenderOptions.SharpenStrength == 0,
                    "extreme uses 2x supersampling without sharpening halos");

                var before = RenderOptions.ResolutionScale;
                Check(GraphicsPresetProfile.Get(GraphicsPreset.Original) != null
                    && RenderOptions.ResolutionScale == before,
                    "reading a launcher draft does not mutate live rendering");
                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Custom);
                Check(RenderOptions.ResolutionScale == before
                    && GraphicsPresetProfile.Get(GraphicsPreset.Custom) == null,
                    "custom preserves manually tuned values");

                RenderOptions.AntiAliasing = AntiAliasingMode.Taa;
                Check(RenderOptions.PostProcessingEnabled,
                    "TAA remains available as an explicit temporal reprojection mode");

                Check(MphRead.Scene.ResolvePostProcessAntiAliasing(
                        AntiAliasingMode.Taa, hdrRequested: false, pbrAvailable: false)
                        == AntiAliasingMode.Taa
                    && MphRead.Scene.ResolvePostProcessAntiAliasing(
                        AntiAliasingMode.Taa, hdrRequested: true, pbrAvailable: false)
                        == AntiAliasingMode.Smaa
                    && MphRead.Scene.ResolvePostProcessAntiAliasing(
                        AntiAliasingMode.Taa, hdrRequested: false, pbrAvailable: true)
                        == AntiAliasingMode.Smaa,
                    "TAA avoids allocating rejected history for HDR/PBR and falls back to SMAA");

                var supersampled = new OpenTK.Mathematics.Vector2i(5760, 3240);
                var presentation = new OpenTK.Mathematics.Vector2i(3840, 2160);
                Check(MphRead.Scene.ResolveGraphicsProcessingSize(
                        supersampled, presentation, taaActive: false) == presentation
                    && MphRead.Scene.ResolveGraphicsProcessingSize(
                        supersampled, presentation, taaActive: true) == supersampled,
                    "supersampled worlds resolve expensive post processing at presentation size unless TAA needs full history");

                // Exercise individual on -> off transitions independently of presets.
                foreach (string toggle in new[] { "Bloom", "EnhancedLighting", "DeferredPbr", "ContactShadows",
                    "EnhancedFog", "VolumetricFog", "InternalHdr", "Reflections", "DynamicGlow" })
                {
                    RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
                    var property = typeof(RenderOptions).GetProperty(toggle)!;
                    property.SetValue(null, true);
                    Check(RenderOptions.PostProcessingEnabled, toggle + " enables processing");
                    property.SetValue(null, false);
                    Check(!RenderOptions.PostProcessingEnabled && !RenderOptions.NeedsReadableDepth,
                        toggle + " releases processing and readable depth when off");
                }
                RenderOptions.Bloom = true; RenderOptions.BloomIntensity = 0;
                Check(!RenderOptions.PostProcessingEnabled, "zero-intensity bloom does not run a pass");
                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);

                uint pixel = 0x7F3366CCu;
                uint[] scaled = TextureUpscaler.Scale(new[] { pixel }, 1, 1, 4,
                    out int scaledWidth, out int scaledHeight);
                Check(scaledWidth == 4 && scaledHeight == 4 && scaled.Length == 16,
                    "Scale4x produces four-by-four output from one source texel");
                for (int i = 0; i < scaled.Length; i++)
                {
                    Check(scaled[i] == pixel, "uniform source color survives edge-aware upscaling");
                }

                var settings = new MenuSettings
                {
                    SettingsSchemaVersion = 0,
                    Renderer = "vulkan",
                    ResolutionScale = "9999",
                    AntiAliasing = "not-a-mode",
                    ShadowQuality = "ultra",
                    TextureUpscale = "scale4x",
                    TextureQuality = "impossible",
                    Gamma = "999",
                    AdvancedMaterials = "yes",
                    InternalHdr = "true",
                    CharacterModelReplacements = "yes"
                };
                Check(SettingsMigration.Apply(settings, out string summary)
                    && settings.SettingsSchemaVersion == SettingsMigration.CurrentSchema
                    && settings.Renderer == "OpenGL"
                    && settings.ResolutionScale == RenderOptions.MaxScale.ToString()
                    && settings.AntiAliasing == "off"
                    && settings.ShadowQuality == "ultra"
                    && settings.TextureUpscale == "scale4x"
                    && settings.TextureQuality == "automatic"
                    && settings.TextureSampling == "auto"
                    && settings.Gamma == "150"
                    && settings.AdvancedMaterials == "on"
                    && settings.InternalHdr == "on"
                    && settings.CharacterModelReplacements == "on"
                    && summary.Length > 0,
                    "graphics settings migration clamps and normalizes new options");

                var retiredRendererSettings = new MenuSettings
                {
                    SettingsSchemaVersion = SettingsMigration.CurrentSchema,
                    Renderer = "DirectX12"
                };
                Check(SettingsMigration.Apply(retiredRendererSettings, out string rendererSummary)
                    && retiredRendererSettings.Renderer == "OpenGL"
                    && rendererSummary.Contains("renderer", StringComparison.OrdinalIgnoreCase)
                    && !SettingsMigration.Apply(retiredRendererSettings, out _),
                    "retired persisted renderer is migrated once and remains idempotent");

                var previousHdSettings = new MenuSettings
                {
                    SettingsSchemaVersion = 7,
                    TextureReplacements = "on",
                    TextureFiltering = "off",
                    TextureMipmaps = "off",
                    TextureAnisotropy = "1"
                };
                Check(SettingsMigration.Apply(previousHdSettings, out _)
                    && previousHdSettings.TextureSampling == "custom",
                    "schema 7 HD users retain their previous manual sampling behavior");

                Check(TextureAssetManager.RequestedDimensionLimit(TextureAssetQuality.Low, TextureAssetClass.Hunter) == 1024
                    && TextureAssetManager.RequestedDimensionLimit(TextureAssetQuality.Medium, TextureAssetClass.Hunter) == 2048
                    && TextureAssetManager.RequestedDimensionLimit(TextureAssetQuality.High, TextureAssetClass.Hunter) == 4096
                    && TextureAssetManager.RequestedDimensionLimit(TextureAssetQuality.Ultra, TextureAssetClass.Hunter) == 8192
                    && TextureAssetManager.RequestedDimensionLimit(TextureAssetQuality.Ultra, TextureAssetClass.Effect) == 4096,
                    "modern texture policy exposes 1K/2K/4K/8K tiers and a bounded FX tier");

                const long MiB = 1024L * 1024;
                Check(TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.Automatic, android: false) == 1024 * MiB
                    && TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.High, android: false) == 1024 * MiB
                    && TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.Ultra, android: false) == 3072 * MiB
                    && TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.Automatic, android: true) == 256 * MiB
                    && TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.High, android: true) == 384 * MiB
                    && TextureAssetManager.WorldMaterialMemoryBudgetBytesForQuality(
                        TextureAssetQuality.Ultra, android: true) == 768 * MiB,
                    "world material VRAM admission is quality-aware and conservative on Android");

                RenderOptions.TextureSampling = TextureSamplingMode.Auto;
                TextureSamplerDescriptor autoSampling = TextureSamplingPolicy.ResolveModern(
                    TextureAssetClass.World, TextureAssetChannel.Albedo);
                Check(autoSampling.LinearMagnification && autoSampling.LinearMinification
                    && autoSampling.Mipmaps
                    && autoSampling.Anisotropy == (OperatingSystem.IsAndroid() ? 4 : 8)
                    && autoSampling.LodBias == 0,
                    "auto HD sampling resolves to stable trilinear minification with bounded anisotropy");

                string autoKey = TextureSamplingPolicy.RuntimeKey;
                RenderOptions.TextureFiltering = !RenderOptions.TextureFiltering;
                string autoKeyAfterNativeChange = TextureSamplingPolicy.RuntimeKey;
                Check(autoKey == autoKeyAfterNativeChange,
                    "Auto HD residency is independent from native cartridge filtering");

                TextureSamplerDescriptor effectSampling = TextureSamplingPolicy.ResolveModern(
                    TextureAssetClass.Effect, TextureAssetChannel.Emissive);
                Check(effectSampling.Mipmaps && effectSampling.LinearMinification,
                    "authored effect models share the stable HD material policy");
                TextureSamplerDescriptor uiSampling = TextureSamplingPolicy.ResolveModern(
                    TextureAssetClass.Ui, TextureAssetChannel.Albedo);
                Check(uiSampling == TextureSamplingPolicy.ResolveNativeWorld(),
                    "UI assets stay outside the HD world-material policy");

                RenderOptions.TextureSampling = TextureSamplingMode.Legacy;
                TextureSamplerDescriptor legacySampling = TextureSamplingPolicy.ResolveModern(
                    TextureAssetClass.World, TextureAssetChannel.Albedo);
                Check(!legacySampling.LinearMagnification && !legacySampling.LinearMinification
                    && !legacySampling.Mipmaps && legacySampling.Anisotropy == 1,
                    "legacy HD sampling preserves nearest-neighbour presentation");

                RenderOptions.TextureFiltering = true;
                RenderOptions.TextureMipmaps = true;
                RenderOptions.TextureAnisotropy = 16;
                RenderOptions.TextureSampling = TextureSamplingMode.Custom;
                TextureSamplerDescriptor customSampling = TextureSamplingPolicy.ResolveModern(
                    TextureAssetClass.World, TextureAssetChannel.Normal);
                Check(customSampling.LinearMagnification && customSampling.LinearMinification
                    && customSampling.Mipmaps && customSampling.Anisotropy == 16,
                    "custom HD sampling follows the existing filtering controls");
                string customKey = TextureSamplingPolicy.RuntimeKey;
                RenderOptions.TextureAnisotropy = 4;
                Check(customKey != TextureSamplingPolicy.RuntimeKey,
                    "Custom HD residency key changes when its effective sampling changes");
                RenderOptions.TextureSampling = TextureSamplingMode.Auto;
                byte[] normalPixels = { 255, 128, 128, 255, 128, 255, 128, 255, 128, 128, 255, 255, 255, 255, 255, 255 };
                ModernTextureAsset normal = ModernTextureAsset.FromRgba("check", TextureAssetClass.Hunter,
                    TextureAssetChannel.Normal, 2, 2, normalPixels).Fit(1);
                Check(normal.Width == 1 && normal.Height == 1 && normal.Pixels.Length == 4,
                    "modern texture downscale preserves a valid RGBA surface");
                Check(normal.EstimateGpuBytes(false) == 4 && normal.EstimateGpuBytes(true) == 5,
                    "modern texture residency estimate includes mip overhead");

                // Exercise the desktop STB pointer-to-final-buffer downscale.
                // A 2x2 24-bit BMP uses the STB path (not the dedicated TGA
                // decoder); forcing it to 1x1 must match the managed RGBA
                // resampler byte-for-byte.
                byte[] bmpRgba =
                {
                    255, 0, 0, 255,     0, 255, 0, 255,
                    0, 0, 255, 255,     255, 255, 255, 255
                };
                ModernTextureAsset managedBmp = ModernTextureAsset.FromRgba(
                    "managed-bmp", TextureAssetClass.World, TextureAssetChannel.Albedo,
                    2, 2, bmpRgba).Fit(1);
                using var bmp = new System.IO.MemoryStream();
                using (var writer = new System.IO.BinaryWriter(
                    bmp, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write((byte)'B'); writer.Write((byte)'M');
                    writer.Write(70); writer.Write((ushort)0); writer.Write((ushort)0);
                    writer.Write(54); writer.Write(40);
                    writer.Write(2); writer.Write(2);
                    writer.Write((ushort)1); writer.Write((ushort)24);
                    writer.Write(0); writer.Write(16);
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    // BMP rows are bottom-up and BGR, padded to four bytes.
                    writer.Write(new byte[] { 255, 0, 0, 255, 255, 255, 0, 0 });
                    writer.Write(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 });
                }
                bmp.Position = 0;
                ModernTextureAsset nativeBmp = ModernTextureAsset.Decode(
                    bmp, "pointer-bmp", TextureAssetClass.World,
                    TextureAssetChannel.Albedo, maximumDimension: 1);
                Check(nativeBmp.Width == 1 && nativeBmp.Height == 1
                    && nativeBmp.Pixels.AsSpan().SequenceEqual(managedBmp.Pixels),
                    "native STB downscale matches managed modern-texture filtering");

                using var tga = new System.IO.MemoryStream();
                using (var writer = new System.IO.BinaryWriter(tga, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)2);
                    writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((byte)0);
                    writer.Write((ushort)0); writer.Write((ushort)0);
                    writer.Write((ushort)2); writer.Write((ushort)1);
                    writer.Write((byte)24); writer.Write((byte)0x20);
                    writer.Write(new byte[] { 30, 20, 10, 90, 80, 70 });
                }
                tga.Position = 0;
                ModernTextureAsset tgaAsset = ModernTextureAsset.Decode(tga, "check.tga",
                    TextureAssetClass.World, TextureAssetChannel.Albedo);
                Check(tgaAsset.Width == 2 && tgaAsset.Height == 1
                    && tgaAsset.Pixels[0] == 10 && tgaAsset.Pixels[1] == 20 && tgaAsset.Pixels[2] == 30
                    && tgaAsset.Pixels[4] == 70 && tgaAsset.Pixels[5] == 80 && tgaAsset.Pixels[6] == 90,
                    "managed TGA decode preserves BGR ordering and dimensions");

                Console.WriteLine("[graphicscheck] presets, migration and modern texture policy passed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[graphicscheck] FAIL: {ex}");
                return 1;
            }
        }
    }
}
