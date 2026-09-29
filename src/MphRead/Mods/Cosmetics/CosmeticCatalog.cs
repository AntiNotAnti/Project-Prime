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
            new ArmorEffectDefinition("armor.lightning", 1, "Lightning") { Motion = ArmorMotion.Lightning, Intensity = 0.95f, PulseSpeed = 10, ScrollSpeed = 2.2f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0x53baff), SecondaryColor = Rgb(0xecfaff), ParticleStyle = ParticleStyle.Sparks, DynamicLight = true, Description = "Forked electrical arcs" },
            new ArmorEffectDefinition("armor.pestilence", 2, "Pestilence") { Motion = ArmorMotion.Pestilence, ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0x93cd35), SecondaryColor = Rgb(0xc8ef77), ParticleStyle = ParticleStyle.Vapor, DynamicLight = true, Description = "Billowing rising spore clouds" },
            new ArmorEffectDefinition("armor.eclipse", 3, "Eclipse") { Motion = ArmorMotion.Eclipse, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x735c9b), SecondaryColor = Rgb(0xbba0ff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "A rotating vertical eclipse halo" },
            new ArmorEffectDefinition("armor.inferno", 4, "Inferno") { Motion = ArmorMotion.Inferno, Intensity = 0.9f, PulseSpeed = 2.8f, ScrollSpeed = 1.4f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0xff661e), SecondaryColor = Rgb(0xffd56a), ParticleStyle = ParticleStyle.Embers, DynamicLight = true, Description = "Tapered rising flame tongues" },
            new ArmorEffectDefinition("armor.glacial", 5, "Glacial") { Motion = ArmorMotion.Glacial, Intensity = 0.8f, PulseSpeed = 1.1f, ScrollSpeed = 0.4f, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x5fcfea), SecondaryColor = Rgb(0xe4fbff), ParticleStyle = ParticleStyle.Ice, DynamicLight = true, Description = "Orbiting angular ice crystals" },
            new ArmorEffectDefinition("armor.void", 6, "Void") { Motion = ArmorMotion.Void, Intensity = 0.85f, PulseSpeed = 1.5f, ScrollSpeed = 0.7f, ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0x662fa8), SecondaryColor = Rgb(0xc88eff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Spirals collapsing inward" },
            new ArmorEffectDefinition("armor.radiant", 7, "Radiant") { Motion = ArmorMotion.Radiant, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0xffd877), SecondaryColor = Rgb(0xfff6d8), ParticleStyle = ParticleStyle.Motes, DynamicLight = true, Description = "Expanding radial sunbursts" },
            new ArmorEffectDefinition("armor.phase", 8, "Phase") { Motion = ArmorMotion.Phase, Intensity = 0.85f, PulseSpeed = 3, ScrollSpeed = 1.8f, ShaderStyle = SurfaceStyle.Hologram, PrimaryColor = Rgb(0x53e4ee), SecondaryColor = Rgb(0xb5ffff), ParticleStyle = ParticleStyle.Pixels, DynamicLight = true, Description = "Stepped scanning brackets" },
            new ArmorEffectDefinition("armor.spectral", 9, "Spectral") { Motion = ArmorMotion.Spectral, ShaderStyle = SurfaceStyle.ColorFlow, PrimaryColor = Rgb(0x87cdcb), SecondaryColor = Rgb(0xd2ffed), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Trailing ghostly corkscrews" },
            new ArmorEffectDefinition("armor.spike", 10, "Spike") { Motion = ArmorMotion.Spike, ShaderStyle = SurfaceStyle.AnimatedMask, PrimaryColor = Rgb(0xb1aacd), SecondaryColor = Rgb(0xf0d3ff), ParticleStyle = ParticleStyle.Shards, DynamicLight = true, Description = "Pulsing outward armor quills" },
            new ArmorEffectDefinition("armor.solar", 11, "Solar") { Motion = ArmorMotion.Solar, ShaderStyle = SurfaceStyle.EmissionPulse, PrimaryColor = Rgb(0xff9e3d), SecondaryColor = Rgb(0xffefab), ParticleStyle = ParticleStyle.Embers, DynamicLight = true, Description = "Looping solar prominences" },
            new ArmorEffectDefinition("armor.lumen", 12, "Lumen") { Motion = ArmorMotion.Lumen, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0xd7eeff), SecondaryColor = Rgb(0xffffff), ParticleStyle = ParticleStyle.Motes, DynamicLight = true, Description = "Cross-shaped hovering glints" },
            new ArmorEffectDefinition("armor.corruption", 13, "Corruption") { Motion = ArmorMotion.Corruption, ShaderStyle = SurfaceStyle.NoiseDissolve, PrimaryColor = Rgb(0xa53653), SecondaryColor = Rgb(0xff6073), ParticleStyle = ParticleStyle.Vapor, DynamicLight = true, Description = "Crawling jagged tendrils" },
            new ArmorEffectDefinition("armor.aurora", 14, "Aurora") { Motion = ArmorMotion.Aurora, Intensity = 0.8f, PulseSpeed = 1.2f, ScrollSpeed = 0.6f, ShaderStyle = SurfaceStyle.ColorFlow, PrimaryColor = Rgb(0x58dcac), SecondaryColor = Rgb(0xbc79ff), ParticleStyle = ParticleStyle.Wisps, DynamicLight = true, Description = "Flowing curtains of light" },
            new ArmorEffectDefinition("armor.quantum", 15, "Quantum") { Motion = ArmorMotion.Quantum, Intensity = 0.9f, PulseSpeed = 6, ScrollSpeed = 2, ShaderStyle = SurfaceStyle.Scanline, PrimaryColor = Rgb(0xe36ae5), SecondaryColor = Rgb(0x61e8ff), ParticleStyle = ParticleStyle.Pixels, DynamicLight = true, Description = "Particles jumping between lattice nodes" },
            new ArmorEffectDefinition("armor.thunderstorm", 16, "Thunderstorm") { Motion = ArmorMotion.Thunderstorm, Intensity = 0.9f, PulseSpeed = 13, ScrollSpeed = 2.7f, ShaderStyle = SurfaceStyle.EnergyCracks, PrimaryColor = Rgb(0x7781d2), SecondaryColor = Rgb(0xc2d9ff), ParticleStyle = ParticleStyle.Sparks, DynamicLight = true, Description = "Cascading storm bolts" },
            new ArmorEffectDefinition("armor.orbital", 17, "Orbital") { Motion = ArmorMotion.Orbit, ParticleRate = 6, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x70bfff), SecondaryColor = Rgb(0xffffff), ParticleStyle = ParticleStyle.Motes, Description = "Three intersecting satellite rings" },
            new ArmorEffectDefinition("armor.dna", 18, "Double Helix") { Motion = ArmorMotion.Helix, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x40ffc0), SecondaryColor = Rgb(0xdf87ff), ParticleStyle = ParticleStyle.Motes, Description = "Twin winding energy strands" },
            new ArmorEffectDefinition("armor.warp", 19, "Warp Drive") { Motion = ArmorMotion.Warp, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0x78aaff), SecondaryColor = Rgb(0xe1f8ff), ParticleStyle = ParticleStyle.Motes, Description = "Expanding horizontal warp rings" },
            new ArmorEffectDefinition("armor.starfall", 20, "Starfall") { Motion = ArmorMotion.Rain, ShaderStyle = SurfaceStyle.Fresnel, PrimaryColor = Rgb(0xffb7dc), SecondaryColor = Rgb(0xfff1ac), ParticleStyle = ParticleStyle.Motes, Description = "Falling meteor streaks" },
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
                { SurfaceTreatment = 1, DecalAsset = $"Decals/{(Hunter)h}/Obsidian.png", AlbedoSet = $"Skins/{(Hunter)h}/Obsidian/Biped", GunAssets = $"Skins/{(Hunter)h}/Obsidian/ViewModel", AltFormAssets = $"Skins/{(Hunter)h}/Obsidian/AltForm", TurretAssets = $"Skins/{(Hunter)h}/Obsidian/Halfturret", Description = "Graphite armor with native palette accents" });
                skins.Add(new($"skin.{hunter}.alimbic", (ushort)(2 + h * 2), "Alimbic", (Hunter)h)
                { SurfaceTreatment = 2, DecalAsset = $"Decals/{(Hunter)h}/Alimbic.png", AlbedoSet = $"Skins/{(Hunter)h}/Alimbic/Biped", GunAssets = $"Skins/{(Hunter)h}/Alimbic/ViewModel", AltFormAssets = $"Skins/{(Hunter)h}/Alimbic/AltForm", TurretAssets = $"Skins/{(Hunter)h}/Alimbic/Halfturret", Description = "Warm alloy with etched energy channels" });
            }
            // IDs 1–14 remain unchanged for existing saves and recordings.
            string[] variants = { "ceramic", "circuit", "tiger", "nebula" };
            string[] names = { "Ceramic", "Circuit", "Tiger", "Nebula" };
            string[] descriptions = { "Ivory ceramic plates with dark seams", "Graphite circuitry with cyan traces", "Amber armor with diagonal black stripes", "Indigo starfield with violet nebula clouds" };
            for (int h = 0; h < 7; h++)
                for (int v = 0; v < variants.Length; v++)
                    skins.Add(new($"skin.{((Hunter)h).ToString().ToLowerInvariant()}.{variants[v]}",
                        (ushort)(15 + h * 4 + v), names[v], (Hunter)h)
                    { SurfaceTreatment = 3 + v, Description = descriptions[v], Collection = "Frontiers" });
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
