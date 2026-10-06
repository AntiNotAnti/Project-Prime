using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MphRead.Mods.MapGen;

/// <summary>Authoring provenance only. Runtime geometry and entities are always resolved in the owning map.</summary>
public sealed class MapPrefabSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Revision { get; set; } = "";
}

public sealed class MapPrefabInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public string SourcePath { get; set; } = "";
    public string SourceRevision { get; set; } = "";
    public MapTransform Transform { get; set; } = new();
    public List<MapPrefabMember> Members { get; set; } = new();
    public List<MapPrefabMaterialBinding> Materials { get; set; } = new();
}

public sealed class MapPrefabMember
{
    public Guid SourceObjectId { get; set; }
    public Guid ObjectId { get; set; }
    public string Kind { get; set; } = "";
    public string Baseline { get; set; } = "";
    /// <summary>JSON merge patch of locally edited fields. Null values remove optional fields.</summary>
    public string? Overrides { get; set; }
    public bool Deleted { get; set; }
}

public sealed class MapPrefabMaterialBinding
{
    public Guid SourceMaterialId { get; set; }
    public Guid MaterialId { get; set; }
    public string Baseline { get; set; } = "";
    public string? Overrides { get; set; }
}

internal static class MapPrefabMetadata
{
    public static void RemapMaterialSlots(MapDefinition definition, IReadOnlyDictionary<int, int> slots)
    {
        string? Remap(string? json)
        {
            if (json == null) return null;
            var value = JsonNode.Parse(json)!.AsObject();
            if (value["material"] is JsonValue material && material.TryGetValue<int>(out int slot)) value["material"] = slots.GetValueOrDefault(slot, slot);
            if (value["faceMaterials"] is JsonArray faces)
                for (int i = 0; i < faces.Count; i++) if (faces[i] is JsonValue face && face.TryGetValue<int>(out int index)) faces[i] = slots.GetValueOrDefault(index, index);
            return value.ToJsonString();
        }
        foreach (var instance in definition.PrefabInstances)
            foreach (var member in instance.Members) { member.Baseline = Remap(member.Baseline)!; member.Overrides = Remap(member.Overrides); }
    }
}
