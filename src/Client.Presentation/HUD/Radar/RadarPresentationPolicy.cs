using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public static class RadarPresentationPolicy
{
    public static bool IsVisible(RadarProfile profile, in RadarContact contact)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return contact.Type switch
        {
            RadarContactType.Enemy or RadarContactType.PrimeHunter => profile.ShowEnemies,
            RadarContactType.Teammate => profile.ShowTeammates,
            RadarContactType.Objective => profile.ShowObjectives && contact.Objective switch
            {
                RadarObjective.Flag => profile.ShowFlags,
                RadarObjective.Base => profile.ShowBases,
                RadarObjective.Node => profile.ShowNodes,
                RadarObjective.Defender => profile.ShowDefenders,
                _ => true
            },
            _ => false
        };
    }

    public static int Priority(in RadarContact contact, bool objectivePriority = true)
    {
        int type = contact.Type switch
        {
            RadarContactType.Objective when objectivePriority => 400,
            RadarContactType.PrimeHunter => 350,
            RadarContactType.Teammate => 250,
            RadarContactType.Enemy => 200,
            _ => 100
        };
        return type + (int)MathF.Round(Math.Clamp(contact.Visibility, 0, 1) * 20)
            - (int)Math.Min(contact.AgeTicks, 100u);
    }

    public static float Alpha(RadarProfile profile, in RadarContact contact, ulong presentationTick = 0)
    {
        if (!IsVisible(profile, contact)) return 0;
        float ageSeconds = contact.AgeTicks / 60f;
        float persistence = profile.ContactPersistenceSeconds;
        float ageAlpha = ageSeconds <= 0 ? 1
            : persistence <= 0 ? 0 : Math.Clamp(1 - ageSeconds / persistence, 0, 1);
        float pulse = profile.ContactPulse && contact.Type is RadarContactType.Objective or RadarContactType.PrimeHunter
            ? .94f + .06f * MathF.Sin(presentationTick * .16f) : 1;
        return Math.Clamp(contact.Visibility, 0, 1) * profile.MarkerOpacity * ageAlpha * pulse;
    }

    public static Vector4 Color(RadarProfile profile, in RadarContact contact, RadarElevation elevation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RadarColor color = contact.Type switch
        {
            RadarContactType.Enemy => profile.Colors.Enemy,
            RadarContactType.Teammate => profile.Colors.Teammate,
            RadarContactType.PrimeHunter => profile.Colors.PrimeHunter,
            _ => profile.Colors.Objective
        };
        return color.Vector;
    }

    /// <summary>Elevation is a secondary cue and must not replace contact identity.</summary>
    public static Vector4 ElevationColor(RadarProfile profile, RadarElevation elevation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return (elevation == RadarElevation.Below
            ? profile.Colors.Below : profile.Colors.Above).Vector;
    }

    public static RadarMarkerShape MarkerShape(in RadarContact contact)
        => contact.Type switch
        {
            RadarContactType.Teammate => RadarMarkerShape.Square,
            RadarContactType.Objective => RadarMarkerShape.Triangle,
            RadarContactType.PrimeHunter => RadarMarkerShape.DoubleDiamond,
            _ => RadarMarkerShape.Diamond
        };

    /// <summary>Keeps the complete off-screen indicator inside the radar frame.</summary>
    public static float EdgeMarkerRadius(float radarRadius, float markerScale,
        float edgeScale)
    {
        if (!float.IsFinite(radarRadius) || radarRadius <= 0) return 0;
        float marker = float.IsFinite(markerScale) ? Math.Max(.5f, markerScale) : 1;
        float edge = float.IsFinite(edgeScale) ? Math.Max(.5f, edgeScale) : 1;
        return Math.Max(0, radarRadius - 5.5f * marker * edge);
    }

    public static IEnumerable<int> VisibleFloors(RadarFloorMode mode, int current, int count)
    {
        if ((uint)current >= (uint)count) yield break;
        if (mode == RadarFloorMode.All)
        {
            for (int i = 0; i < count; i++) if (i != current) yield return i;
        }
        else if (mode == RadarFloorMode.Adjacent)
        {
            if (current > 0) yield return current - 1;
            if (current + 1 < count) yield return current + 1;
        }
        yield return current;
    }
}

/// <summary>Render-time zoom interpolation. This never changes simulation or contact admission.</summary>
public sealed class RadarZoomController
{
    private float _range;
    private TimeSpan _lastSample;
    private bool _initialized;

