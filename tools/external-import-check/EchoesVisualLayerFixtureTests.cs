using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesVisualLayerFixtureTests
{
    public static void Run()
    {
        var collision = new EchoesCollisionData(
            new[] { 0f, 0f, 0f, 5f, 0f, 0f, 0f, 5f, 0f },
            new[] { 0, 1, 2 }, new[] { 0UL });
        EchoesScriptObject Spawn(uint id, float x) => new("SPWN", id, 0, true, "Spawn",
            new[] { x, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f }, uint.MaxValue, 0, true);
        var input = EchoesLocalMapDraft.BuildSpires(
            collision, new[] { Spawn(1, 0), Spawn(2, 3) },
            "b8b92af37dc772ecd8234fb1ce664be0493188f6");
        var world = new EchoesWorldGeometry(
            new[] { 0f, 0f, 0f, 5f, 0f, 0f, 0f, 5f, 0f },
            new[] { 0, 1, 2 });
        var visual = EchoesVisualLayer.CreatePreview(input, world);
        Check(visual.LocalSourceOnly && visual.Name == "VISUAL ECHOES SPIRES",
            "private separate visual preview identity");
        Check(visual.MapId != input.MapId &&
            visual.MapId == EchoesVisualLayer.CreatePreview(input, world).MapId,
            "new deterministic visual-content identity");
        Check(!((MapMesh)input.Geometry[0]).CollisionOnly,
            "historical source collision preview stays unmodified");
        Check(visual.Geometry.OfType<MapMesh>().Count(m => m.CollisionOnly) == 1,
            "original Echoes collision uses only physics channel");
        Check(visual.Geometry.OfType<MapMesh>().Count(m => !m.CollisionOnly) == 1,
            "original world mesh uses only visual channel");
        var build = MapBuilder.Build(visual);
        Check(build.Solid.Count == 1 && build.Faces.Count == 1,
            "native compiler never conflates render and collision");
        Check(MapValidator.Validate(visual, checkSources: false).IsValid,
            "mapped visual preview satisfies validation");
        Expect<InvalidDataException>(() =>
            MapPackageBuilder.Build(visual, "forbidden.ppmap"),
            "private source-render geometry is not a Community package");
        var bad = world with { TriangleVertices = new[] { 0, 1, 99 } };
        Expect<InvalidDataException>(() =>
            EchoesVisualLayer.CreatePreview(input, bad),
            "reject missing source vertex");
        var publicMap = MapProjectSerializer.Clone(input);
        publicMap.LocalSourceOnly = false;
        Expect<InvalidDataException>(() =>
            EchoesVisualLayer.CreatePreview(publicMap, world),
            "reject use with regular shared map");
    }
    private static void Check(bool yes,string name)
    {
        if(!yes) throw new InvalidOperationException("Echoes visual separation: "+name);
    }
    private static void Expect<T>(Action action,string name) where T:Exception
    {
        try { action(); }
        catch(T) { return; }
        throw new InvalidOperationException("Expected "+typeof(T).Name+": "+name);
    }
}
