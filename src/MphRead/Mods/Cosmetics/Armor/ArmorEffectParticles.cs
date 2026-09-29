using System;
using System.Buffers;
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
            var silhouette = CosmeticSilhouette.For(hunter, alt);
            Matrix4 transform = previewTransform ?? Matrix4.Identity;
            float scale = transform.Row0.Xyz.Length;
            // Six analytic samples form ribbons or angular shapes.
            // At most 16 sprites plus 80 pooled quads; no simulation RNG/state.
            for (int i = 0; i < count; i++)
                for (int j = 0; j < 6; j++)
                {
                    float u = j / 5f;
                    Vector3 point = Sample(effect.Motion, time, seed, i, u);
                    var offset = new Vector3(point.X * silhouette.Radius,
                        silhouette.Base + point.Y * silhouette.Height, point.Z * silhouette.Radius);
                    bool cloud = effect.Motion == ArmorMotion.Pestilence;
                    float size = cloud ? 0.10f + u * 0.09f : 0.035f + (1 - u) * 0.025f;
                    if (effect.Motion == ArmorMotion.Inferno) size = 0.095f * (1 - u * 0.8f);
                    float alpha = cloud ? 0.16f : 0.72f - u * 0.35f;
                    Vector3 position = origin + Vector3.TransformPosition(offset, transform);
                    Vector3 color = Vector3.Lerp(effect.PrimaryColor, effect.SecondaryColor, u);
                    if (j == 0)
                        scene.AddSingleParticle(cloud ? SingleType.Fuzzball : SingleType.Death,
                            position, color, alpha, size * silhouette.ParticleScale * scale, cosmeticTint: true);
                    if (j > 0)
                    {
                        Vector3 previous = Sample(effect.Motion, time, seed, i, (j - 1) / 5f);
                        previous = origin + Vector3.TransformPosition(new Vector3(previous.X * silhouette.Radius,
                            silhouette.Base + previous.Y * silhouette.Height, previous.Z * silhouette.Radius), transform);
                        Vector3 direction = position - previous;
                        // Skip wrapped/discontinuous samples rather than drawing a body-spanning streak.
                        if (direction.LengthSquared > 0.000001f && direction.LengthSquared < scale * scale * 0.8f)
                        {
                            Vector3 side = Vector3.Cross(direction, Vector3.UnitZ);
                            if (side.LengthSquared < 0.000001f) side = Vector3.Cross(direction, Vector3.UnitY);
                            side = side.Normalized() * size * scale * (cloud ? 1.5f : 0.6f);
                            Vector3[] points = ArrayPool<Vector3>.Shared.Rent(4);
                            points[0] = previous - side; points[1] = previous + side;
                            points[2] = position + side; points[3] = position - side;
                            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
                                new Vector4(color, alpha), RenderItemType.Quad, points, noLines: true);
                        }
                    }
                }
        }

        // Normalized hunter-local envelope, shared by previews, gameplay and replay.
        internal static Vector3 Sample(ArmorMotion motion, float time, uint seed, int i, float u)
        {
            float random = Unit(seed + (uint)i * 37);
            float phase = (time * 0.6f + Unit(seed + (uint)i * 17)) % 1;
            float angle = random * MathF.Tau;
            float y = Unit(seed + (uint)i * 53);
            Vector3 Polar(float a, float r, float h) => new(MathF.Cos(a) * r, h, MathF.Sin(a) * r);
            switch (motion)
            {
                case ArmorMotion.Lightning:
                    float jitter = (Unit(seed + (uint)(time * 12) * 97 + (uint)(i * 6 + u * 5)) - 0.5f) * 0.3f;
                    return Polar(angle + u * 0.65f, 1.05f + jitter, y + u * 0.25f);
                case ArmorMotion.Thunderstorm:
                    return Polar(angle + MathF.Sin(u * 25 + MathF.Floor(time * 9)) * 0.12f,
                        1.1f, 1.3f - ((phase + u * 0.5f) % 1) * 1.4f);
                case ArmorMotion.Pestilence:
                    return Polar(angle + phase + u, 0.9f + phase * 0.5f + u * 0.2f, phase + u * 0.2f);
                case ArmorMotion.Inferno:
                    return Polar(angle + MathF.Sin(time * 4 + u * 5) * u * 0.18f,
                        1.05f - u * 0.25f, (phase + u * 0.5f) % 1.25f);
                case ArmorMotion.Eclipse:
                    float halo = time * 0.5f + (i + u) * MathF.Tau / 10;
                    return new(MathF.Cos(halo) * 1.35f, 0.55f + MathF.Sin(halo) * 0.65f, MathF.Sin(time * 0.4f) * MathF.Cos(halo) * 0.5f);
                case ArmorMotion.Glacial:
                    float corner = u * MathF.Tau;
                    return Polar(angle + time * 0.3f + MathF.Sin(corner) * 0.12f,
                        1.12f, y + MathF.Cos(corner) * 0.13f);
                case ArmorMotion.Void:
                    return Polar(angle + phase * 4 + u, 1.65f * (1 - phase) + 0.1f, 0.5f + (y - 0.5f) * (1 - phase) + u * 0.12f);
                case ArmorMotion.Radiant:
                    return Polar(angle, 1 + phase * 0.5f + u * 0.65f, 0.5f + (y - 0.5f) * (1 + u));
                case ArmorMotion.Phase:
                    return Polar(angle + MathF.Floor(u * 3) * 0.18f, 1.12f, MathF.Floor((phase + u * 0.25f) * 8) / 8);
                case ArmorMotion.Spectral:
                    return Polar(angle + time + u * 1.8f, 1.15f + u * 0.3f, y + MathF.Sin(time + u * 3) * 0.2f);
                case ArmorMotion.Spike:
                    return Polar(angle, 0.95f + u * (0.35f + phase * 0.3f), y + u * (y - 0.5f) * 0.4f);
                case ArmorMotion.Solar:
                    return Polar(angle + u * 0.5f + time * 0.25f, 1 + MathF.Sin(u * MathF.PI) * 0.7f, y + MathF.Sin(u * MathF.Tau) * 0.18f);
                case ArmorMotion.Lumen:
                    Vector3 center = Polar(angle + time * 0.2f, 1.2f, y);
                    return center + ((i & 1) == 0 ? Vector3.UnitY : Vector3.UnitX) * ((u - 0.5f) * 0.35f);
                case ArmorMotion.Corruption:
                    return Polar(angle + u * 0.6f, 1 + MathF.Sin(u * 15 + time * 2) * 0.14f, y - u * 0.35f);
                case ArmorMotion.Aurora:
                    return Polar(angle + u * 0.55f, 1.25f, 0.45f + u * 0.65f + MathF.Sin(angle * 2 + time + u * 3) * 0.22f);
                case ArmorMotion.Quantum:
                    return Polar(MathF.Floor((angle + time) * 3) / 3, 1.15f + u * 0.2f, MathF.Floor((y + time * 0.3f) % 1 * 6) / 6);
                case ArmorMotion.Orbit:
                    float orbit = time + (i / 3 + u) * MathF.Tau / 4;
                    Vector3 ring = Polar(orbit, 1.35f, 0);
                    return (i % 3) switch { 0 => ring + new Vector3(0, 0.5f, 0),
                        1 => new(ring.X, 0.5f + ring.Z * 0.45f, ring.Z * 0.3f),
                        _ => new(ring.X * 0.3f, 0.5f + ring.X * 0.45f, ring.Z) };
                case ArmorMotion.Helix:
                    float h = (i / 2 + u) / 5;
                    return Polar(h * MathF.Tau + time + (i % 2) * MathF.PI, 1.1f, h);
                case ArmorMotion.Warp:
                    return Polar((i + u) * MathF.Tau / 10, 1 + (time * 0.7f % 1) * 0.7f, time * 0.7f % 1);
                case ArmorMotion.Rain:
                    return Polar(angle, 1.25f + u * 0.25f, 1.25f - phase * 1.5f + u * 0.3f);
                default: return Polar(angle, 1, y);
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
