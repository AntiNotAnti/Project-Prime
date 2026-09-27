using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
using MphRead.Mods.Cosmetics.Skins;
using MphRead.Mods.Cosmetics.Armor;
using MphRead.Mods.Cosmetics.Death;
namespace MphRead.Mods.Cosmetics
{
    public static class CosmeticCatalog
    {
        // Wire IDs are permanent. Append new entries; never reuse a retired ID.
        public static IReadOnlyList<SkinDefinition> Skins { get; } = BuildSkins();
        public static IReadOnlyList<ArmorEffectDefinition> ArmorEffects { get; } = Array.AsReadOnly(new[]
        {
            new ArmorEffectDefinition("armor.none", 0, "None") { Intensity = 0, ParticleRate = 0 },
            new ArmorEffectDefinition("armor.lightning", 1, "Lightning") { Intensity = 0.95f, PulseSpeed = 10, ScrollSpeed = 2.2f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0x53baff), SecondaryColor = Rgb(0xecfaff), ParticleStyle = ParticleStyle.Sparks, DynamicLight = true, Description = "Lightning surface and sparks" },
            new ArmorEffectDefinition("armor.pestilence", 2, "Pestilence") { ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0x93cd35), SecondaryColor = Rgb(0xc8ef77), ParticleStyle = ParticleStyle.Vapor, DynamicLight = true, Description = "Pestilence surface and vapor" },
            new ArmorEffectDefinition("armor.eclipse", 3, "Eclipse") { ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x735c9b), SecondaryColor = Rgb(0xbba0ff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Eclipse surface and wisps" },
            new ArmorEffectDefinition("armor.inferno", 4, "Inferno") { Intensity = 0.9f, PulseSpeed = 2.8f, ScrollSpeed = 1.4f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0xff661e), SecondaryColor = Rgb(0xffd56a), ParticleStyle = ParticleStyle.Embers, DynamicLight = true, Description = "Inferno surface and embers" },
            new ArmorEffectDefinition("armor.glacial", 5, "Glacial") { Intensity = 0.8f, PulseSpeed = 1.1f, ScrollSpeed = 0.4f, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x5fcfea), SecondaryColor = Rgb(0xe4fbff), ParticleStyle = ParticleStyle.Ice, DynamicLight = true, Description = "Glacial surface and ice" },
            new ArmorEffectDefinition("armor.void", 6, "Void") { Intensity = 0.85f, PulseSpeed = 1.5f, ScrollSpeed = 0.7f, ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0x662fa8), SecondaryColor = Rgb(0xc88eff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Void surface and wisps" },
            new ArmorEffectDefinition("armor.radiant", 7, "Radiant") { ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0xffd877), SecondaryColor = Rgb(0xfff6d8), ParticleStyle = ParticleStyle.Motes, DynamicLight = true, Description = "Radiant surface and motes" },
            new ArmorEffectDefinition("armor.phase", 8, "Phase") { Intensity = 0.85f, PulseSpeed = 3, ScrollSpeed = 1.8f, ShaderStyle = SurfaceStyle.Hologram, PrimaryColor = Rgb(0x53e4ee), SecondaryColor = Rgb(0xb5ffff), ParticleStyle = ParticleStyle.Pixels, DynamicLight = true, Description = "Phase surface and pixels" },
            new ArmorEffectDefinition("armor.spectral", 9, "Spectral") { ShaderStyle = SurfaceStyle.ColorFlow, PrimaryColor = Rgb(0x87cdcb), SecondaryColor = Rgb(0xd2ffed), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Spectral surface and wisps" },
            new ArmorEffectDefinition("armor.spike", 10, "Spike") { ShaderStyle = SurfaceStyle.AnimatedMask, PrimaryColor = Rgb(0xb1aacd), SecondaryColor = Rgb(0xf0d3ff), ParticleStyle = ParticleStyle.Shards, DynamicLight = true, Description = "Spike surface and shards" },
            new ArmorEffectDefinition("armor.solar", 11, "Solar") { ShaderStyle = SurfaceStyle.EmissionPulse, PrimaryColor = Rgb(0xff9e3d), SecondaryColor = Rgb(0xffefab), ParticleStyle = ParticleStyle.Embers, DynamicLight = true, Description = "Solar surface and embers" },
            new ArmorEffectDefinition("armor.lumen", 12, "Lumen") { ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0xd7eeff), SecondaryColor = Rgb(0xffffff), ParticleStyle = ParticleStyle.Motes, DynamicLight = true, Description = "Lumen surface and motes" },
            new ArmorEffectDefinition("armor.corruption", 13, "Corruption") { ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0xa53653), SecondaryColor = Rgb(0xff6073), ParticleStyle = ParticleStyle.Vapor, DynamicLight = true, Description = "Corruption surface and vapor" },
            new ArmorEffectDefinition("armor.aurora", 14, "Aurora") { Intensity = 0.8f, PulseSpeed = 1.2f, ScrollSpeed = 0.6f, ShaderStyle = SurfaceStyle.ColorFlow, PrimaryColor = Rgb(0x58dcac), SecondaryColor = Rgb(0xbc79ff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Aurora surface and wisps" },
            new ArmorEffectDefinition("armor.quantum", 15, "Quantum") { Intensity = 0.9f, PulseSpeed = 6, ScrollSpeed = 2, ShaderStyle = SurfaceStyle.Scanline, PrimaryColor = Rgb(0xe36ae5), SecondaryColor = Rgb(0x61e8ff), ParticleStyle = ParticleStyle.Pixels, DynamicLight = true, Description = "Quantum surface and pixels" },
            new ArmorEffectDefinition("armor.thunderstorm", 16, "Thunderstorm") { Intensity = 0.9f, PulseSpeed = 13, ScrollSpeed = 2.7f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0x7781d2), SecondaryColor = Rgb(0xc2d9ff), ParticleStyle = ParticleStyle.Sparks, DynamicLight = true, Description = "Thunderstorm surface and sparks" },
        });
        public static IReadOnlyList<DeathPresentationDefinition> DeathPresentations { get; } = Array.AsReadOnly(new[]
        {
            new DeathPresentationDefinition("death.classic", 0, "Classic"),
            new DeathPresentationDefinition("death.inferno_burst", 1, "Inferno Burst") { Duration = 1.6f, FadeStart = 0.2f, PoseStyle = DeathPoseStyle.Backfall, SurfaceEffect = SurfaceStyle.EnergyCracks, ParticleEffect = ParticleStyle.Embers, LightEffect = Rgb(0xff963e) },
            new DeathPresentationDefinition("death.cryo_shatter", 2, "Cryo Shatter") { Duration = 1.7f, FadeStart = 0.45f, PoseStyle = DeathPoseStyle.ShatterFreeze, SurfaceEffect = SurfaceStyle.Fresnel, ParticleEffect = ParticleStyle.Ice, LightEffect = Rgb(0xacf0ff) },
            new DeathPresentationDefinition("death.plasma_dissolve", 3, "Plasma Dissolve") { Duration = 2.0f, FadeStart = 0.08f, PoseStyle = DeathPoseStyle.Disintegrate, SurfaceEffect = SurfaceStyle.NoiseDissolve, ParticleEffect = ParticleStyle.Motes, LightEffect = Rgb(0x72ffcb) },
            new DeathPresentationDefinition("death.void_collapse", 4, "Void Collapse") { Duration = 1.7f, FadeStart = 0.2f, PoseStyle = DeathPoseStyle.KneelCollapse, SurfaceEffect = SurfaceStyle.NoiseDissolve, ParticleEffect = ParticleStyle.Wisps, LightEffect = Rgb(0xad64ee) },
            new DeathPresentationDefinition("death.quantum", 5, "Quantum Fragmentation") { Duration = 1.6f, FadeStart = 0.12f, PoseStyle = DeathPoseStyle.Disintegrate, SurfaceEffect = SurfaceStyle.Scanline, ParticleEffect = ParticleStyle.Pixels, LightEffect = Rgb(0xda8bff) },
            new DeathPresentationDefinition("death.radiant_ascension", 6, "Radiant Ascension") { Duration = 2.2f, FadeStart = 0.3f, PoseStyle = DeathPoseStyle.Float, SurfaceEffect = SurfaceStyle.Fresnel, ParticleEffect = ParticleStyle.Motes, LightEffect = Rgb(0xffe1a2) },
            new DeathPresentationDefinition("death.lightning_discharge", 7, "Lightning Discharge") { Duration = 1.4f, FadeStart = 0.22f, PoseStyle = DeathPoseStyle.Backfall, SurfaceEffect = SurfaceStyle.EnergyCracks, ParticleEffect = ParticleStyle.Sparks, LightEffect = Rgb(0x9bdaff) },
        });
        private static IReadOnlyList<SkinDefinition> BuildSkins()
        {
            var skins = new List<SkinDefinition> { new("skin.default", 0, "Default", null) };
            for (int h = 0; h < 7; h++)
            {
                string hunter = ((Hunter)h).ToString().ToLowerInvariant();
                skins.Add(new($"skin.{hunter}.obsidian", (ushort)(1 + h * 2), "Obsidian", (Hunter)h)
                { SurfaceTreatment = 1, AlbedoSet = $"Skins/{(Hunter)h}/Obsidian/Biped", GunAssets = $"Skins/{(Hunter)h}/Obsidian/ViewModel", AltFormAssets = $"Skins/{(Hunter)h}/Obsidian/AltForm", TurretAssets = $"Skins/{(Hunter)h}/Obsidian/Halfturret", Description = "Graphite armor with native palette accents" });
                skins.Add(new($"skin.{hunter}.alimbic", (ushort)(2 + h * 2), "Alimbic", (Hunter)h)
                { SurfaceTreatment = 2, AlbedoSet = $"Skins/{(Hunter)h}/Alimbic/Biped", GunAssets = $"Skins/{(Hunter)h}/Alimbic/ViewModel", AltFormAssets = $"Skins/{(Hunter)h}/Alimbic/AltForm", TurretAssets = $"Skins/{(Hunter)h}/Alimbic/Halfturret", Description = "Warm alloy with etched energy channels" });
            }
            return skins.AsReadOnly();
        }
        public static SkinDefinition ResolveSkin(string? key, Hunter hunter) =>
            Skins.FirstOrDefault(x => x.Key == key && (x.Hunter == null || x.Hunter == hunter)) ?? Skins[0];
        public static ArmorEffectDefinition ResolveArmor(string? key) => ArmorEffects.FirstOrDefault(x => x.Key == key) ?? ArmorEffects[0];
        public static DeathPresentationDefinition ResolveDeath(string? key) => DeathPresentations.FirstOrDefault(x => x.Key == key) ?? DeathPresentations[0];
        public static CosmeticLoadout Resolve(Hunter hunter, CosmeticLoadout? value) => value == null ? CosmeticLoadout.Default
            : new(ResolveSkin(value.SkinKey, hunter).Key, ResolveArmor(value.ArmorEffectKey).Key, ResolveDeath(value.DeathEffectKey).Key);
        public static CosmeticLoadout FromWire(Hunter hunter, ushort skin, ushort armor, ushort death) => Resolve(hunter,
            new(Skins.FirstOrDefault(x => x.WireId == skin)?.Key ?? "skin.default",
                ArmorEffects.FirstOrDefault(x => x.WireId == armor)?.Key ?? "armor.none",
                DeathPresentations.FirstOrDefault(x => x.WireId == death)?.Key ?? "death.classic"));
        private static Vector3 Rgb(uint value) => new((value >> 16) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);
    }
}
