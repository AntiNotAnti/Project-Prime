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
            float time, uint seed, CosmeticLod lod, bool alt, Hunter hunter, Matrix4? previewTransform = null)
        {
            if (!RenderOptions.ShowCustomCosmetics || (alt && !effect.SupportsAltForm)
                || effect.ParticleStyle == ParticleStyle.None || lod >= CosmeticLod.Far
                || RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off) return;
            time = CosmeticDebug.FixedTime ?? time;
            int count = RenderOptions.CosmeticQuality == CosmeticEffectQuality.Low ? 2
                : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Medium ? 8 : 16;
            if (lod == CosmeticLod.Medium) count /= 2;
            count = Math.Min(count, Math.Max(0, (int)(effect.ParticleRate * 2)));
            var attachments = CosmeticAttachments.For(model);
            var silhouette = CosmeticSilhouette.For(hunter, alt);
            Matrix4 transform = previewTransform ?? Matrix4.Identity;
            float scale = transform.Row0.Xyz.Length;
            for (int i = 0; i < count; i++)
            {
                float phase = (time * 0.7f + Unit(seed + (uint)i * 17)) % 1;
                float angle = Unit(seed + (uint)i * 37) * MathF.Tau + phase;
                // Animated node positions wrap the actual pose, including hands
                // and feet. Alt forms use an orbit instead of humanoid nodes.
                Vector3 anchor = !alt && attachments.Length > 0
                    ? model.Nodes[attachments[i % attachments.Length].NodeIndex].Animation.Row3.Xyz : origin;
                Vector3 offset = new(MathF.Cos(angle) * 0.24f, phase * 0.45f, MathF.Sin(angle) * 0.24f);
                // Keep half the motes just outside the silhouette; skeletal
                // joint centers alone bury small sprites inside opaque armor.
                if (alt || (i & 1) == 0)
                {
                    anchor = origin + Vector3.TransformPosition(new Vector3(MathF.Cos(angle) * silhouette.Radius,
                        silhouette.Base + Unit(seed + (uint)i * 53) * silhouette.Height,
                        MathF.Sin(angle) * silhouette.Radius), transform);
                }
                float size = 0.065f;
                switch (effect.ParticleStyle)
                {
                    case ParticleStyle.Sparks: offset *= 0.5f + phase; break;
                    case ParticleStyle.Embers: offset.Y = phase * 0.65f; break;
                    case ParticleStyle.Vapor: offset *= 1.5f; size = 0.11f; break;
                    case ParticleStyle.Ice: offset.Y *= 0.1f; size = 0.05f; break;
                    case ParticleStyle.Wisps: offset.X *= 2; offset.Z *= 2; break;
                    case ParticleStyle.Motes: offset.Y = MathF.Sin(angle) * 0.2f; break;
                    case ParticleStyle.Pixels: offset.X = MathF.Round(offset.X * 12) / 12; offset.Y = MathF.Round(offset.Y * 12) / 12; break;
                    case ParticleStyle.Shards: offset *= 1.8f; size = 0.075f; break;
                }
                scene.AddSingleParticle(effect.ParticleStyle == ParticleStyle.Vapor ? SingleType.Fuzzball : SingleType.Death, anchor + Vector3.TransformVector(offset * (alt ? 0.55f : 1), transform),
                    Vector3.Lerp(effect.PrimaryColor, effect.SecondaryColor, phase), (0.25f + 0.75f * MathF.Sin(phase * MathF.PI)) * 0.85f, size * silhouette.ParticleScale * scale, cosmeticTint: true);
            }
        }
        public static void DrawDeath(Scene scene, Vector3 position, DeathPresentationDefinition effect, float progress, uint seed, Matrix4? previewTransform = null)
        {
            if (!RenderOptions.ShowCustomCosmetics || RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off
                || !float.IsFinite(progress) || progress < 0 || progress >= 1) return;
            Matrix4 transform = previewTransform ?? Matrix4.Identity;
            float scale = transform.Row0.Xyz.Length;
            int count = RenderOptions.CosmeticQuality == CosmeticEffectQuality.Low ? 6
                : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Medium ? 12 : 18;
            for (int i = 0; i < count; i++)
            {
                float angle = Unit(seed + (uint)i * 29) * MathF.Tau;
                float radius = effect.PoseStyle == DeathPoseStyle.KneelCollapse ? (1 - progress) * 1.3f : progress * 1.8f;
                float height = Unit(seed + (uint)i * 43);
                float y = effect.PoseStyle == DeathPoseStyle.Float ? height + progress * 2.2f
                    : effect.ParticleEffect == ParticleStyle.Ice ? height + progress * 0.6f - progress * progress * 1.2f
                    : height + progress * 0.8f;
                var offset = new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
                if (effect.ParticleEffect == ParticleStyle.Pixels)
                    offset = new(MathF.Round(offset.X * 8) / 8, MathF.Round(offset.Y * 8) / 8, MathF.Round(offset.Z * 8) / 8);
                scene.AddSingleParticle(SingleType.Death, position + Vector3.TransformPosition(offset, transform),
                    effect.WireId == 0 ? new Vector3(0.7f, 0.9f, 1) : effect.LightEffect,
                    1 - progress * progress, (effect.ParticleEffect == ParticleStyle.Ice ? 0.12f : 0.09f) * scale, cosmeticTint: effect.WireId != 0);
            }
        }
    }
}
