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
            int steps = RenderOptions.CosmeticQuality == CosmeticEffectQuality.High ? 12 : 6;
            // Trails are two continuous strips (soft halo + bright core), rather
            // than a draw call per segment. All buffers use the scene's pool lifecycle.
            Vector3 eyeDirection = previewTransform.HasValue ? new Vector3(0, 0.026f, 1)
                : scene.ViewMatrix.Inverted().Row2.Xyz.Normalized();
            for (int i = 0; i < count; i++)
            {
                bool cloud = effect.Motion == ArmorMotion.Pestilence;
                bool loop = effect.Motion is ArmorMotion.Orbit or ArmorMotion.Eclipse or ArmorMotion.Warp or ArmorMotion.Helix;
                float phase = (time * 0.6f + Unit(seed + (uint)i * 17)) % 1;
                bool cycling = effect.Motion is ArmorMotion.Inferno or ArmorMotion.Thunderstorm
                    or ArmorMotion.Void or ArmorMotion.Rain or ArmorMotion.Pestilence or ArmorMotion.Radiant;
                float envelope = effect.Motion == ArmorMotion.Warp ? MathF.Sin((time * 0.7f % 1) * MathF.PI)
                    : cycling ? MathF.Sin(phase * MathF.PI) : 1;
                Vector3 Position(float u)
                {
                    Vector3 point = Sample(effect.Motion, time, seed, i, u, count);
                    return origin + Vector3.TransformPosition(new Vector3(point.X * silhouette.Radius,
                        silhouette.Base + point.Y * silhouette.Height, point.Z * silhouette.Radius), transform);
                }
                scene.AddSingleParticle(cloud ? SingleType.Fuzzball : SingleType.Death,
                    Position(0), effect.SecondaryColor, envelope * (cloud ? 0.32f : 0.55f),
                    (cloud ? 0.22f : 0.045f) * silhouette.ParticleScale * scale, cosmeticTint: true);
                if (cloud) continue;
                for (int layer = 0; layer < 2; layer++)
                {
                    Vector3[] points = ArrayPool<Vector3>.Shared.Rent((steps + 1) * 4);
                    for (int j = 0; j <= steps; j++)
                    {
                        float u = j / (float)steps;
                        Vector3 position = Position(u);
                        Vector3 tangent = Position(u + 0.01f) - Position(u - 0.01f);
                        Vector3 side = RibbonSide(tangent, eyeDirection);
                        float taper = loop ? 1 : 0.15f + 0.85f * MathF.Sin(u * MathF.PI);
                        float width = effect.Motion == ArmorMotion.Inferno ? 0.05f * (1 - u * 0.8f) : 0.018f;
                        side *= width * scale * taper * (layer == 0 ? 2.8f : 0.65f);
                        points[j * 4] = new Vector3(0, u, 0);
                        points[j * 4 + 1] = position - side;
                        points[j * 4 + 2] = new Vector3(1, u, 0);
                        points[j * 4 + 3] = position + side;
                    }
                    Vector3 color = layer == 0 ? effect.PrimaryColor
                        : Vector3.Lerp(effect.SecondaryColor, Vector3.One, 0.25f);
                    scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
                        new Vector4(color, envelope * (layer == 0 ? 0.16f : 0.8f)),
                        RenderItemType.TrailMulti, points, (steps + 1) * 4, noLines: true);
                }
            }
        }

        internal static Vector3 RibbonSide(Vector3 tangent, Vector3 viewDirection)
        {
            Vector3 side = Vector3.Cross(tangent, viewDirection);
            if (side.LengthSquared < 0.000001f) side = Vector3.Cross(tangent, Vector3.UnitY);
            if (side.LengthSquared < 0.000001f) side = Vector3.UnitX;
            return side.Normalized();
        }

        // Normalized hunter-local envelope, shared by previews, gameplay and replay.
        internal static Vector3 Sample(ArmorMotion motion, float time, uint seed, int i, float u, int strandCount = 10)
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
                        1.1f, 1.3f - (phase + u * 0.5f) * 1.1f);
                case ArmorMotion.Pestilence:
                    return Polar(angle + phase + u, 0.9f + phase * 0.5f + u * 0.2f, phase + u * 0.2f);
                case ArmorMotion.Inferno:
                    return Polar(angle + MathF.Sin(time * 4 + u * 5) * u * 0.18f,
                        1.05f - u * 0.25f, phase + u * 0.5f);
                case ArmorMotion.Eclipse:
                    float halo = time * 0.5f + (i + u) * MathF.Tau / strandCount;
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
                    return Polar(angle, 0.95f + u * (0.5f + MathF.Sin(time * 2) * 0.15f), y + u * (y - 0.5f) * 0.4f);
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
                    int rings = Math.Min(3, strandCount);
                    int arcs = (strandCount - 1 - i % rings) / rings + 1;
                    float orbit = time + (i / rings + u) * MathF.Tau / arcs;
                    Vector3 ring = Polar(orbit, 1.35f, 0);
                    return (i % 3) switch { 0 => ring + new Vector3(0, 0.5f, 0),
                        1 => new(ring.X, 0.5f + ring.Z * 0.45f, ring.Z * 0.3f),
                        _ => new(ring.X * 0.3f, 0.5f + ring.X * 0.45f, ring.Z) };
                case ArmorMotion.Helix:
                    float h = (i / 2 + u) / ((strandCount + 1 - i % 2) / 2);
                    return Polar(h * MathF.Tau + time + (i % 2) * MathF.PI, 1.1f, h);
                case ArmorMotion.Warp:
                    return Polar((i + u) * MathF.Tau / strandCount, 1 + (time * 0.7f % 1) * 0.7f, time * 0.7f % 1);
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
