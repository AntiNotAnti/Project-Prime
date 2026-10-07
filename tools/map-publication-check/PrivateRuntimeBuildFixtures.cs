// This records the publication boundary reached by the real private-root guard.
// Compiler and runtime publication proof remains in the existing map tools.
namespace MphRead.Mods.MapGen;

public sealed class MapBuildResult;
public sealed class MapDefinition { public string Name { get; init; } = "PRIVATE_ROOM"; }
public static class MapValidator
{
    public static void RequireRuntimeName(string name)
    {
        if (name != "PRIVATE_ROOM") throw new ArgumentException("Invalid fixture runtime room.");
    }
}
public static class MapBuildScheduler
{
    public static readonly List<string[]> Publications = new();
    public static void Publish(MapBuildResult result, MapDefinition definition,
        string archive, string entities, string nodes, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Publications.Add([archive, entities, nodes]);
    }
}
