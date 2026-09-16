using System;
using System.Collections.Generic;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Combat;

/// <summary>
/// Builds one smooth, full-resolution hit marker in the HUD's 256x192 virtual
/// coordinate space. The renderer keeps the floating-point positions through
/// final viewport scaling, avoiding the old bitmap-font pixel quantization.
/// </summary>
internal static class HitMarkerVisual
{
    internal static void Build(List<HudGeometryVertex> geometry,
        Vector2 normalizedPosition, HitMarkerKind kind, float age, float duration,
        float pulseAge, in CombatFeedbackState state, float reticleScale,
        bool reduceMotion)
    {
        geometry.Clear();
        if (kind == HitMarkerKind.None || duration == 0) return;

        float progress = Math.Clamp(age / (float)duration, 0, 1);
        float fadeStart = kind == HitMarkerKind.Predicted ? .35f : .62f;
        float fade = 1 - SmoothStep(fadeStart, 1, progress);
        float opacity = CombatFeedbackSettings.MarkerOpacity * fade;
        if (kind == HitMarkerKind.Predicted) opacity *= .58f;
        if (opacity <= .001f) return;

        float animation = reduceMotion ? 0 : CombatFeedbackSettings.MarkerAnimation;
        float entry = 1 - Math.Clamp(age / 4f, 0, 1);
        float pulse = 1 - Math.Clamp(pulseAge / 4f, 0, 1);
        float damage = Math.Clamp(state.MarkerDamage / 80f, 0, 1);
        if ((state.MarkerFlags & (CombatEventFlags.Charged | CombatEventFlags.Affinity)) != 0)
            damage = Math.Max(damage, .65f);
        if (kind != HitMarkerKind.Predicted && state.MarkerHealth == 0)
            damage = 1;
        else if (state.MarkerHealth is > 0 and <= 25)
            damage = Math.Max(damage, .75f);
        if (state.MarkerWeapon is 2 or 3 or 4 or 6 or 8)
            damage = Math.Max(damage, .55f);
        float burst = Math.Clamp((state.MarkerBurst - 1) / 5f, 0, 1);
        float scale = Math.Clamp(reticleScale, .4f, 2.5f)
            * CombatFeedbackSettings.MarkerScale;
        scale *= 1 + animation * (entry * .12f + pulse * (.08f + .08f * burst));

        float gap = (kind == HitMarkerKind.Kill ? 4.5f : 5.5f) * scale;
        gap += animation * (entry * 5f + pulse * (1.5f + burst)) * scale;
        float length = (kind == HitMarkerKind.Kill ? 8f
            : kind == HitMarkerKind.Headshot ? 7.25f : 6.5f) * scale;
        float thickness = (kind == HitMarkerKind.Predicted ? 1.15f : 1.7f)
            * scale * (1 + damage * .22f);

        Vector2 center = new(
            Math.Clamp(normalizedPosition.X, 0, 1) * 256f,
            Math.Clamp(normalizedPosition.Y, 0, 1) * 192f);
        Vector4 color = Color(kind, CombatFeedbackSettings.Palette, opacity);
        AddArms(geometry, center, gap, length, thickness, color);

        // Headshots receive a brief second inward ring; kills close around a
        // center diamond. Both remain geometric rather than font-dependent.
        if (kind == HitMarkerKind.Headshot && animation > 0 && age is >= 4 and <= 10)
        {
            float secondary = 1 - Math.Abs(age - 7) / 3f;
            Vector4 echo = new(color.X, color.Y, color.Z,
                color.W * secondary * .55f);
            AddArms(geometry, center, Math.Max(1.5f * scale, gap - 3.5f * scale),
                length * .58f, thickness * .7f, echo);
        }
        else if (kind == HitMarkerKind.Kill)
        {
            float diamondScale = (2.2f + damage * .7f) * scale;
            AddDiamond(geometry, center, diamondScale, color);
        }
    }

    internal static Vector4 Color(HitMarkerKind kind, HitMarkerPalette palette,
        float opacity)
    {
        Vector3 rgb = palette switch
        {
            HitMarkerPalette.HighContrast => kind switch
            {
                HitMarkerKind.Headshot => new(1f, 1f, 1f),
                HitMarkerKind.Kill => new(1f, .48f, .05f),
                HitMarkerKind.Predicted => new(.68f, .75f, .78f),
                _ => new(0f, .95f, 1f)
            },
            HitMarkerPalette.Colorblind => kind switch
            {
                HitMarkerKind.Headshot => new(.94f, .89f, .26f),
                HitMarkerKind.Kill => new(.84f, .37f, 0f),
                HitMarkerKind.Predicted => new(.62f, .62f, .62f),
                _ => new(.34f, .71f, .91f)
            },
            HitMarkerPalette.Monochrome => kind == HitMarkerKind.Predicted
                ? new(.65f, .65f, .65f) : Vector3.One,
            _ => kind switch
            {
                HitMarkerKind.Headshot => new(1f, 1f, .25f),
                HitMarkerKind.Kill => new(1f, .25f, .25f),
                HitMarkerKind.Predicted => new(.82f, .82f, .82f),
                _ => Vector3.One
            }
        };
        return new Vector4(rgb, Math.Clamp(opacity, 0, 1));
    }

    private static void AddArms(List<HudGeometryVertex> geometry, Vector2 center,
        float gap, float length, float thickness, Vector4 color)
    {
        const float diagonal = .70710678f;
        AddBar(geometry, center, new Vector2(diagonal, diagonal), gap, length,
            thickness, color);
        AddBar(geometry, center, new Vector2(-diagonal, diagonal), gap, length,
            thickness, color);
        AddBar(geometry, center, new Vector2(diagonal, -diagonal), gap, length,
            thickness, color);
        AddBar(geometry, center, new Vector2(-diagonal, -diagonal), gap, length,
            thickness, color);
    }

    private static void AddBar(List<HudGeometryVertex> geometry, Vector2 center,
        Vector2 direction, float gap, float length, float thickness, Vector4 color)
    {
        Vector2 start = center + direction * gap;
        Vector2 end = start + direction * length;
        Vector2 normal = new(-direction.Y * thickness / 2,
            direction.X * thickness / 2);
        AddQuad(geometry, start + normal, start - normal, end + normal,
            end - normal, color);
    }

    private static void AddDiamond(List<HudGeometryVertex> geometry,
        Vector2 center, float radius, Vector4 color)
    {
        AddQuad(geometry, center + new Vector2(0, -radius),
            center + new Vector2(-radius, 0), center + new Vector2(radius, 0),
            center + new Vector2(0, radius), color);
    }

    private static void AddQuad(List<HudGeometryVertex> geometry, Vector2 a,
        Vector2 b, Vector2 c, Vector2 d, Vector4 color)
    {
        if (geometry.Count > 0)
        {
            // Degenerate vertices restart the triangle strip without another
            // draw call or a per-frame allocation.
            geometry.Add(geometry[^1]);
            geometry.Add(new HudGeometryVertex(a, color));
        }
        geometry.Add(new HudGeometryVertex(a, color));
        geometry.Add(new HudGeometryVertex(b, color));
        geometry.Add(new HudGeometryVertex(c, color));
        geometry.Add(new HudGeometryVertex(d, color));
    }

    private static float SmoothStep(float from, float to, float value)
    {
        if (to <= from) return value >= to ? 1 : 0;
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