    public float Update(RadarProfile profile, Vector3 origin, ReadOnlySpan<RadarContact> contacts,
        TimeSpan sampleTime)
    {
        float target = Target(profile, origin, contacts);
        if (!_initialized || sampleTime < _lastSample)
        {
            _range = target;
            _initialized = true;
        }
        else
        {
            float elapsed = (float)(sampleTime - _lastSample).TotalSeconds;
            float amount = profile.ZoomSmoothingSeconds <= 0 ? 1
                : 1 - MathF.Exp(-Math.Clamp(elapsed, 0, .25f) / profile.ZoomSmoothingSeconds);
            _range += (target - _range) * amount;
        }
        _lastSample = sampleTime;
        return _range;
    }

    public void Reset() => _initialized = false;

    public static float Target(RadarProfile profile, Vector3 origin, ReadOnlySpan<RadarContact> contacts)
    {
        if (profile.ZoomMode == RadarZoomMode.Fixed) return profile.Range;
        float farthest = profile.AutomaticMinimumRange;
        bool combat = false;
        for (int i = 0; i < contacts.Length; i++)
        {
            RadarContact contact = contacts[i];
            if (!RadarPresentationPolicy.IsVisible(profile, contact)) continue;
            Vector3 delta = contact.Position - origin;
            farthest = MathF.Max(farthest, new Vector2(delta.X, delta.Z).Length * 1.15f);
            combat |= contact.Type is RadarContactType.Enemy or RadarContactType.PrimeHunter
                && contact.AgeTicks <= 15;
        }
        if (profile.ZoomMode == RadarZoomMode.CombatSensitive && combat)
            return profile.AutomaticMinimumRange;
        float minimum = MathF.Min(profile.AutomaticMinimumRange, profile.AutomaticMaximumRange);
        float maximum = MathF.Max(profile.AutomaticMinimumRange, profile.AutomaticMaximumRange);
        return Math.Clamp(farthest, minimum, maximum);
    }
}

public static class RadarLayoutEditor
{
    public const float SnapStep = 4;

    public static RadarProfile Drag(RadarProfile profile, Vector2 delta, bool snap = true)
    {
        RadarLayout layout = RadarLayoutCalculator.Calculate(profile.Anchor, profile.Scale,
            profile.OffsetX, profile.OffsetY, 1);
        float x = layout.CenterX - 128 + (float.IsFinite(delta.X) ? delta.X : 0);
        float y = layout.CenterY - 96 + (float.IsFinite(delta.Y) ? delta.Y : 0);
        if (snap) { x = Snap(x); y = Snap(y); }
        RadarProfile normalized = (profile with
        {
            Name = "Custom", Preset = RadarPreset.Custom,
            Anchor = RadarAnchor.Custom, OffsetX = x, OffsetY = y
        }).Normalize();
        float size = RadarLayoutCalculator.BaseDiameter * normalized.Scale;
        return normalized with
        {
            OffsetX = Math.Clamp(normalized.OffsetX, -128 + size / 2, 128 - size / 2),
            OffsetY = Math.Clamp(normalized.OffsetY, -96 + size / 2, 96 - size / 2)
        };
    }

    public static RadarProfile Resize(RadarProfile profile, float scaleDelta, bool snap = true)
    {
        float scale = profile.Scale + (float.IsFinite(scaleDelta) ? scaleDelta : 0);
        if (snap) scale = MathF.Round(scale / .05f) * .05f;
        return (profile with
        {
            Name = "Custom", Preset = RadarPreset.Custom, Scale = scale
        }).Normalize();
    }

    public static RadarProfile Reset(RadarProfile profile)
        => (profile with
        {
            Name = "Custom", Preset = RadarPreset.Custom,
            Anchor = RadarAnchor.TopRight, Scale = 1, OffsetX = 0, OffsetY = 0
        }).Normalize();

    private static float Snap(float value) => MathF.Round(value / SnapStep) * SnapStep;
}

public static class RadarPreview
{
    /// <summary>Synthetic, non-world contacts used by the settings preview.</summary>
    public static RadarFrame CreateFrame()
    {
        var frame = new RadarFrame();
        frame.Begin(Vector3.Zero, -Vector3.UnitZ, 0);
        frame.AddApproved(new(RadarContactType.Enemy, new Vector3(12, 3, -8), 1, RadarObjective.None, 1));
        frame.AddApproved(new(RadarContactType.Teammate, new Vector3(-10, -3, 7), 0, RadarObjective.None, 1));
        frame.AddApproved(new(RadarContactType.Objective, new Vector3(0, 0, -18), -1, RadarObjective.Flag, 1));
        frame.AddApproved(new(RadarContactType.PrimeHunter, new Vector3(30, 0, 5), 2, RadarObjective.None, .9f));
        return frame;
    }
}
