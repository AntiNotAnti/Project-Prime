using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MphRead.Mods.MapGen;

public sealed record MapCollisionStatistics(
    int Faces,
    int DistinctPoints,
    int Planes,
    int GridX,
    int GridY,
    int GridZ,
    int GridReferences);

public sealed record MapBuildStatistics
{
    public int RenderTriangles { get; init; }
    public int RenderVertices { get; init; }
    public int Materials { get; init; }
    public int Textures { get; init; }
    public int CollisionFaces { get; init; }
    public int CollisionPoints { get; init; }
    public int CollisionPlanes { get; init; }
    public int CollisionGridX { get; init; }
    public int CollisionGridY { get; init; }
    public int CollisionGridZ { get; init; }
    public int CollisionGridReferences { get; init; }
    public int Entities { get; init; }
    public int Spawns { get; init; }
    public int Items { get; init; }
    public float[] WorldMin { get; init; } = new float[3];
    public float[] WorldMax { get; init; } = new float[3];
    public int ModelScaleFactor { get; init; }
    public float FixedPointPrecision { get; init; }
}

public sealed record MapGeneratedFile(string Path, long Size, string Sha256);

public sealed record MapStageTiming(string Stage, double ElapsedMilliseconds);

public sealed class MapBuildMetadata
{
    public int CompilerSchemaVersion { get; set; }
    public string CompilerVersion { get; set; } = "";
    public string BuildFingerprint { get; set; } = "";
    public MapContentIdentity SourceIdentity { get; set; }
        = new(new MapIdentity("map.placeholder", new MapVersion(0, 0, 0)), new string('0', 64));
    public List<MapGeneratedFile> GeneratedFiles { get; set; } = [];
    public MapBuildStatistics Statistics { get; set; } = new();
    public List<MapDiagnostic> Diagnostics { get; set; } = [];
    public List<MapStageTiming> Timings { get; set; } = [];
}

public sealed record MapPackedContent(
    byte[] Model,
    byte[] Animation,
    byte[] Collision,
    byte[] Entities,
    byte[] Nodes,
    MapBuildStatistics Statistics);

public sealed record MapBuildResult(
    bool Success,
    bool CacheHit,
    string BuildFingerprint,
    string? CachePath,
    MapContentIdentity? ContentIdentity,
    ImmutableArray<MapDiagnostic> Diagnostics,
    MapBuildStatistics? Statistics,
    ImmutableArray<MapStageTiming> Timings);

public sealed class MapBuildOptions
{
    public string CacheDirectory { get; init; }
        = System.IO.Path.Combine(AppContext.BaseDirectory, "map-cache");
    public string BaseContentIdentity { get; init; } = "unversioned";
    public bool Force { get; init; }
    public bool Verbose { get; init; }
}
