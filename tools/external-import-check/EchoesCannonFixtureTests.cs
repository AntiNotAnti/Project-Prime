using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesCannonFixtureTests
{
    public static void Run()
    {
        Check(EchoesTunedCannons.TotalCount == 12 &&
            EchoesTunedCannons.ArenaKeys.Count == 4,
            "upstream kinetic cannon inventory pinned");
        Check(EchoesTunedCannons.CountFor("M02_Spires") == 3 &&
            EchoesTunedCannons.CountFor("M05_SpiderComplex") == 2 &&
            EchoesTunedCannons.CountFor("M03_CrossfireChaos") == 0 &&
            EchoesTunedCannons.CountFor("M04_Pipeline") == 0,
            "only source arenas with tuned cannons receive triggers");
        var collision = new EchoesCollisionData(
            new[] { 0f, 0f, 0f, 5f, 0f, 0f, 0f, 5f, 0f },
            new[] { 0, 1, 2 }, new[] { 0UL });
        EchoesScriptObject Spawn(uint id, float x) => new("SPWN", id, 0, true, "Spawn",
            new[] { x, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f }, uint.MaxValue, 0, true);
        const string hash = "b8b92af37dc772ecd8234fb1ce664be0493188f6";
        var original = EchoesLocalMapDraft.BuildSpires(collision,
            new[] { Spawn(1, 0), Spawn(2, 3) }, hash);
        var world = new EchoesWorldGeometry(
            new[] { 0f, 0f, 0f, 5f, 0f, 0f, 0f, 5f, 0f }, new[] { 0, 1, 2 });
        var visual = EchoesVisualLayer.CreatePreview(original, world);
        var cannons = EchoesTunedCannons.CreateVariant(visual, "M02_Spires");
        Check(cannons.Name == "CANNON ECHOES SPIRES" &&
            cannons.JumpPads.Count == 3 && cannons.Capabilities?.MaxPlayers == 1,
            "separate solo-only cannon map");
        Check(original.JumpPads.Count == 0 && visual.JumpPads.Count == 0 &&
            original.MapId != visual.MapId && visual.MapId != cannons.MapId,
            "original and visual preview identities/behavior unchanged");
        Check(cannons.MapId == EchoesTunedCannons.CreateVariant(visual, "M02_Spires").MapId,
            "stable cannon map identity");
        Check(cannons.JumpPads.All(p => p.Vector is { Length: 3 } &&
            p.Speed > 0 && p.Size.Length == 3 && p.CooldownTime > 0),
            "valid tuned vectors and bounded cooldown");
        var result = MapValidator.Validate(cannons, checkSources: false);
        Check(result.IsValid, "tuned map respects source authoring constraints");
        var built = MapBuilder.Build(cannons);
        Check(built.Entities.Count == 2 + 3 && built.Solid.Count == 1 &&
            built.Faces.Count == 1, "pads use native entities without changing collision");
        string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "echoes-cannon-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            string saved = EchoesPrivateMapImport.InstallLocalDefinition(cannons, temp);
            Check(saved.EndsWith("cannon-spires.json") && File.Exists(saved),
                "private cannon map installed without Community archive");
            Check(MapDefinition.Load(saved).JumpPads.Count == 3 &&
                MapDefinition.Load(saved).MapId == cannons.MapId,
                "persisted cannon launch recipe");
            Check(!Directory.EnumerateFiles(temp, "*.ppmap", SearchOption.AllDirectories).Any(),
                "no distributable commercial game bytes");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
        Expect<InvalidDataException>(() =>
            EchoesTunedCannons.CreateVariant(visual, "M04_Pipeline"),
            "cannot invent cannons for Pipeline");
        Expect<InvalidDataException>(() =>
            EchoesTunedCannons.CreateVariant(original, "M02_Spires"),
            "requires separate visual-tier source identity");
    }

    private static void Check(bool value, string why)
    {
        if (!value) throw new InvalidOperationException("Echoes cannon fixture: " + why);
    }
    private static void Expect<T>(Action action, string name) where T:Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " for " + name);
    }
}
