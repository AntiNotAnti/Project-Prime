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
