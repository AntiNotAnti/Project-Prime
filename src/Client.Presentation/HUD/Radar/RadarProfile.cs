using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public enum RadarPreset { ClassicMph, Competitive, Minimal, Accessibility, ObjectiveFocus, Broadcast, Custom }
public enum RadarColorPreset { Classic, HighContrast, Deuteranopia, Protanopia, Tritanopia, Monochrome, Custom }
public enum RadarFloorMode { Current, Adjacent, All }
public enum RadarZoomMode { Fixed, Automatic, CombatSensitive }
public enum RadarDeviceClass { Desktop, Handheld, Television }

public readonly record struct RadarColor(byte R, byte G, byte B, byte A = 255)
{
    [JsonIgnore]
    public Vector4 Vector => new(R / 255f, G / 255f, B / 255f, A / 255f);
}

public sealed record RadarColors
{
    public RadarColor Background { get; init; } = new(4, 9, 14, 158);
    public RadarColor Border { get; init; } = new(107, 184, 209, 230);
    public RadarColor Ring { get; init; } = new(64, 122, 148, 140);
    public RadarColor Floor { get; init; } = new(92, 151, 185, 230);
    public RadarColor MapOutline { get; init; } = new(150, 216, 240);
    public RadarColor Player { get; init; } = new(224, 255, 255);
    public RadarColor Enemy { get; init; } = new(255, 64, 56);
    public RadarColor Teammate { get; init; } = new(64, 204, 255);
    public RadarColor Objective { get; init; } = new(255, 199, 51);
    public RadarColor PrimeHunter { get; init; } = new(255, 140, 26);
    public RadarColor Above { get; init; } = new(255, 255, 255);
    public RadarColor Below { get; init; } = new(145, 160, 175);

    public static RadarColors For(RadarColorPreset preset) => preset switch
    {
        RadarColorPreset.HighContrast => new()
        {
            Background = new(0, 0, 0, 220), Border = new(255, 255, 255), Ring = new(220, 220, 220, 190),
            Floor = new(185, 185, 185, 230), MapOutline = new(255, 255, 255),
            Player = new(255, 255, 255), Enemy = new(255, 64, 64), Teammate = new(64, 224, 255),
            Objective = new(255, 230, 0), PrimeHunter = new(255, 128, 0)
        },
        RadarColorPreset.Deuteranopia => new()
        {
            Enemy = new(222, 96, 32), Teammate = new(46, 170, 255), Objective = new(255, 221, 87),
            Floor = new(74, 119, 148, 230), MapOutline = new(143, 207, 237),
            PrimeHunter = new(187, 103, 255), Above = new(255, 255, 255), Below = new(100, 160, 230)
        },
        RadarColorPreset.Protanopia => new()
        {
            Enemy = new(225, 139, 34), Teammate = new(36, 169, 255), Objective = new(255, 232, 98),
            Floor = new(73, 120, 153, 230), MapOutline = new(144, 211, 242),
            PrimeHunter = new(177, 111, 255), Above = new(255, 255, 255), Below = new(92, 157, 225)
        },
        RadarColorPreset.Tritanopia => new()
        {
            Enemy = new(255, 72, 98), Teammate = new(60, 220, 153), Objective = new(246, 164, 255),
            Floor = new(71, 128, 112, 230), MapOutline = new(138, 224, 193),
            PrimeHunter = new(255, 120, 164), Above = new(255, 255, 255), Below = new(105, 190, 145)
        },
        RadarColorPreset.Monochrome => new()
        {
            Border = new(230, 230, 230), Ring = new(180, 180, 180, 150), Player = new(255, 255, 255),
            Floor = new(145, 145, 145, 230), MapOutline = new(225, 225, 225),
            Enemy = new(235, 235, 235), Teammate = new(185, 185, 185), Objective = new(255, 255, 255),
            PrimeHunter = new(210, 210, 210), Above = new(255, 255, 255), Below = new(125, 125, 125)
        },
        _ => new()
    };
}

