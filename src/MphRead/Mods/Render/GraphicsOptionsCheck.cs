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

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
                Check(RenderOptions.Preset == GraphicsPreset.Original
                    && RenderOptions.ResolutionScale == 100
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Off
                    && RenderOptions.Shadows == ShadowQuality.Off
                    && !RenderOptions.PostProcessingEnabled,
                    "original preset remains the unprocessed compatibility path");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Performance);
                Check(RenderOptions.ResolutionScale == 85
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Fxaa
                    && RenderOptions.TextureAnisotropy == 4
                    && RenderOptions.Shadows == ShadowQuality.Off,
                    "performance preset keeps the cheap path");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Enhanced);
                Check(RenderOptions.AntiAliasing == AntiAliasingMode.Smaa
                    && RenderOptions.TextureUpscale == TextureUpscaleMode.Scale2x
                    && RenderOptions.Shadows == ShadowQuality.Low
                    && RenderOptions.AmbientOcclusion == AmbientOcclusionQuality.Medium
                    && RenderOptions.AdvancedMaterials
                    && RenderOptions.NeedsReadableDepth
                    && RenderOptions.PostProcessingEnabled,
                    "enhanced preset enables the modern depth/material pipeline");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Ultra);
                Check(RenderOptions.ResolutionScale == 200
                    && RenderOptions.TextureUpscale == TextureUpscaleMode.Scale4x
                    && RenderOptions.Shadows == ShadowQuality.High
                    && RenderOptions.InternalHdr
                    && RenderOptions.DeferredPbr
                    && RenderOptions.Reflections
                    && RenderOptions.VolumetricFog,
                    "ultra preset enables high-end effects");

                RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme);
                Check(RenderOptions.ResolutionScale == 400
                    && RenderOptions.AntiAliasing == AntiAliasingMode.Smaa
                    && RenderOptions.DeferredPbr
                    && RenderOptions.Shadows == ShadowQuality.Ultra
                    && RenderOptions.InternalHdr
                    && RenderOptions.DynamicGlow,
                    "extreme preset remains bounded but maximal");

                RenderOptions.AntiAliasing = AntiAliasingMode.Taa;
                Check(RenderOptions.PostProcessingEnabled,
                    "TAA remains available as an explicit temporal reprojection mode");

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
                    ResolutionScale = "9999",
                    AntiAliasing = "not-a-mode",
                    ShadowQuality = "ultra",
                    TextureUpscale = "scale4x",
                    Gamma = "999",
                    AdvancedMaterials = "yes",
                    InternalHdr = "true"
                };
                Check(SettingsMigration.Apply(settings, out string summary)
                    && settings.SettingsSchemaVersion == SettingsMigration.CurrentSchema
                    && settings.ResolutionScale == RenderOptions.MaxScale.ToString()
                    && settings.AntiAliasing == "off"
                    && settings.ShadowQuality == "ultra"
                    && settings.TextureUpscale == "scale4x"
                    && settings.Gamma == "150"
                    && settings.AdvancedMaterials == "on"
                    && settings.InternalHdr == "on"
                    && summary.Length > 0,
                    "graphics settings migration clamps and normalizes new options");

                Console.WriteLine("[graphicscheck] presets, migration and texture upscaling passed");
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
