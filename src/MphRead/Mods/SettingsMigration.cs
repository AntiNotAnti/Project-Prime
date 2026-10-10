using System;
using System.Collections.Generic;
using System.Globalization;
using MphRead.Mods.Render;

namespace MphRead.Mods
{
    /// <summary>
    /// Versioned normalization for settings.json.
    ///
    /// Settings survive upgrades by design. That is useful until a setting's
    /// range or meaning changes: an old value can then make a new build behave
    /// very differently from a clean install. Every load comes through here so
    /// old files are upgraded deliberately rather than accumulating folklore.
    /// </summary>
    public static class SettingsMigration
    {
        public const int CurrentSchema = 10;

        public static bool Apply(MenuSettings settings, out string summary)
        {
            var changed = new List<string>();
            int from = settings.SettingsSchemaVersion;

            // Retired DX12/Vulkan/Metal and Auto preferences must never survive
            // into a renderer restart or be shown as selectable settings.
            settings.Renderer = Normalize(settings.Renderer, "OpenGL", "renderer", changed);
            settings.ResolutionScale = Normalize(settings.ResolutionScale,
                RenderOptions.ParseScale(settings.ResolutionScale, 100)
                    .ToString(CultureInfo.InvariantCulture), "render scale", changed);
            settings.FieldOfView = Normalize(settings.FieldOfView,
                RenderOptions.ParseFov(settings.FieldOfView, RenderOptions.DefaultFov)
                    .ToString(CultureInfo.InvariantCulture), "field of view", changed);
            settings.Lighting = Normalize(settings.Lighting,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.Lighting, true)),
                "lighting", changed);
            settings.Fog = Normalize(settings.Fog,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.Fog, true)),
                "fog", changed);
            if (from < 8)
            {
                // Existing HD users already made a concrete filtering/mipmap/
                // anisotropy choice. Preserve that behavior as Custom instead
                // of silently turning a previous preference into Auto. Users
                // who had replacements disabled receive the new stable Auto
                // policy only if they opt into HD assets later.
                string migratedSampling = RenderOptions.ParseOnOff(settings.TextureReplacements, false)
                    ? "custom" : "auto";
                settings.TextureSampling = Normalize(settings.TextureSampling, migratedSampling,
                    "HD texture sampling", changed);
            }

            settings.TextureFiltering = Normalize(settings.TextureFiltering,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.TextureFiltering, false)),
                "texture filtering", changed);
            settings.TextureMipmaps = Normalize(settings.TextureMipmaps,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.TextureMipmaps, false)),
                "mipmaps", changed);

            int aniso = Math.Clamp(RenderOptions.ParseInt(settings.TextureAnisotropy, 1), 1, 16);
            aniso = aniso >= 16 ? 16 : aniso >= 8 ? 8 : aniso >= 4 ? 4 : aniso >= 2 ? 2 : 1;
            settings.TextureAnisotropy = Normalize(settings.TextureAnisotropy, aniso.ToString(CultureInfo.InvariantCulture),
                "anisotropy", changed);
            settings.TextureSampling = NormalizeEnum(settings.TextureSampling,
                TextureSamplingMode.Auto, "HD texture sampling", changed);

            settings.ShowFps = Normalize(settings.ShowFps,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.ShowFps, false)),
                "fps counter", changed);
            settings.SmoothNativeHud = Normalize(settings.SmoothNativeHud,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.SmoothNativeHud, true)),
                "native HUD sampling", changed);

            int cap = FrameTiming.ParseSavedCap(settings.FrameRateCap, FrameTiming.DisplayRate);
            settings.FrameRateCap = Normalize(settings.FrameRateCap, FrameTiming.CapString(cap), "fps limit", changed);

            settings.CelShading = Normalize(settings.CelShading,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(settings.CelShading, false)),
                "cel shading", changed);
            settings.CelBands = Normalize(settings.CelBands,
                Math.Clamp(RenderOptions.ParseInt(settings.CelBands, 8), 2, 8)
                    .ToString(CultureInfo.InvariantCulture), "cel bands", changed);
            settings.CelEdge = Normalize(settings.CelEdge,
                Math.Clamp(RenderOptions.ParseInt(settings.CelEdge, 50), 0, 100)
                    .ToString(CultureInfo.InvariantCulture), "cel edge", changed);

            settings.GraphicsPreset = NormalizeEnum(settings.GraphicsPreset,
                GraphicsPreset.Original, "graphics preset", changed);

            // Phase 3 retires the experimental postprocessing controls. Force
            // legacy JSON to canonical neutral values even when its schema is
            // current, so hidden options cannot stay active after an update.
            settings.AntiAliasing = Normalize(settings.AntiAliasing, "off", "anti-aliasing", changed);
            settings.SharpenStrength = Normalize(settings.SharpenStrength, "0", "sharpening", changed);
            settings.Bloom = Normalize(settings.Bloom, "off", "bloom", changed);
            settings.BloomIntensity = Normalize(settings.BloomIntensity, "0", "bloom intensity", changed);
            settings.ColorGrade = Normalize(settings.ColorGrade, "original", "color grade", changed);
            settings.Gamma = Normalize(settings.Gamma, "100", "gamma", changed);
            settings.Contrast = Normalize(settings.Contrast, "100", "contrast", changed);
            settings.Saturation = Normalize(settings.Saturation, "100", "saturation", changed);
            settings.EnhancedLighting = Normalize(settings.EnhancedLighting, "off", "enhanced lighting", changed);
            settings.DeferredPbr = Normalize(settings.DeferredPbr, "off", "deferred PBR", changed);
            settings.AmbientOcclusion = Normalize(settings.AmbientOcclusion, "off", "ambient occlusion", changed);
            settings.ContactShadows = Normalize(settings.ContactShadows, "off", "contact shadows", changed);
            settings.EnhancedFog = Normalize(settings.EnhancedFog, "off", "enhanced fog", changed);
            settings.VolumetricFog = Normalize(settings.VolumetricFog, "off", "volumetric fog", changed);
            settings.InternalHdr = Normalize(settings.InternalHdr, "off", "HDR tone mapping", changed);
            settings.Reflections = Normalize(settings.Reflections, "off", "reflections", changed);
            settings.DynamicGlow = Normalize(settings.DynamicGlow, "off", "dynamic glow", changed);

            // Advanced authored material maps and native directional shadows
            // are maintained by OpenGL and remain user-selectable.
            settings.AdvancedMaterials = NormalizeToggle(settings.AdvancedMaterials, false,
                "advanced materials", changed);
            settings.ShadowQuality = NormalizeEnum(settings.ShadowQuality,
                ShadowQuality.Off, "shadow quality", changed);
            settings.TextureReplacements = NormalizeToggle(settings.TextureReplacements, false,
                "HD texture replacements", changed);
            settings.CharacterModelReplacements = NormalizeToggle(
                settings.CharacterModelReplacements, false, "HD character models", changed);
            settings.TextureUpscale = NormalizeEnum(settings.TextureUpscale,
                TextureUpscaleMode.Off, "texture upscaling", changed);
            settings.TextureQuality = NormalizeEnum(settings.TextureQuality,
                TextureAssetQuality.Automatic, "HD asset resolution", changed);

            settings.SfxVolume = NormalizeVolume(settings.SfxVolume, 0.35f, "sfx volume", changed);
            settings.PlayerVolume = NormalizeVolume(settings.PlayerVolume, 1f, "player volume", changed);
            settings.WeaponVolume = NormalizeVolume(settings.WeaponVolume, 1f, "weapon volume", changed);
            settings.NotificationVolume = NormalizeVolume(settings.NotificationVolume, 1f, "notification volume", changed);
            settings.EffectsVolume = NormalizeVolume(settings.EffectsVolume, 1f, "effects volume", changed);
            settings.MusicVolume = NormalizeVolume(settings.MusicVolume, 0.50f, "music volume", changed);

            if (settings.SettingsSchemaVersion != CurrentSchema)
            {
                settings.SettingsSchemaVersion = CurrentSchema;
            }

            summary = from == CurrentSchema && changed.Count == 0
                ? ""
                : $"settings schema {from} -> {CurrentSchema}"
                    + (changed.Count == 0 ? "" : $"; normalized {String.Join(", ", changed)}");
            return from != CurrentSchema || changed.Count > 0;
        }

        /// <summary>
        /// Canonical values for options removed from the OpenGL settings UI.
        /// Retain JSON fields for old installations, but never re-enable their
        /// shaders, history textures or transient framebuffers on save.
        /// </summary>
        public static void ResetRetiredPostProcessing(MenuSettings settings)
        {
            settings.AntiAliasing = "off";
            settings.SharpenStrength = "0";
            settings.Bloom = "off";
            settings.BloomIntensity = "0";
            settings.ColorGrade = "original";
            settings.Gamma = "100";
            settings.Contrast = "100";
            settings.Saturation = "100";
            settings.EnhancedLighting = "off";
            settings.DeferredPbr = "off";
            settings.AmbientOcclusion = "off";
            settings.ContactShadows = "off";
            settings.EnhancedFog = "off";
            settings.VolumetricFog = "off";
            settings.InternalHdr = "off";
            settings.Reflections = "off";
            settings.DynamicGlow = "off";
        }

        public static void ResetPerformance(MenuSettings settings)
        {
            settings.Renderer = "OpenGL";
            settings.ResolutionScale = "100";
            settings.FieldOfView = RenderOptions.DefaultFov.ToString(CultureInfo.InvariantCulture);
            settings.Lighting = "on";
            settings.Fog = "on";
            settings.TextureFiltering = "off";
            settings.TextureMipmaps = "off";
            settings.TextureAnisotropy = "1";
            settings.TextureSampling = "auto";
            settings.ShowFps = "off";
            settings.SmoothNativeHud = "on";
            settings.FrameRateCap = "display";
            settings.CelShading = "off";
            settings.CelBands = "8";
            settings.CelEdge = "50";
            settings.GraphicsPreset = "original";
            settings.AntiAliasing = "off";
            settings.SharpenStrength = "0";
            settings.Bloom = "off";
            settings.BloomIntensity = "0";
            settings.ColorGrade = "original";
            settings.Gamma = "100";
            settings.Contrast = "100";
            settings.Saturation = "100";
            settings.EnhancedLighting = "off";
            settings.AdvancedMaterials = "off";
            settings.DeferredPbr = "off";
            settings.ShadowQuality = "off";
            settings.AmbientOcclusion = "off";
            settings.ContactShadows = "off";
            settings.EnhancedFog = "off";
            settings.VolumetricFog = "off";
            settings.InternalHdr = "off";
            settings.Reflections = "off";
            settings.DynamicGlow = "off";
            settings.TextureReplacements = "off";
            settings.CharacterModelReplacements = "off";
            settings.TextureUpscale = "off";
            settings.TextureQuality = "automatic";
            settings.SettingsSchemaVersion = CurrentSchema;
        }

        private static string NormalizeToggle(string value, bool fallback, string name,
            List<string> changed)
        {
            return Normalize(value,
                RenderOptions.OnOff(RenderOptions.ParseOnOff(value, fallback)), name, changed);
        }

        private static string NormalizeEnum<T>(string value, T fallback, string name,
            List<string> changed) where T : struct, Enum
        {
            T normalized = Enum.TryParse(value, true, out T parsed)
                && Enum.IsDefined(typeof(T), parsed) ? parsed : fallback;
            return Normalize(value, normalized.ToString().ToLowerInvariant(), name, changed);
        }

        private static string NormalizeVolume(string value, float fallback, string name,
            List<string> changed)
        {
            float normalized = Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float parsed) ? Math.Clamp(parsed, 0, 1) : fallback;
            return Normalize(value, normalized.ToString("0.###", CultureInfo.InvariantCulture), name, changed);
        }

        private static string Normalize(string value, string normalized, string name,
            List<string> changed)
        {
            if (!String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
            {
                value = normalized;
                changed.Add(name);
            }
            return value;
        }
    }
}
