using System;
using System.Collections.Generic;

namespace MphRead.Hud.Radar;

public enum RadarStyle { Classic, Enhanced }
public enum RadarOrientation { Heading, North }
public enum RadarAnchor { TopRight, TopLeft, BottomRight, BottomLeft, Custom }

public static class RadarSettings
{
    public const float MinimumScale = .65f;
    public const float MaximumScale = 1.5f;
    public const float MinimumRange = 20;
    public const float MaximumRange = 80;
    public const float DefaultRange = 40;
    public const float MinimumOpacity = .35f;
    public const float MaximumOpacity = 1;
    private static RadarProfile _default = RadarProfile.Create(RadarPreset.Competitive);
    private static readonly object _profileLock = new();
    private static readonly Dictionary<string, RadarProfile> _modeProfiles
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<RadarDeviceClass, RadarProfile> _deviceProfiles = new();

    public static RadarProfile DefaultProfile => _default;
    public static RadarProfile ForMode(string? mode)
    {
        lock (_profileLock)
            return mode != null && _modeProfiles.TryGetValue(mode, out RadarProfile? profile) ? profile : _default;
    }
    public static RadarProfile ForContext(string? mode, RadarDeviceClass device)
    {
        lock (_profileLock)
            return mode != null && _modeProfiles.TryGetValue(mode, out RadarProfile? modeProfile) ? modeProfile
                : _deviceProfiles.TryGetValue(device, out RadarProfile? deviceProfile) ? deviceProfile : _default;
    }

    public static void Apply(RadarProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _default = profile.Normalize();
    }
    public static void ApplyPreset(RadarPreset preset) => Apply(RadarProfile.Create(preset));
    public static void SetModeProfile(string mode, RadarProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("A mode is required.", nameof(mode));
        lock (_profileLock)
        {
            if (profile == null) _modeProfiles.Remove(mode.Trim());
            else _modeProfiles[mode.Trim()] = profile.Normalize();
        }
    }
    public static void ClearModeProfiles() { lock (_profileLock) _modeProfiles.Clear(); }
    public static IReadOnlyDictionary<string, RadarProfile> ModeProfiles
    { get { lock (_profileLock) return new Dictionary<string, RadarProfile>(_modeProfiles); } }
    public static void SetDeviceProfile(RadarDeviceClass device, RadarProfile? profile)
    {
        if (!Enum.IsDefined(device)) throw new ArgumentOutOfRangeException(nameof(device));
        lock (_profileLock)
        {
            if (profile == null) _deviceProfiles.Remove(device);
            else _deviceProfiles[device] = profile.Normalize();
        }
    }
    public static void ClearDeviceProfiles() { lock (_profileLock) _deviceProfiles.Clear(); }
    public static IReadOnlyDictionary<RadarDeviceClass, RadarProfile> DeviceProfiles
    { get { lock (_profileLock) return new Dictionary<RadarDeviceClass, RadarProfile>(_deviceProfiles); } }

    public static RadarStyle Style { get => _default.Style; set => Update(_default with { Style = value }); }
    public static RadarOrientation Orientation { get => _default.Orientation; set => Update(_default with { Orientation = value }); }
    public static RadarAnchor Anchor { get => _default.Anchor; set => Update(_default with { Anchor = value }); }
    public static float Scale { get => _default.Scale; set => Update(_default with { Scale = value }); }
    public static float OffsetX { get => _default.OffsetX; set => Update(_default with { OffsetX = value }); }
    public static float OffsetY { get => _default.OffsetY; set => Update(_default with { OffsetY = value }); }
    public static float Range
    {
        get => _default.Range;
        set => Update(_default with { Range = value });
    }
    public static float Opacity
    {
        get => _default.Opacity;
        set => Update(_default with { Opacity = value });
    }
    public static bool ElevationIndicators { get => _default.ElevationIndicators; set => Update(_default with { ElevationIndicators = value }); }
    public static float ElevationThreshold => _default.ElevationThreshold;

    private static void Update(RadarProfile profile) => _default = profile.Normalize() with { Preset = RadarPreset.Custom };
}
