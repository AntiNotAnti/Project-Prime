using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesPrivateMapFixtureTests
{
    public static void Run()
    {
        string temp = Path.Combine(Path.GetTempPath(), "prime-echoes-private-tests-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var collision = new EchoesCollisionData(
                new[] { 0f, 0f, 0f, 5f, 0f, 0f, 0f, 5f, 0f },
                new[] { 0, 1, 2 }, new[] { 1UL << 4 });
            EchoesScriptObject Spawn(uint id, float x) => new("SPWN", id, 0, true, "Spawn",
                new[] { x, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f }, uint.MaxValue, 0, true);
            var map = EchoesLocalMapDraft.BuildSpires(
                collision, new[] { Spawn(1, 0), Spawn(2, 3) },
                "b8b92af37dc772ecd8234fb1ce664be0493188f6");
            string installed = EchoesPrivateMapImport.InstallLocalDefinition(map, temp);
            Check(File.Exists(installed) && installed.EndsWith("spires.json"),
                "private map installed");
            MapDefinition loaded = MapDefinition.Load(installed);
            Check(loaded.LocalSourceOnly && loaded.MapId == map.MapId &&
                loaded.Name == "LOCAL ECHOES SPIRES" &&
                loaded.Spawns.Count == 2, "local map JSON roundtrip");
            Check(loaded.Geometry.OfType<MapMesh>().Any(),
                "locally converted geometry stored as authored mesh");
            string original = File.ReadAllText(installed);
            Check(EchoesPrivateMapImport.InstallLocalDefinition(map, temp) == installed
                && File.ReadAllText(installed) == original, "repeat import remains stable");
            Check(!Directory.EnumerateFiles(temp, "*.ppmap",
                SearchOption.AllDirectories).Any(), "no game-derived Community archive");
            // All six arena keys must have independent and deterministic
            // local map identities, without ever producing .ppmap packages.
            var all = EchoesArenaSources.All.Select(arena =>
                EchoesLocalMapDraft.BuildArena(arena, collision,
                    new[] { Spawn(1, 0), Spawn(2, 3) },
                    "b8b92af37dc772ecd8234fb1ce664be0493188f6")).ToArray();
            Check(all.Length == 6 && all.Select(x => x.MapId).Distinct().Count() == 6,
                "six unique local arena identities");
            Check(all.Single(x => x.Name == "LOCAL ECHOES SPIRES").MapId == map.MapId,
                "Spires source identity remains backward-compatible");
            Check(all.All(x => x.LocalSourceOnly && x.Capabilities?.MaxPlayers == 1),
                "preview arenas cannot enter a full multiplayer lobby");
            var paths = EchoesPrivateMapImport.InstallLocalDefinitions(all, temp);
            Check(paths.Count == 6 && paths.All(File.Exists), "six private map recipes");
            Check(!Directory.EnumerateFiles(temp, "*.ppmap",
                SearchOption.AllDirectories).Any(), "no commercial game data packaged after bulk import");
            var notLocal = MapProjectSerializer.Clone(map);
            notLocal.LocalSourceOnly = false;
            Expect<InvalidDataException>(() =>
                EchoesPrivateMapImport.InstallLocalDefinition(notLocal, temp),
                "reject ordinary/sharable map in private import endpoint");
            Check(File.ReadAllText(installed) == original,
                "rejected transaction leaves verified project unchanged");
            Expect<ArgumentException>(() =>
                EchoesPrivateMapImport.InstallLocalDefinition(map, " "),
                "require explicit output namespace");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static void Check(bool value, string what)
    {
        if (!value) throw new InvalidOperationException("Private Echoes install: " + what);
    }
    private static void Expect<T>(Action action, string why) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why);
    }
}
