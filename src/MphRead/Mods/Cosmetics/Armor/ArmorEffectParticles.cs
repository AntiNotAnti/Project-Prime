using System;
using OpenTK.Mathematics;
using MphRead.Mods.Cosmetics.Death;
namespace MphRead.Mods.Cosmetics.Armor
{
    // Uses Scene's bounded, preallocated single-particle pool. Particles are
    // analytic samples, so seeking/respawning cannot leave live emitters behind.
    public static class ArmorEffectParticles
    {
        private static float Unit(uint seed) { seed ^= seed >> 16; seed *= 0x7feb352d; seed ^= seed >> 15; return (seed & 65535) / 65535f; }
        public static void Draw(Scene scene, Model model, Vector3 origin, ArmorEffectDefinition effect,
            float time, uint seed, CosmeticLod lod, bool alt)
        {
            if (effect.ParticleStyle == ParticleStyle.None || lod >= CosmeticLod.Far
                || RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off) return;
            time = CosmeticDebug.FixedTime ?? time;
            int count = RenderOptions.CosmeticQuality == CosmeticEffectQuality.Low ? 2
                : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Medium ? 8 : 16;
            if (lod == CosmeticLod.Medium) count /= 2;
            count = Math.Min(count, Math.Max(0, (int)(effect.ParticleRate * 2)));
            var attachments = CosmeticAttachments.For(model);
            for (int i = 0; i < count; i++)
            {
                float phase = (time * 0.7f + Unit(seed + (uint)i * 17)) % 1;
                float angle = Unit(seed + (uint)i * 37) * MathF.Tau + phase;
                // Animated node positions wrap the actual pose, including hands
                // and feet. Alt forms use an orbit instead of humanoid nodes.
                Vector3 anchor = !alt && attachments.Length > 0
                    ? model.Nodes[attachments[i % attachments.Length].NodeIndex].Animation.Row3.Xyz : origin;
                Vector3 offset = new(MathF.Cos(angle) * 0.13f, phase * 0.35f, MathF.Sin(angle) * 0.13f);
                float size = 0.025f;
                switch (effect.ParticleStyle)
                {
                    case ParticleStyle.Sparks: offset *= 0.5f + phase; break;
                    case ParticleStyle.Embers: offset.Y = phase * 0.65f; break;
                    case ParticleStyle.Vapor: offset *= 1.5f; size = 0.04f; break;
                    case ParticleStyle.Ice: offset.Y *= 0.1f; size = 0.018f; break;
                    case ParticleStyle.Wisps: offset.X *= 2; offset.Z *= 2; break;
                    case ParticleStyle.Motes: offset.Y = MathF.Sin(angle) * 0.2f; break;
                    case ParticleStyle.Pixels: offset.X = MathF.Round(offset.X * 12) / 12; offset.Y = MathF.Round(offset.Y * 12) / 12; break;
                    case ParticleStyle.Shards: offset *= 1.8f; size = 0.045f; break;
                }
                scene.AddSingleParticle(effect.ParticleStyle == ParticleStyle.Vapor ? SingleType.Fuzzball : SingleType.Death, anchor + offset,
                    Vector3.Lerp(effect.PrimaryColor, effect.SecondaryColor, phase), (1 - phase) * 0.5f, size);
            }
        }
        public static void DrawDeath(Scene scene, Vector3 position, DeathPresentationDefinition effect, float progress, uint seed)
        {
            int count = RenderOptions.CosmeticQuality == CosmeticEffectQuality.Low ? 6 : 18;
            for (int i = 0; i < count; i++)
            {
                float angle = Unit(seed + (uint)i * 29) * MathF.Tau;
                float radius = effect.PoseStyle == DeathPoseStyle.KneelCollapse ? 1 - progress : progress;
                float height = Unit(seed + (uint)i * 43);
                var offset = new Vector3(MathF.Cos(angle) * radius, height + progress * 0.5f, MathF.Sin(angle) * radius);
                scene.AddSingleParticle(SingleType.Death, position + offset, effect.LightEffect,
                    1 - progress, effect.ParticleEffect == ParticleStyle.Ice ? 0.09f : 0.045f);
            }
        }
    }
}
