using System;
using System.Collections.Generic;

namespace MphRead.Mods.MapGen;

/// <summary>Authoring provenance only; distributed packages contain normalized geometry.</summary>
public sealed class MapModelSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public List<MapSourceDependency> Dependencies { get; set; } = new();
    public string NormalizedHash { get; set; } = "";
    public ModelImportSettings Settings { get; set; } = new();
    public List<MapModelObject> Objects { get; set; } = new();
    public Dictionary<string, Guid> MaterialMappings { get; set; } = new();
    public Dictionary<string, string> MaterialBaselines { get; set; } = new();
}

public sealed class MapModelObject
{
    public string Key { get; set; } = "";
    public Guid Id { get; set; }
    public int BaseMaterial { get; set; }
    public Dictionary<string, int> FaceMaterials { get; set; } = new();
    public Dictionary<string, string> FaceUvs { get; set; } = new();
}

public sealed record MapSourceDependency(string Path, string Hash);
