using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesLocalMapFixtureTests
{
    public static void Run()
    {
        var source = new EchoesCollisionData(
            new[] { 0f, 0, 0, 5f, 0, 0, 0f, 5, 0 },
            new[] { 0, 1, 2 },
            new[] { 1UL << 4 });
        EchoesScriptObject Spawn(uint id, float x) => new("SPWN", id, 0, true,
            "Spawn", new[] { x, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f }, uint.MaxValue, 0, true);
        var actors = new[]
        {
            Spawn(1, 0), Spawn(2, 3),
            new EchoesScriptObject("PCKP", 3, 0, true, "Health",
                new[] { 2f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f }, 0x29, 50, true)
        };
        const string sourceHash = "b8b92af37dc772ecd8234fb1ce664be0493188f6";
        MapDefinition map = EchoesLocalMapDraft.BuildSpires(source, actors, sourceHash);
        Check(map.LocalSourceOnly && map.Name == "LOCAL ECHOES SPIRES", "private source marker");
        Check(map.Geometry.Count == 1 && map.Spawns.Count == 2 && map.Items.Count == 1, "compiled authoring draft");
        Check(((MapMesh)map.Geometry[0]).Terrain == "Ice", "Echoes terrain mapping");
        Check(map.Items[0].Type == "HealthMedium", "pickup mapping");
        Check(MapProjectSerializer.Clone(map).LocalSourceOnly, "private marker preserved across clone");
        MapDefinition again = EchoesLocalMapDraft.BuildSpires(source, actors, sourceHash);
        Check(map.MapId == again.MapId && map.Spawns[0].Id == again.Spawns[0].Id,
            "stable local identifiers");
        var compiled = MapBuilder.Build(map);
        Check(compiled.Faces.Count == 1 && compiled.Solid.Count == 1, "project prime native geometry compiler");
        var validation = MapValidator.Validate(map, checkSources: false);
        Check(validation.IsValid, "private Spires blockout passes map validation");
        Expect<InvalidDataException>(() => MapPackageBuilder.Build(map, "never-write.ppmap"),
            "refuse proprietary-derived map package");
        var unknown = MapProjectSerializer.Clone(map);
        unknown.LocalSourceOnly = false;
        Check(!unknown.LocalSourceOnly, "standard maps retain their normal packaging policy");
        Expect<InvalidDataException>(() => EchoesLocalMapDraft.BuildSpires(source,
            new[] { Spawn(1, 0) }, sourceHash), "insufficient spawns fail closed");
        Expect<ArgumentException>(() => EchoesLocalMapDraft.BuildSpires(source, actors, "bad"),
            "source fingerprint required");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException(what);
    }
    private static void Expect<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + what);
    }
}