/// <summary>
/// Complete presentation policy for contacts that have already passed the authoritative reveal policy.
/// Filters in this type can only hide prepared contacts; they never discover contacts.
/// </summary>
public sealed record RadarProfile
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string Name { get; init; } = "Competitive";
    public RadarPreset Preset { get; init; } = RadarPreset.Competitive;
    public RadarStyle Style { get; init; } = RadarStyle.Enhanced;
    public RadarOrientation Orientation { get; init; } = RadarOrientation.Heading;
    public RadarAnchor Anchor { get; init; } = RadarAnchor.TopRight;
    public float Scale { get; init; } = 1;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float Opacity { get; init; } = 1;

    public RadarColorPreset ColorPreset { get; init; } = RadarColorPreset.Classic;
    public RadarColors Colors { get; init; } = RadarColors.For(RadarColorPreset.Classic);
    public float MarkerScale { get; init; } = 1;
    public float MarkerOpacity { get; init; } = 1;
    public bool MarkerOutline { get; init; } = true;
    public bool EdgeArrows { get; init; } = true;
    public bool Labels { get; init; } = true;
    public bool ObjectiveEmphasis { get; init; } = true;

    public bool ShowEnemies { get; init; } = true;
    public bool ShowTeammates { get; init; } = true;
    public bool ShowObjectives { get; init; } = true;
    public bool ShowFlags { get; init; } = true;
    public bool ShowBases { get; init; } = true;
    public bool ShowNodes { get; init; } = true;
    public bool ShowDefenders { get; init; } = true;

    public RadarFloorMode FloorMode { get; init; } = RadarFloorMode.Adjacent;
    public float AdjacentFloorOpacity { get; init; } = .24f;
    public bool ElevationIndicators { get; init; } = true;
    public float ElevationThreshold { get; init; } = 2;

    public RadarZoomMode ZoomMode { get; init; } = RadarZoomMode.Fixed;
    public float Range { get; init; } = 40;
    public float AutomaticMinimumRange { get; init; } = 24;
    public float AutomaticMaximumRange { get; init; } = 72;
    public float ZoomSmoothingSeconds { get; init; } = .2f;

    public bool MapFill { get; init; } = true;
    public bool MapOutlines { get; init; } = true;
    public float FloorBrightness { get; init; } = .9f;
    public float BackgroundDim { get; init; } = 1;
    public float BackgroundBlur { get; init; }
    public bool Grid { get; init; }
    public bool DistanceRings { get; init; } = true;
    public bool Compass { get; init; } = true;

    public float ContactPersistenceSeconds { get; init; } = .5f;
    public bool ContactPulse { get; init; } = true;
    public float ClampedEdgeScale { get; init; } = 1;
    public bool PrioritizeObjectives { get; init; } = true;

    public static RadarProfile Create(RadarPreset preset) => preset switch
    {
        RadarPreset.ClassicMph => new() { Name = "Classic MPH", Preset = preset, Style = RadarStyle.Classic },
        RadarPreset.Minimal => new()
        {
            Name = "Minimal", Preset = preset, MapFill = false, MapOutlines = false, Labels = false,
            MarkerOutline = false, Compass = false, DistanceRings = false, BackgroundDim = .65f
        },
        RadarPreset.Accessibility => new()
        {
            Name = "Accessibility", Preset = preset, ColorPreset = RadarColorPreset.HighContrast,
            Colors = RadarColors.For(RadarColorPreset.HighContrast), MarkerScale = 1.35f,
            MarkerOutline = true, Labels = true, ElevationThreshold = 1.5f, FloorBrightness = 1
        },
        RadarPreset.ObjectiveFocus => new()
        {
            Name = "Objective Focus", Preset = preset, ObjectiveEmphasis = true, MarkerScale = 1.1f,
            ShowEnemies = false, ShowTeammates = true, ShowObjectives = true, ZoomMode = RadarZoomMode.Automatic
        },
        RadarPreset.Broadcast => new()
        {
            Name = "Broadcast", Preset = preset, Scale = 1.35f, Labels = true, MarkerScale = 1.2f,
            FloorMode = RadarFloorMode.All, ZoomMode = RadarZoomMode.Automatic, Grid = true, Compass = true
        },
        _ => new() { Name = preset == RadarPreset.Custom ? "Custom" : "Competitive", Preset = preset }
    };

    public RadarProfile Normalize()
    {
        RadarColorPreset colorPreset = Enum.IsDefined(ColorPreset) ? ColorPreset : RadarColorPreset.Classic;
        RadarColors colors = colorPreset == RadarColorPreset.Custom
            ? Colors ?? RadarColors.For(RadarColorPreset.Classic) : RadarColors.For(colorPreset);
        float automaticMinimum = FiniteClamp(AutomaticMinimumRange, 20, 80, 24);
        float automaticMaximum = FiniteClamp(AutomaticMaximumRange, 20, 80, 72);
        if (automaticMinimum > automaticMaximum)
            (automaticMinimum, automaticMaximum) = (automaticMaximum, automaticMinimum);
        string name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();
        return this with
        {
            Version = CurrentVersion,
            Name = name[..Math.Min(name.Length, 48)],
            Preset = Enum.IsDefined(Preset) ? Preset : RadarPreset.Custom,
            Style = Enum.IsDefined(Style) ? Style : RadarStyle.Enhanced,
            Orientation = Enum.IsDefined(Orientation) ? Orientation : RadarOrientation.Heading,
            Anchor = Enum.IsDefined(Anchor) ? Anchor : RadarAnchor.TopRight,
            Scale = FiniteClamp(Scale, .65f, 1.5f, 1), OffsetX = FiniteClamp(OffsetX, -256, 256, 0),
            OffsetY = FiniteClamp(OffsetY, -192, 192, 0), Opacity = FiniteClamp(Opacity, .35f, 1, 1),
            ColorPreset = colorPreset, Colors = colors,
            MarkerScale = FiniteClamp(MarkerScale, .5f, 2, 1), MarkerOpacity = FiniteClamp(MarkerOpacity, .1f, 1, 1),
            FloorMode = Enum.IsDefined(FloorMode) ? FloorMode : RadarFloorMode.Adjacent,
            AdjacentFloorOpacity = FiniteClamp(AdjacentFloorOpacity, 0, 1, .24f),
            ElevationThreshold = FiniteClamp(ElevationThreshold, .25f, 20, 2),
            ZoomMode = Enum.IsDefined(ZoomMode) ? ZoomMode : RadarZoomMode.Fixed,
            Range = FiniteClamp(Range, 20, 80, 40),
            AutomaticMinimumRange = automaticMinimum,
            AutomaticMaximumRange = automaticMaximum,
            ZoomSmoothingSeconds = FiniteClamp(ZoomSmoothingSeconds, 0, 2, .2f),
            FloorBrightness = FiniteClamp(FloorBrightness, 0, 1.5f, .9f),
            BackgroundDim = FiniteClamp(BackgroundDim, 0, 1, 1), BackgroundBlur = FiniteClamp(BackgroundBlur, 0, 1, 0),
            ContactPersistenceSeconds = FiniteClamp(ContactPersistenceSeconds, 0, 5, .5f),
            ClampedEdgeScale = FiniteClamp(ClampedEdgeScale, .5f, 2, 1)
        };
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

public static class RadarProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Export(RadarProfile profile)
        => JsonSerializer.Serialize((profile ?? throw new ArgumentNullException(nameof(profile))).Normalize(), Options);

    public static RadarProfile Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("A radar profile is required.", nameof(json));
        RadarProfile? profile = JsonSerializer.Deserialize<RadarProfile>(json, Options);
        if (profile == null || profile.Version > RadarProfile.CurrentVersion)
            throw new FormatException("The radar profile version is not supported.");
        return profile.Normalize();
    }

    public static string ExportModes(IReadOnlyDictionary<string, RadarProfile> profiles)
        => JsonSerializer.Serialize(profiles, Options);

    public static IReadOnlyDictionary<string, RadarProfile> ImportModes(string json)
    {
        Dictionary<string, RadarProfile>? values = JsonSerializer.Deserialize<Dictionary<string, RadarProfile>>(json, Options);
        if (values == null) throw new FormatException("The radar mode profile document is empty.");
        var result = new Dictionary<string, RadarProfile>(StringComparer.OrdinalIgnoreCase);
        foreach ((string mode, RadarProfile profile) in values)
            if (!string.IsNullOrWhiteSpace(mode) && profile != null) result[mode.Trim()] = profile.Normalize();
        return result;
    }

    public static string ExportBundle(RadarProfile profile,
        IReadOnlyDictionary<string, RadarProfile> modes,
        IReadOnlyDictionary<RadarDeviceClass, RadarProfile> devices)
    {
        var namedDevices = new Dictionary<string, RadarProfile>(StringComparer.OrdinalIgnoreCase);
        foreach ((RadarDeviceClass device, RadarProfile value) in devices)
            namedDevices[device.ToString()] = value.Normalize();
        return JsonSerializer.Serialize(new RadarProfileBundle
        {
            Version = RadarProfile.CurrentVersion,
            Profile = profile.Normalize(),
            Modes = new Dictionary<string, RadarProfile>(modes, StringComparer.OrdinalIgnoreCase),
            Devices = namedDevices
        }, Options);
    }

    public static RadarProfileBundle ImportBundle(string json)
    {
        RadarProfileBundle? bundle = JsonSerializer.Deserialize<RadarProfileBundle>(json, Options);
        if (bundle == null || bundle.Profile == null || bundle.Version > RadarProfile.CurrentVersion)
            throw new FormatException("The radar profile bundle version is not supported.");
        var modes = new Dictionary<string, RadarProfile>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, RadarProfile profile) in bundle.Modes
            ?? new Dictionary<string, RadarProfile>())
            if (!string.IsNullOrWhiteSpace(name)) modes[name.Trim()] = profile.Normalize();
        var devices = new Dictionary<string, RadarProfile>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, RadarProfile profile) in bundle.Devices
            ?? new Dictionary<string, RadarProfile>())
            if (Enum.TryParse(name, true, out RadarDeviceClass device) && Enum.IsDefined(device))
                devices[device.ToString()] = profile.Normalize();
        return bundle with { Profile = bundle.Profile.Normalize(), Modes = modes, Devices = devices };
    }
}

public sealed record RadarProfileBundle
{
    public int Version { get; init; } = RadarProfile.CurrentVersion;
    public RadarProfile Profile { get; init; } = RadarProfile.Create(RadarPreset.Competitive);
    public IReadOnlyDictionary<string, RadarProfile> Modes { get; init; }
        = new Dictionary<string, RadarProfile>();
    public IReadOnlyDictionary<string, RadarProfile> Devices { get; init; }
        = new Dictionary<string, RadarProfile>();
}
