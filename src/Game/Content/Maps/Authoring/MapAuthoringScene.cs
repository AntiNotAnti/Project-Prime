using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

[JsonConverter(typeof(JsonStringEnumConverter<MapPrimitiveKind>))]
public enum MapPrimitiveKind
{
    Custom,
    Box,
    Wedge,
    Cylinder,
    Stairs
}

[JsonConverter(typeof(JsonStringEnumConverter<MapEntityKind>))]
public enum MapEntityKind
{
    PlayerSpawn,
    ItemSpawn,
    JumpPad,
    DamageVolume,
    KillVolume,
    TeamSpawn,
    CaptureBase,
    BountyBase,
    NodeObjective,
    Teleporter,
    Door,
    ForceField,
    MovingPlatform,
    Trigger
}

public sealed class MapAuthoringScene
{
    public List<MapAuthoringMaterial> Materials { get; set; } = [];
    public List<ConvexBrush> Brushes { get; set; } = [];
    public List<MapEntityDefinition> Entities { get; set; } = [];
    public MapEditorSettings Editor { get; set; } = new();
}

public sealed class MapAuthoringMaterial
{
    public string Id { get; set; } = "material.default";
    public string Name { get; set; } = "Default";
    public string? SourceRoom { get; set; }
    public int? SourceMaterial { get; set; }
    public string? CustomImage { get; set; }
    public float Tiling { get; set; } = 16;
    public float[] Offset { get; set; } = [0, 0];
    public float Rotation { get; set; }
    public string Terrain { get; set; } = "Metal";
}

/// <summary>
/// Additive convex solid represented by outward-facing half spaces. Local
/// points satisfy dot(Normal, point) &lt;= Distance. The compiler derives both
/// render and collision faces from this single source.
/// </summary>
public sealed class ConvexBrush
{
    public string Id { get; set; } = "brush.new";
    public MapPrimitiveKind Primitive { get; set; } = MapPrimitiveKind.Custom;
    public MapTransform Transform { get; set; } = new();
    public List<ConvexBrushFace> Faces { get; set; } = [];
    public bool Solid { get; set; } = true;
    public bool Damaging { get; set; }
    public float Shade { get; set; } = 1;
}

public sealed class ConvexBrushFace
{
    public float[] Normal { get; set; } = [0, 1, 0];
    public float Distance { get; set; } = 0.5f;
    public string MaterialId { get; set; } = "material.default";
    public float[] TextureOffset { get; set; } = [0, 0];
    public float TextureRotation { get; set; }
    public float TextureScale { get; set; } = 1;
}

public sealed class MapTransform
{
    public float[] Position { get; set; } = [0, 0, 0];
    /// <summary>Euler rotation in degrees, ordered X/Y/Z.</summary>
    public float[] Rotation { get; set; } = [0, 0, 0];
    public float[] Scale { get; set; } = [1, 1, 1];
}

/// <summary>
/// Creator-facing typed entity. Runtime numeric IDs are assigned
/// deterministically during compilation and never appear in source.
/// </summary>
public sealed class MapEntityDefinition
{
    public string Id { get; set; } = "entity.new";
    public MapEntityKind Kind { get; set; }
    public MapTransform Transform { get; set; } = new();
    public float[] Size { get; set; } = [1, 1, 1];
    public float[]? Target { get; set; }
    public float[]? LaunchVector { get; set; }
    public float LaunchSpeed { get; set; }
    public float ControlLockTime { get; set; } = 10;
    public float CooldownTime { get; set; } = 15;
    public int ModelId { get; set; }
    public string ItemType { get; set; } = "HealthSmall";
    public bool HasBase { get; set; } = true;
    public float SpawnInterval { get; set; } = 300;
    public int Team { get; set; } = -1;
    public int DamagePerTick { get; set; }
    public string? TargetObjectId { get; set; }
}

public sealed class MapEditorSettings
{
    public float PositionSnap { get; set; } = 0.25f;
    public float RotationSnap { get; set; } = 15;
    public float ScaleSnap { get; set; } = 0.25f;
    public float[] CameraPosition { get; set; } = [12, 10, 12];
    public float[] CameraTarget { get; set; } = [0, 0, 0];
    public bool ShowCollision { get; set; } = true;
    public bool ShowEntities { get; set; } = true;
    public bool ShowWorldBounds { get; set; }
}

public static class MapObjectId
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        if (!char.IsAsciiLetterOrDigit(value[0])) return false;
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) return false;
        }
        return true;
    }
}
