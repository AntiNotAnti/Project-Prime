using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

[JsonConverter(typeof(JsonStringEnumConverter<MapMode>))]
public enum MapMode
{
    Battle,
    Survival,
    Capture,
    Bounty,
    Nodes
}

public sealed class MapProject
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public string StableId { get; set; } = "community.new-map";
    public MapVersion Version { get; set; } = new(1, 0, 0);
    public MapProjectMetadata Metadata { get; set; } = new();
    public List<MapMode> SupportedModes { get; set; } = [MapMode.Battle, MapMode.Survival];
    /// <summary>Authoring-level environment. Null is accepted only for legacy project migration.</summary>
    public MapEnvironment? Environment { get; set; }
    public MapDefinition Map { get; set; } = new();
    public string? PreviewImage { get; set; }
    /// <summary>
    /// Native Project Prime authoring scene. Null means the project is a
    /// legacy recipe or a read-only imported geometry source.
    /// </summary>
    public MapAuthoringScene? Authoring { get; set; }

    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonIgnore]
    public MapContentIdentity? DeclaredContentIdentity { get; set; }

    public MapIdentity Identity => new(StableId, Version);

    public static MapProject FromLegacy(MapDefinition definition, string? stableId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MapBundleReadResult? bundle = definition.BundlePath == null
            ? null : new MapBundleReader().Read(definition.BundlePath);
        var project = new MapProject
        {
            StableId = stableId ?? bundle?.Manifest.StableId ?? MapIdentity.FromLegacyName(definition.Name),
            Version = bundle?.Manifest.Version ?? new MapVersion(1, 0, 0),
            Metadata = new MapProjectMetadata
            {
                Name = bundle?.Manifest.Name ?? definition.InGameName ?? definition.Name,
                Author = bundle?.Manifest.Author ?? "Unknown",
                Description = bundle?.Manifest.Description ?? "Imported legacy Project Prime map.",
                Redistribution = bundle?.Manifest.Redistribution ?? false
            },
            SupportedModes = bundle?.Manifest.SupportedModes is { } modes
                ? [.. modes] : [MapMode.Battle, MapMode.Survival],
            Environment = MapEnvironment.From(definition),
            Map = definition,
            SourcePath = definition.SourcePath,
            DeclaredContentIdentity = bundle == null ? null
                : new MapContentIdentity(bundle.Manifest.Identity, bundle.Manifest.ContentHash)
        };
        return project;
    }
}

public sealed class MapEnvironment
{
    public float KillHeight { get; set; } = -40;
    public float FarClip { get; set; } = 350;
    public bool FogEnabled { get; set; } = true;
    public int[] FogColor { get; set; } = [8, 10, 16];
    public int FogSlope { get; set; } = 5;
    public int FogOffset { get; set; } = 65180;
    public int[] Light1Color { get; set; } = [31, 28, 24];
    public float[] Light1Vector { get; set; } = [0.3f, -1, 0.2f];
    public int[] Light2Color { get; set; } = [10, 11, 16];
    public float[] Light2Vector { get; set; } = [-0.3f, 1, -0.2f];

    public void ApplyTo(MapDefinition definition)
    {
        definition.KillHeight = KillHeight;
        definition.FarClip = FarClip;
        definition.FogEnabled = FogEnabled;
        definition.FogColor = (int[])FogColor.Clone();
        definition.FogSlope = FogSlope;
        definition.FogOffset = FogOffset;
        definition.Light1Color = (int[])Light1Color.Clone();
        definition.Light1Vector = (float[])Light1Vector.Clone();
        definition.Light2Color = (int[])Light2Color.Clone();
        definition.Light2Vector = (float[])Light2Vector.Clone();
    }

    public static MapEnvironment From(MapDefinition definition) => new()
    {
        KillHeight = definition.KillHeight,
        FarClip = definition.FarClip,
        FogEnabled = definition.FogEnabled,
        FogColor = (int[])definition.FogColor.Clone(),
        FogSlope = definition.FogSlope,
        FogOffset = definition.FogOffset,
        Light1Color = (int[])definition.Light1Color.Clone(),
        Light1Vector = (float[])definition.Light1Vector.Clone(),
        Light2Color = (int[])definition.Light2Color.Clone(),
        Light2Vector = (float[])definition.Light2Vector.Clone()
    };
}

public sealed class MapProjectMetadata
{
    public string Name { get; set; } = "New Map";
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = "";
    public bool Redistribution { get; set; }
}

public sealed class MapModeCapabilities
{
    public bool HasGeneralSpawns { get; init; }
    public bool HasTeamZeroSpawns { get; init; }
    public bool HasTeamOneSpawns { get; init; }
    public bool HasTeamZeroBase { get; init; }
    public bool HasTeamOneBase { get; init; }
    public bool HasBountyBase { get; init; }
    public int ObjectiveNodeCount { get; init; }
}
