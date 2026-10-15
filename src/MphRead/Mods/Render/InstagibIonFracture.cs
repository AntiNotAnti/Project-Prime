using System;
using System.Buffers;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// World-owned, presentation-only Imperialist effect for Insta-Gib.
    /// "Ion Fracture" is deliberately not Fruity Prime's double helix: it is a
    /// luminous lance with a hot core, asymmetric crystalline shear fins and
    /// a short impact corona. Nothing here affects hit validation or packets.
    /// </summary>
    internal sealed class InstagibIonFracture
    {
        internal const float LifeSeconds = 0.82f;
        internal const int Capacity = 24;

        private readonly struct Lance
        {
            internal readonly Vector3 Start, End, Color;
            internal readonly float Born;
            internal readonly int Slot;
            internal readonly uint ShotId, LaunchFrame;

            internal Lance(Vector3 start, Vector3 end, Vector3 color, float born,
                int slot, uint shotId, uint launchFrame)
            {
                Start = start;
                End = end;
                Color = color;
                Born = born;
                Slot = slot;
                ShotId = shotId;
                LaunchFrame = launchFrame;
            }
        }

        // Saturated player-slot identification, with a common cold-white inner core.
        // These are visual identities only; they do not follow team outline settings.
        private static readonly Vector3[] Palette =
        {
            new(0.13f, 0.88f, 1.00f), // ion cyan
            new(1.00f, 0.22f, 0.73f), // plasma pink
            new(0.58f, 1.00f, 0.18f), // acid green
            new(1.00f, 0.53f, 0.16f), // ember
            new(1.00f, 0.88f, 0.28f), // gold
            new(1.00f, 0.18f, 0.22f), // scarlet
            new(0.54f, 0.42f, 1.00f), // ultraviolet
            new(0.55f, 0.86f, 1.00f)  // glacial blue
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

        internal void Add(Scene scene, Vector3 start, Vector3 end, int slot,
            uint shotId, uint launchFrame)
        {
            if (!scene.GameState.Multiplayer || !scene.GameState.InstaGib
                || !Finite(start) || !Finite(end) || (end - start).LengthSquared < 0.0025f)
                return;

            float now = scene.ElapsedTime;
            if (now + 0.001f < _lastTime) Clear(); // replay seek/new timeline
            _lastTime = now;

            // A recovered/replicated fire event can reach multiple presentation
            // paths. Never double-emit its distinctive lance.
            for (int i = 0; i < _count; i++)
            {
                Lance old = _active[i];
                if (old.Slot != slot || now - old.Born > LifeSeconds) continue;
                if (shotId != 0 && old.ShotId == shotId && old.LaunchFrame == launchFrame)
                    return;
                if (shotId == 0 && old.ShotId == 0 && MathF.Abs(now - old.Born) < 1f / 120f
                    && (old.Start - start).LengthSquared < 0.01f
                    && (old.End - end).LengthSquared < 0.01f)
                    return;
            }

            if (_count == Capacity)
            {
                Array.Copy(_active, 1, _active, 0, Capacity - 1);
                _count--;
            }
            _active[_count++] = new Lance(start, end, ColorForSlot(slot), now,
                slot, shotId, launchFrame);
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

            // Reuse the frame's existing GL transparent world submission. No
            // static scene references, model/material edits, or render-only RNG.
            bool reduce = HudProfiles.Runtime.ReduceMotion;
            bool lowTransparency = HudProfiles.Runtime.ReduceTransparency;
            Vector3 view = scene.ViewMatrix.Inverted().Row2.Xyz;
            if (view.LengthSquared < 0.000001f) view = Vector3.UnitZ;
            else view.Normalize();

            int surviving = 0;
            for (int i = 0; i < _count; i++)
            {
                Lance lance = _active[i];
                float age = now - lance.Born;
                if (age < 0 || age >= LifeSeconds) continue;
                _active[surviving++] = lance;
                DrawLance(scene, lance, view, age, reduce, lowTransparency);
            }
            _count = surviving;
        }

        private static void DrawLance(Scene scene, in Lance lance, Vector3 view,
            float age, bool reducedMotion, bool reducedTransparency)
        {
            Vector3 delta = lance.End - lance.Start;
            float length = delta.Length;
            if (length <= 0.05f) return;
            Vector3 axis = delta / length;
            Vector3 right = Perpendicular(axis, view);
            Vector3 up = Vector3.Cross(axis, right).Normalized();
            float fraction = age / LifeSeconds;
            float fade = FadeAt(fraction);
            float muzzleTaper = MathF.Min(1f, length / 2f);

            // The three-lane energy silhouette: cold white pencil core,
            // coloured sheath and atmospheric halo, tapered at the muzzle.
            Vector3 hot = Vector3.Lerp(lance.Color, Vector3.One, 0.78f);
            if (!reducedMotion && !reducedTransparency)
                DrawShaft(scene, lance.Start, lance.End, right, 0.18f * muzzleTaper,
                    lance.Color, fade * 0.12f);
            DrawShaft(scene, lance.Start, lance.End, right, 0.075f * muzzleTaper,
                lance.Color, fade * 0.62f);
            DrawShaft(scene, lance.Start, lance.End, right, 0.022f * muzzleTaper,
                hot, fade * 0.98f);

            if (reducedMotion) return; // readable beam without animated fragments

            // Three stationary-in-shot-space shear fractures, not a corkscrew.
            // As the beam ages, each crystalline fin opens once and dissipates.
            // The stagger is stable per-shot, independent of the frame cadence.
            float open = MathF.Min(1f, age * 15f);
            float disperse = fraction * 0.25f;
            for (int i = 0; i < 3; i++)
            {
                float along = 0.20f + i * 0.25f;
                if (along * length > 55f) break;
                float sign = (i & 1) == 0 ? 1f : -1f;
                Vector3 basePoint = lance.Start + delta * along;
                Vector3 offset = (right * sign + up * (i == 1 ? -0.45f : 0.28f)) * (0.15f + open * 0.28f + disperse);
                Vector3 tip = basePoint + offset + axis * (0.18f + 0.12f * i);
                DrawFacet(scene, basePoint, tip, right, 0.042f, 0.008f,
                    lance.Color, fade * (0.48f + 0.07f * i));
            }

            // Impact prism: three sharp, short-lived radial flashes emerging
            // on the shooter's side of the actual hit point (no x-ray decals).
            float impactAlpha = 1f - Math.Clamp(age / 0.22f, 0f, 1f);
            if (impactAlpha <= 0) return;
            Vector3 origin = lance.End - axis * 0.075f;
            for (int i = 0; i < 3; i++)
            {
                float a = MathF.Tau * i / 3f + (lance.Slot & 3) * 0.35f;
                Vector3 spread = right * MathF.Cos(a) + up * MathF.Sin(a);
                Vector3 end = origin + spread * (0.36f + age * 0.70f) - axis * 0.12f;
                DrawFacet(scene, origin, end, axis, 0.036f, 0.002f,
                    i == 0 ? hot : lance.Color, impactAlpha * 0.72f);
            }
        }

        private static Vector3 Perpendicular(Vector3 axis, Vector3 view)
        {
            Vector3 side = Vector3.Cross(axis, view);
            if (side.LengthSquared < 0.000001f) side = Vector3.Cross(axis, Vector3.UnitY);
            if (side.LengthSquared < 0.000001f) side = Vector3.UnitX;
            return side.Normalized();
        }

        // Two points are the minimum valid TrailMulti strip. The source renderer
        // returns pooled vertices after submitting the frame, as it does for
        // armor cosmetic trails.
        private static void DrawFacet(Scene scene, Vector3 a, Vector3 b, Vector3 normal,
            float widthA, float widthB, Vector3 color, float alpha)
        {
            if (alpha <= 0.005f) return;
            Vector3[] points = ArrayPool<Vector3>.Shared.Rent(8);
            points[0] = new Vector3(0, 0, 0);
            points[1] = a - normal * widthA;
            points[2] = new Vector3(1, 0, 0);
            points[3] = a + normal * widthA;
            points[4] = new Vector3(0, 1, 0);
            points[5] = b - normal * widthB;
            points[6] = new Vector3(1, 1, 0);
            points[7] = b + normal * widthB;
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
                new Vector4(color, alpha), RenderItemType.TrailMulti,
                points, 8, noLines: true);
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
                // Pinch right at the muzzle and end, rather than leaving a
                // large flat glowing plane over the player's view-model.
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
