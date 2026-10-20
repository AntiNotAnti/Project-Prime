using System;
using System.Buffers;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Scene-owned, depth-tested, cosmetic-only Insta-Gib presentation. The
    /// canonical Imperialist collision endpoint and shot identity are immutable.
    /// All styles share the original accepted shot identity and scene lifetime.
    /// The existing trail material supplies soft texturing, without new assets.
    /// </summary>
    internal sealed class InstagibIonFracture
    {
        internal const float LifeSeconds = 0.9f;
        internal const int Capacity = 24;
        internal readonly record struct RailLook(float Core, float Glow, float Spiral, float Radius);
        internal static RailLook LookAt(float age)
        {
            float t = Math.Clamp(age, 0f, 1f);
            float left = 1f - t;
            return new(MathF.Pow(left, 0.8f), 0.75f * MathF.Pow(left, 1.5f),
                0.95f * MathF.Pow(left, 0.9f), 0.11f * (1f + 1.8f * t));
        }
        internal static float TaperAt(float distance, float width, float fullAt)
        {
            float t = Math.Clamp(distance / fullAt, 0f, 1f);
            return width * (0.12f + 0.88f * t * (2f - t));
        }
        internal static Vector3 HelixDisplacement(Vector3 u, Vector3 v,
            float along, float age, float phase, int strand)
        {
            float opening = MathF.Min(1f, MathF.Sqrt(MathF.Max(0f, along) / 4f));
            float angle = MathF.Tau * along / 4f + phase + age * (LifeSeconds * 14f)
                + (strand == 1 ? MathF.PI : 0f);
            return (u * MathF.Cos(angle) + v * MathF.Sin(angle))
                * (LookAt(age).Radius * opening);
        }

        // Scene-bound original beam-trail artwork, never shared across scenes.
        private ModelInstance? _trail;
        private int _trailTexture = -1;
        private RepeatMode _repeatX, _repeatY;
        private float _scaleX = 1f, _scaleY = 1f;
        private float _uvX = 1f, _uvY = 1f;

        private readonly struct Lance
        {
            internal readonly Vector3 Start, End, Color, Secondary;
            internal readonly float Born;
            internal readonly int Slot;
            internal readonly uint ShotId, LaunchFrame;
            internal readonly BeamFxStyle Style;
            internal readonly bool HitPlayer;
            internal Lance(Vector3 start, Vector3 end, Vector3 color, Vector3 secondary,
                float born, int slot, uint shotId, uint launchFrame, BeamFxStyle style,
                bool hitPlayer)
            {
                Start = start; End = end; Color = color; Secondary = secondary;
                Born = born; Slot = slot; ShotId = shotId;
                LaunchFrame = launchFrame; Style = style; HitPlayer = hitPlayer;
            }
        }

        private static readonly Vector3[] Palette =
        {
            new(0.13f, 0.88f, 1.00f), new(1.00f, 0.22f, 0.73f),
            new(0.58f, 1.00f, 0.18f), new(1.00f, 0.53f, 0.16f),
            new(1.00f, 0.88f, 0.28f), new(1.00f, 0.18f, 0.22f),
            new(0.54f, 0.42f, 1.00f), new(0.55f, 0.86f, 1.00f)
        };
        private readonly Lance[] _active = new Lance[Capacity];
        private int _count;
        private float _lastTime;
        internal int Count => _count;
        internal static Vector3 ColorForSlot(int slot) => Palette[slot < 0 ? 0 : slot % Palette.Length];
        internal static float FadeAt(float normalizedAge)
        {
            float left = 1 - Math.Clamp(normalizedAge, 0, 1);
            return left * left * (3 - 2 * left);
        }
        internal void Clear() => _count = 0;
        private static bool Finite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        internal bool Add(Scene scene, Vector3 start, Vector3 end, int slot,
            uint shotId, uint launchFrame, BeamEffectDefinition? effect = null, byte tone = 0,
            bool hitPlayer = false)
        {
            if (!scene.GameState.Multiplayer || !scene.GameState.InstaGib
                || !Finite(start) || !Finite(end) || (end - start).LengthSquared < 0.0025f)
                return false;
            float now = scene.ElapsedTime;
            if (now + 0.001f < _lastTime) Clear();
            _lastTime = now;
            for (int i = 0; i < _count; i++)
            {
                Lance old = _active[i];
                if (old.Slot != slot || now - old.Born > LifeSeconds) continue;
                if (shotId != 0 && old.ShotId == shotId && old.LaunchFrame == launchFrame)
                    return false;
                if (shotId == 0 && old.ShotId == 0 && MathF.Abs(now - old.Born) < 1f / 120f
                    && (old.Start - start).LengthSquared < 0.01f
                    && (old.End - end).LengthSquared < 0.01f)
                    return false;
            }

            // Drop the oldest effect instead of allowing unbounded multi-player
            // bursts to accumulate render submissions or heap state.
            if (_count == Capacity)
            {
                Array.Copy(_active, 1, _active, 0, Capacity - 1);
                _count--;
            }
            effect ??= CosmeticCatalog.BeamEffects[0];
            Vector3 original = effect.Style == BeamFxStyle.IonFracture
                ? ColorForSlot(slot) : effect.PrimaryColor;
            Vector3 color = BeamPalette.Resolve(tone, original);
            _active[_count++] = new Lance(start, end, color, effect.SecondaryColor,
                now, slot, shotId, launchFrame, effect.Style, hitPlayer);
            return true;
        }

        /// <summary>
        /// Pure, reproducible motion envelopes for each non-default signature.
        /// A non-zero transverse shape is visible for most of the rail's
        /// lifetime, unlike one brief near-impact particle burst.
        /// The envelope goes to zero at both ends, preserving muzzle/impact
        /// positions and never changing the weapon's actual collision ray.
        /// </summary>
        internal static Vector3 SignatureOffset(BeamFxStyle style, float progress,
            float normalizedAge, int strand, Vector3 right, Vector3 up)
        {
            float u = Math.Clamp(progress, 0f, 1f);
            float t = Math.Clamp(normalizedAge, 0f, 1f);
            float envelope = MathF.Sin(u * MathF.PI);
            float sign = strand == 0 ? 1f : -1f;
            float tau = MathF.Tau;
            float travel = u * tau;
            switch (style)
            {
                case BeamFxStyle.VoidRift:
                    // Two void seams continuously fold inward and out.
                    float tear = 0.15f + 0.11f * MathF.Cos(u * tau * 3.4f - t * 13f);
                    return (right * (sign * tear)
                        + up * (sign * 0.065f * MathF.Sin(travel * 5f + t * 9f)))
                        * envelope;
                case BeamFxStyle.QuantumSplit:
                    // Diverging/converging ghost trajectories, magenta/cyan.
                    float split = 0.13f + 0.28f * MathF.Sin(u * MathF.PI)
                        * (0.70f + 0.30f * MathF.Sin(t * 15f + travel));
                    return (right * (sign * split)
                        + up * (0.095f * sign * MathF.Sin(travel * 2.1f + t * 17f)))
                        * envelope;
                case BeamFxStyle.AlimbicResonance:
                    // A faceted six-sided energy cage, not another sine helix.
                    float cell = MathF.Floor(u * 9f + t * 3.7f);
                    float hex = (cell % 6f) * tau / 6f;
                    return (right * (sign * 0.22f * MathF.Cos(hex))
                        + up * (sign * 0.22f * MathF.Sin(hex)))
                        * envelope;
                case BeamFxStyle.OmegaPulse:
                    // Three moving compression packets bulge down the lance.
                    float wave = MathF.Max(0f, MathF.Sin(travel * 3f - t * 17f));
                    float compression = 0.095f + 0.30f * wave * wave * wave * wave;
                    return (right * (sign * compression)
                        + up * (sign * 0.075f * MathF.Sin(travel * 3f - t * 17f)))
                        * envelope;
                case BeamFxStyle.ArcDisruptor:
                    // Chopped zig-zag, with opposite-polarity discharges.
                    float charge = MathF.Sin(travel * 8.3f - t * 28f);
                    float crack = charge >= 0f ? 1f : -1f;
                    return (right * (sign * 0.19f * crack)
                        + up * (0.11f * MathF.Sin(travel * 11f - t * 20f + strand)))
                        * envelope;
                case BeamFxStyle.SolarSpear:
                    // Two incandescent flame tongues twist independently.
                    float flare = travel * 4.4f - t * 15f + strand * MathF.PI;
                    float billow = 0.18f + 0.055f * MathF.Sin(travel * 13f + t * 23f);
                    return (right * MathF.Cos(flare) + up * MathF.Sin(flare))
                        * billow * envelope;
                case BeamFxStyle.CryoShatter:
                    // Polygonal crystalline facets advancing in hard steps.
                    float facet = u * 7.7f + t * 4.1f + strand * 0.5f;
                    float triangle = 1f - 4f * MathF.Abs((facet - MathF.Floor(facet)) - 0.5f);
                    return (right * (sign * 0.21f * triangle)
                        + up * (sign * 0.10f * (1f - MathF.Abs(triangle))))
                        * envelope;
                case BeamFxStyle.PhantomTrace:
                    // Two slowly drifting holographic afterimages.
                    float drift = 0.16f + 0.19f * t
                        + 0.035f * MathF.Sin(travel * 3f - t * 9f);
                    return (right * (sign * drift)
                        + up * (0.055f * sign * MathF.Sin(travel * 2.3f + t * 7f)))
                        * envelope;
                default:
                    return Vector3.Zero;
            }
        }

        internal readonly record struct SignatureStyle(float Width, float Opacity, float Reach);
        internal static SignatureStyle SignatureFor(BeamFxStyle style) => style switch
        {
            BeamFxStyle.VoidRift => new(0.058f, 0.88f, 44f),
            BeamFxStyle.QuantumSplit => new(0.050f, 0.83f, 55f),
            BeamFxStyle.AlimbicResonance => new(0.044f, 0.91f, 45f),
            BeamFxStyle.OmegaPulse => new(0.068f, 0.92f, 56f),
            BeamFxStyle.ArcDisruptor => new(0.044f, 0.88f, 48f),
            BeamFxStyle.SolarSpear => new(0.060f, 0.90f, 60f),
            BeamFxStyle.CryoShatter => new(0.052f, 0.92f, 50f),
            BeamFxStyle.PhantomTrace => new(0.037f, 0.44f, 60f),
            _ => new(0.031f, 0.95f, 36f)
        };

        // Caps decorative draw submissions in 8-player spam. Every shot
        // retains its actual depth-tested center rail even after the optional
        // accent budget is exhausted. Prioritize fresh, nearby shots.
        internal static CosmeticEffectQuality DetailQualityForRange(
            CosmeticEffectQuality desired, float distance, int remainingBudget)
        {
            if (desired == CosmeticEffectQuality.Off || remainingBudget <= 0
                || !float.IsFinite(distance) || distance > 75f)
                return CosmeticEffectQuality.Off;
            if (distance > 35f && desired > CosmeticEffectQuality.Low)
                return CosmeticEffectQuality.Low;
            if (distance > 20f && desired > CosmeticEffectQuality.Medium)
                return CosmeticEffectQuality.Medium;
            return desired;
        }

        internal void Draw(Scene scene)
        {
            if (!scene.GameState.Multiplayer || !scene.GameState.InstaGib)
            {
                Clear();
                return;
            }
            float now = scene.ElapsedTime;
            if (now + 0.001f < _lastTime) Clear();
            _lastTime = now;
            bool reduceMotion = HudProfiles.Runtime.ReduceMotion;
            bool reduceTransparency = HudProfiles.Runtime.ReduceTransparency;
            CosmeticEffectQuality quality = RenderOptions.CosmeticQuality;
            bool textured = TryPrepareTrail(scene);
            Vector3 view = scene.ViewMatrix.Inverted().Row2.Xyz;
            if (view.LengthSquared < 0.000001f) view = Vector3.UnitZ;
            else view.Normalize();
            int remaining = 0;
            for (int i = 0; i < _count; i++)
            {
                Lance lance = _active[i];
                float age = now - lance.Born;
                if (age < 0 || age >= LifeSeconds) continue;
                _active[remaining++] = lance;
            }
            _count = remaining;

            Vector3 camera = scene.ViewMatrix.Inverted().Row3.Xyz;
            if (!Finite(camera)) camera = Vector3.Zero;
            int detailBudget = quality switch
            {
                CosmeticEffectQuality.High => 12,
                CosmeticEffectQuality.Medium => 8,
                CosmeticEffectQuality.Low => 4,
                _ => 0
            };
            for (int i = remaining - 1; i >= 0; i--)
            {
                Lance lance = _active[i];
                Vector3 delta = lance.End - lance.Start;
                float lengthSquared = delta.LengthSquared;
                float along = lengthSquared < 0.0001f ? 0f
                    : Math.Clamp(Vector3.Dot(camera - lance.Start, delta) / lengthSquared, 0f, 1f);
                float distance = (camera - (lance.Start + delta * along)).Length;
                var effective = DetailQualityForRange(quality, distance, detailBudget);
                if (effective != CosmeticEffectQuality.Off && !reduceMotion) detailBudget--;
                DrawLance(scene, lance, camera: camera, view: view, age: now - lance.Born,
                    quality: effective, reducedMotion: reduceMotion,
                    reducedTransparency: reduceTransparency, textured: textured);
            }
        }

        private void DrawLance(Scene scene, in Lance lance, Vector3 camera, float age, Vector3 view,
            CosmeticEffectQuality quality, bool reducedMotion, bool reducedTransparency, bool textured)
        {
            Vector3 delta = lance.End - lance.Start;
            float length = delta.Length;
            if (length <= 0.05f) return;
            Vector3 axis = delta / length;
            Vector3 right = Perpendicular(axis, view);
            Vector3 up = Vector3.Cross(axis, right).Normalized();
            float fraction = age / LifeSeconds;
            float fade = FadeAt(fraction);
            RailLook look = LookAt(fraction);
            Vector3 hot = Vector3.Lerp(lance.Color, Vector3.One, 0.55f);

            // Preserve the original world depth test. A soft textured halo,
            // stronger colored band, and white-hot core replace the hairline.
            if (textured)
            {
                DrawTexturedRibbon(scene, lance.Start, axis, camera, right, up, length,
                    9, 0.24f, 9f, lance.Color,
                    reducedTransparency || quality == CosmeticEffectQuality.Off ? 0f : look.Glow,
                    0f, -1, fraction);
                DrawTexturedRibbon(scene, lance.Start, axis, camera, right, up, length,
                    9, 0.11f, 5f, lance.Color, look.Core * 0.90f,
                    0f, -1, fraction);
                DrawTexturedRibbon(scene, lance.Start, axis, camera, right, up, length,
                    9, 0.06f, 3f, hot, look.Core, 0f, -1, fraction);
            }
            else
            {
                if (!reducedTransparency && quality != CosmeticEffectQuality.Off)
                    DrawShaft(scene, lance.Start, lance.End, right, 0.24f,
                        lance.Color, look.Glow * 0.45f);
                DrawShaft(scene, lance.Start, lance.End, right, 0.11f,
                    lance.Color, look.Core * 0.90f);
                DrawShaft(scene, lance.Start, lance.End, right, 0.06f, hot, look.Core);
            }

            // Default signature: two animated filaments around the exact rail.
            // Use 73/43 samples, rather than hundreds of segments per shot.
            if (textured && !reducedMotion
                && lance.Style == BeamFxStyle.IonFracture
                && quality >= CosmeticEffectQuality.Medium)
            {
                float reach = MathF.Min(length, 36f);
                int samples = quality == CosmeticEffectQuality.High ? 73 : 43;
                uint identity = lance.ShotId == 0 ? lance.LaunchFrame : lance.ShotId;
                float phase = (identity % 16u) * (MathF.Tau / 16f);
                Vector3 bright = new(MathF.Min(1f, lance.Color.X * 1.1f + 0.12f),
                    MathF.Min(1f, lance.Color.Y * 1.1f + 0.12f),
                    MathF.Min(1f, lance.Color.Z * 1.1f + 0.12f));
                for (int strand = 0; strand < 2; strand++)
                    DrawTexturedRibbon(scene, lance.Start, axis, camera, right, up,
                        reach, samples, 0.031f, 4f, bright, look.Spiral,
                        phase, strand, fraction);
            }

            // Every beam has its own persistent motion grammar, not just a
            // different color on the same thick rail. Even Low detail retains
            // one characteristic moving strand; Medium/High render the pair.
            // The original Ion Fracture helix above remains untouched.
            if (textured && !reducedMotion
                && lance.Style != BeamFxStyle.IonFracture
                && quality != CosmeticEffectQuality.Off)
            {
                SignatureStyle signature = SignatureFor(lance.Style);
                float reach = MathF.Min(length, signature.Reach);
                int strands = quality == CosmeticEffectQuality.Low ? 1 : 2;
                int samples = quality == CosmeticEffectQuality.High ? 57
                    : quality == CosmeticEffectQuality.Medium ? 37 : 21;
                Vector3 secondary = lance.Style is BeamFxStyle.QuantumSplit
                    or BeamFxStyle.AlimbicResonance or BeamFxStyle.ArcDisruptor
                    or BeamFxStyle.SolarSpear or BeamFxStyle.CryoShatter
                        ? lance.Secondary
                        : Vector3.Lerp(lance.Color, Vector3.One, 0.27f);
                for (int strand = 0; strand < strands; strand++)
                {
                    Vector3 tint = strand == 0 ? secondary : lance.Color;
                    DrawTexturedRibbon(scene, lance.Start, axis, camera, right, up,
                        reach, samples, signature.Width, 4f, tint,
                        look.Spiral * signature.Opacity, 0f, strand, fraction,
                        signatureStyle: lance.Style);
                }
            }

            if (reducedMotion || quality == CosmeticEffectQuality.Off) return;
            bool full = quality == CosmeticEffectQuality.High;
            bool medium = quality >= CosmeticEffectQuality.Medium;
            float pulse = Math.Clamp(age / 0.19f, 0f, 1f);
            float blossom = MathF.Sin(pulse * MathF.PI);
            float impact = 1f - Math.Clamp(age / 0.24f, 0f, 1f);
            Vector3 center = lance.Start + delta * (0.06f + 0.91f * pulse);

            // Small, momentary muzzle compression: never covers the reticle,
            // and does not introduce any fire-time delay or recoil.
            if (age < 0.08f && medium)
                DrawRing(scene, lance.Start + axis * 0.12f, right, up,
                    4, 0.11f + age * 0.45f, 0.024f, hot,
                    (1f - age / 0.08f) * 0.62f);

            // A real player collision emits a short prismatic spark rather
            // than a surface glyph. Both paths stop at the canonical impact.
            if (lance.HitPlayer && impact > 0)
            {
                int rays = full ? 4 : 2;
                for (int i = 0; i < rays; i++)
                {
                    float a = MathF.Tau * i / rays + 0.33f;
                    Vector3 spread = right * MathF.Cos(a) + up * MathF.Sin(a);
                    DrawFacet(scene, lance.End - axis * 0.04f,
                        lance.End + spread * (0.22f + age * 0.76f) - axis * 0.12f,
                        axis, 0.025f, 0.003f, hot, impact * 0.74f);
                }
            }

            if (!lance.HitPlayer && impact > 0 && medium)
            {
                // Shallow wall/door collision flare on the shooter's side.
                // Decorative sparks fan only away from the collision plane;
                // geometry never pushes a decal behind the level surface.
                Vector3 origin = lance.End - axis * 0.075f;
                for (int i = 0; i < (full ? 3 : 2); i++)
                {
                    float angle = (i - 1) * 0.74f + lance.Slot * 0.07f;
                    Vector3 spoke = right * MathF.Cos(angle) + up * MathF.Sin(angle);
                    Vector3 tip = origin + spoke * (0.17f + age * 0.37f)
                        - axis * (0.10f + age * 0.21f);
                    DrawFacet(scene, origin, tip, axis, 0.021f, 0.002f,
                        lance.Color, impact * 0.62f);
                }
            }

            if (impact > 0 && full && age < 0.13f)
            {
                // Crossed restrained emissive prism, not a real dynamic light.
                // Depth-tested and short-lived to avoid glowing through walls.
                float glow = (1f - age / 0.13f) * 0.27f;
                Vector3 source = lance.End - axis * 0.085f;
                DrawFacet(scene, source - right * 0.12f, source + right * 0.12f,
                    up, 0.034f, 0.034f, hot, glow);
                DrawFacet(scene, source - up * 0.12f, source + up * 0.12f,
                    right, 0.034f, 0.034f, hot, glow);
            }

            switch (lance.Style)
            {
                case BeamFxStyle.IonFracture:
                    if (age < 0.19f)
                    {
                        Vector3 fork = (right + up) * (0.19f * blossom);
                        DrawFacet(scene, center - axis * 0.21f - fork,
                            center + axis * 0.21f + fork, up, 0.024f, 0.007f,
                            hot, fade * blossom);
                        DrawFacet(scene, center - axis * 0.21f + fork,
                            center + axis * 0.21f - fork, right, 0.020f, 0.006f,
                            lance.Color, fade * blossom * 0.8f);
                        if (full) DrawRing(scene, center, right, up, 4,
                            0.10f + 0.09f * blossom, 0.018f, hot, fade * blossom);
                    }
                    if (medium) Shards(scene, lance.Start, delta, axis, right, up,
                        lance.Color, fade, fraction, 3, outward: true);
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.07f,
                        right, up, full ? 6 : 3, 0.21f + age * 0.9f, 0.036f,
                        hot, impact * 0.76f);
                    break;

                case BeamFxStyle.VoidRift:
                    if (medium)
                    {
                        DrawShaft(scene, lance.Start + right * 0.08f, lance.End + right * 0.08f,
                            right, 0.013f, lance.Color, fade * 0.65f);
                        DrawShaft(scene, lance.Start - right * 0.08f, lance.End - right * 0.08f,
                            right, 0.013f, lance.Secondary, fade * 0.85f);
                    }
                    if (full) Shards(scene, lance.Start, delta, axis, right, up,
                        lance.Color, fade, fraction, 3, outward: false);
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.06f,
                        right, up, 4, 0.14f + age * 0.72f, 0.035f,
                        lance.Color, impact * 0.9f);
                    break;

                case BeamFxStyle.QuantumSplit:
                    if (medium)
                    {
                        float spread = 0.11f * MathF.Sin(MathF.PI * fraction);
                        Vector3 skew = (right + up * 0.36f) * spread;
                        DrawShaft(scene, lance.Start, lance.End + skew, right,
                            0.014f, lance.Secondary, fade * 0.64f);
                        DrawShaft(scene, lance.Start, lance.End - skew, right,
                            0.014f, lance.Secondary, fade * 0.50f);
                    }
                    if (full)
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 site = lance.Start + delta * (0.27f + i * 0.24f);
                            DrawFacet(scene, site - up * 0.11f, site + right * 0.11f,
                                axis, 0.012f, 0.012f, lance.Secondary, fade * 0.58f);
                        }
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.06f,
                        right, up, 4, 0.18f + age * 0.6f, 0.020f,
                        lance.Secondary, impact * 0.8f);
                    break;

                case BeamFxStyle.AlimbicResonance:
                    if (medium)
                        DrawRing(scene, lance.Start + delta * (0.19f + fraction * 0.08f),
                            right, up, full ? 6 : 4, 0.17f, 0.025f,
                            lance.Color, fade * 0.72f);
                    if (full)
                        DrawRing(scene, lance.Start + delta * (0.67f + fraction * 0.06f),
                            right, up, 6, 0.20f, 0.018f, lance.Secondary, fade * 0.56f);
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.06f,
                        right, up, full ? 6 : 4, 0.19f + age * 0.62f, 0.032f,
                        lance.Color, impact * 0.83f);
                    break;

                case BeamFxStyle.OmegaPulse:
                    int rings = full ? 3 : medium ? 2 : 1;
                    for (int i = 0; i < rings; i++)
                    {
                        float along = (0.19f + i * 0.26f + fraction * 0.26f) % 0.94f;
                        DrawRing(scene, lance.Start + delta * along, right, up,
                            medium ? 4 : 3, 0.15f + fraction * 0.05f, 0.026f,
                            lance.Color, fade * 0.73f);
                    }
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.06f,
                        right, up, 4, 0.15f + age * 0.94f, 0.032f,
                        hot, impact * 0.9f);
                    break;

                case BeamFxStyle.ArcDisruptor:
                    for (int i = 0; i < (full ? 3 : medium ? 2 : 1); i++)
                    {
                        float u = 0.19f + i * 0.27f;
                        Vector3 a = lance.Start + delta * u;
                        float sign = (i & 1) == 0 ? 1f : -1f;
                        Vector3 b = a + axis * MathF.Min(0.65f, length * 0.13f)
                            + right * (0.15f * sign);
                        Vector3 c = b + axis * MathF.Min(0.4f, length * 0.07f)
                            - up * 0.18f;
                        DrawFacet(scene, a, b, up, 0.025f, 0.013f, lance.Color, fade * 0.83f);
                        DrawFacet(scene, b, c, right, 0.013f, 0.003f, hot, fade * 0.78f);
                    }
                    if (impact > 0)
                        for (int i = 0; i < (full ? 3 : 2); i++)
                        {
                            Vector3 spoke = right * MathF.Cos(i * MathF.Tau / 3f)
                                + up * MathF.Sin(i * MathF.Tau / 3f);
                            DrawFacet(scene, lance.End, lance.End + spoke * (0.34f + age),
                                axis, 0.028f, 0.002f, hot, impact * 0.84f);
                        }
                    break;

                case BeamFxStyle.SolarSpear:
                    Shards(scene, lance.Start, delta, axis, right, up,
                        lance.Color, fade, fraction, full ? 3 : 2, outward: true);
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.06f,
                        right, up, full ? 6 : 4, 0.14f + age * 0.9f, 0.040f,
                        lance.Secondary, impact * 0.93f);
                    break;

                case BeamFxStyle.CryoShatter:
                    if (medium) Shards(scene, lance.Start, delta, axis, right, up,
                        lance.Color, fade, fraction, full ? 4 : 2, outward: true);
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.05f,
                        right, up, 4, 0.17f + age * 0.56f, 0.025f,
                        lance.Secondary, impact * 0.75f);
                    break;

                case BeamFxStyle.PhantomTrace:
                    if (medium)
                    {
                        Vector3 echo = (right - up) * (0.034f + fraction * 0.09f);
                        DrawShaft(scene, lance.Start + echo, lance.End + echo,
                            right, 0.012f, lance.Color, fade * 0.29f);
                        if (full) DrawShaft(scene, lance.Start - echo, lance.End - echo,
                            right, 0.009f, lance.Color, fade * 0.18f);
                    }
                    if (impact > 0) DrawRing(scene, lance.End - axis * 0.05f,
                        right, up, 3, 0.12f + age * 0.35f, 0.012f,
                        lance.Color, impact * 0.45f);
                    break;
            }
        }

        private bool TryPrepareTrail(Scene scene)
        {
            try
            {
                _trail ??= scene.GetModelInstance("trail");
                Model model = _trail.Model;
                Material material = model.Materials[0];
                Texture texture = model.Recolors[0].Textures[0];
                if (texture.Width < 1 || texture.Height < 1) return false;
                int binding = scene.BindGetTexture(model, material.TextureId, material.PaletteId, 0);
                if (binding <= 0) return false;
                _trailTexture = binding;
                _repeatX = material.XRepeat;
                _repeatY = material.YRepeat;
                _scaleX = material.ScaleS;
                _scaleY = material.ScaleT;
                _uvX = (texture.Width - (1f / 16f)) / texture.Width;
                _uvY = (texture.Height - (1f / 16f)) / texture.Height;
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                return false;
            }
        }

        // Camera-facing textured native TrailMulti ribbons. The render
        // matrix puts muzzle-relative vertices back into world space.
        private void DrawTexturedRibbon(Scene scene, Vector3 origin, Vector3 axis,
            Vector3 camera, Vector3 right, Vector3 up, float reach, int samples,
            float width, float widthReach, Vector3 color, float alpha,
            float phase, int strand, float age, BeamFxStyle? signatureStyle = null)
        {
            if (_trailTexture <= 0 || reach <= 0.05f || alpha <= 0.004f) return;
            int count = samples * 4;
            Vector3[] data = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                float s = reach * (strand < 0 ? t * t : t);
                Vector3 centre = axis * s;
                if (signatureStyle.HasValue)
                    centre += SignatureOffset(signatureStyle.Value, t, age, strand, right, up);
                else if (strand >= 0)
                    centre += HelixDisplacement(right, up, s, age, phase, strand);
                Vector3 side = Vector3.Cross(axis, camera - origin - centre);
                if (side.LengthSquared < 0.000001f) side = right;
                else side.Normalize();
                Vector3 offset = side * TaperAt(s, width, widthReach);
                int at = i * 4;
                data[at] = new Vector3(t * _uvX, 0f, 0f);
                data[at + 1] = centre - offset;
                data[at + 2] = new Vector3(t * _uvX, _uvY, 0f);
                data[at + 3] = centre + offset;
            }
            scene.AddRenderItem(RenderItemType.TrailMulti, Math.Clamp(alpha, 0f, 1f),
                scene.GetNextPolygonId(), color, _repeatX, _repeatY,
                _scaleX, _scaleY, Matrix4.CreateTranslation(origin),
                data, _trailTexture, trailCount: count);
        }

        // The animation is wholly time-based, including the stagger and
        // oscillations: neither simulation RNG nor a draw-frame counter is read.
        private static void Shards(Scene scene, Vector3 origin, Vector3 delta,
            Vector3 axis, Vector3 right, Vector3 up, Vector3 color,
            float fade, float fraction, int count, bool outward)
        {
            float open = MathF.Min(1f, fraction * 10f);
            float signDirection = outward ? 1f : -1f;
            for (int i = 0; i < count; i++)
            {
                float u = 0.19f + i * 0.18f;
                Vector3 basePoint = origin + delta * u;
                float sign = (i & 1) == 0 ? 1f : -1f;
                Vector3 direction = (right * sign + up * (i % 3 == 0 ? 0.52f : -0.36f));
                Vector3 tip = basePoint + axis * 0.19f
                    + direction * signDirection * (0.10f + 0.28f * open);
                DrawFacet(scene, basePoint, tip, right, 0.032f, 0.005f,
                    color, fade * (0.4f + 0.06f * i));
            }
        }

        private static void DrawRing(Scene scene, Vector3 center, Vector3 right,
            Vector3 up, int sides, float radius, float thickness,
            Vector3 color, float alpha)
        {
            if (alpha <= 0.005f) return;
            for (int i = 0; i < sides; i++)
            {
                float a = MathF.Tau * i / sides;
                float b = MathF.Tau * (i + 1) / sides;
                Vector3 radialA = right * MathF.Cos(a) + up * MathF.Sin(a);
                Vector3 radialB = right * MathF.Cos(b) + up * MathF.Sin(b);
                DrawFacet(scene, center + radialA * radius, center + radialB * radius,
                    (radialA + radialB).Normalized(), thickness, thickness,
                    color, alpha);
            }
        }

        private static Vector3 Perpendicular(Vector3 axis, Vector3 view)
        {
            Vector3 side = Vector3.Cross(axis, view);
            if (side.LengthSquared < 0.000001f) side = Vector3.Cross(axis, Vector3.UnitY);
            if (side.LengthSquared < 0.000001f) side = Vector3.UnitX;
            return side.Normalized();
        }

        private static void DrawFacet(Scene scene, Vector3 a, Vector3 b, Vector3 normal,
            float widthA, float widthB, Vector3 color, float alpha)
        {
            if (alpha <= 0.005f) return;
            Vector3[] points = ArrayPool<Vector3>.Shared.Rent(8);
            points[0] = Vector3.Zero;
            points[1] = a - normal * widthA;
            points[2] = new Vector3(1, 0, 0);
            points[3] = a + normal * widthA;
            points[4] = new Vector3(0, 1, 0);
            points[5] = b - normal * widthB;
            points[6] = new Vector3(1, 1, 0);
            points[7] = b + normal * widthB;
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
                new Vector4(color, alpha), RenderItemType.TrailMulti, points, 8, noLines: true);
        }

        private static void DrawShaft(Scene scene, Vector3 start, Vector3 end,
            Vector3 right, float width, Vector3 color, float alpha)
        {
            if (alpha <= 0.005f) return;
            const int samples = 5;
            Vector3[] points = ArrayPool<Vector3>.Shared.Rent(samples * 4);
            for (int i = 0; i < samples; i++)
            {
                float u = i / (float)(samples - 1);
                Vector3 center = Vector3.Lerp(start, end, u);
                float envelope = i == 0 ? 0.12f : i == samples - 1 ? 0.45f : 1f;
                Vector3 offset = right * (width * envelope);
                int at = i * 4;
                points[at] = new Vector3(0, u, 0);
                points[at + 1] = center - offset;
                points[at + 2] = new Vector3(1, u, 0);
                points[at + 3] = center + offset;
            }
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
                new Vector4(color, alpha), RenderItemType.TrailMulti,
                points, samples * 4, noLines: true);
        }
    }
}
