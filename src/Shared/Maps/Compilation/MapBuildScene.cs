using System;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Shared compiler IR. Native projects and imported BSPs converge here before
/// the established Prime binary packers run.
/// </summary>
public sealed class MapBuildScene : BuiltMap
{
    public MapBuildScene(MapDefinition definition) : base(definition) { }

    public static MapBuildScene FromBuiltMap(BuiltMap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var scene = new MapBuildScene(source.Definition);
        scene.Faces.AddRange(source.Faces);
        scene.Solid.AddRange(source.Solid);
        scene.Entities.AddRange(source.Entities);
        return scene;
    }
}

public interface IMapImporter
{
    MapBuildScene Import(MapProject project, bool verbose);
}

public sealed class LegacyMapImporter : IMapImporter
{
    public MapBuildScene Import(MapProject project, bool verbose)
        => MapBuildScene.FromBuiltMap(project.Map.Import == null
            ? MapBuilder.Build(project.Map)
            : Q3Import.Build(project.Map, verbose));
}
