using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MphRead.Mods.MapGen;

// PR-12's asset-free bridge checks can run on any desktop CI runner.
// They do not claim a third-party NetRadiant/q3map2 binary was built or
// executed without explicitly configured external binaries.
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
    checks++;
}
string gamepack = Path.Combine(Directory.GetCurrentDirectory(),
    "tools", "netradiant-gamepack");
XDocument config = XDocument.Load(Path.Combine(gamepack, "projectprime.game"));
Check(config.Root?.Name.LocalName == "game"
    && (string?)config.Root.Attribute("type") == "q3"
    && (string?)config.Root.Attribute("mapformat") == "q3",
    "Project Prime gamepack XML is well-formed Quake-compatible external configuration");
string entities = File.ReadAllText(Path.Combine(gamepack, "entities.ent"));
Check(entities.Contains("info_player_deathmatch", StringComparison.Ordinal)
    && entities.Contains("trigger_teleport", StringComparison.Ordinal),
    "gamepack includes original Quake-style spawn and gameplay authoring helpers");
Check(!Directory.EnumerateFiles(gamepack, "*", SearchOption.AllDirectories)
    .Any(x => x.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase)
        || x.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)
        || x.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)),
    "gamepack ships no proprietary gameplay maps, compiled binaries or textures");

string directory = Path.Combine(Path.GetTempPath(),
    "prime-radiant-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string map = Path.Combine(directory, "author source.map");
    File.WriteAllText(map, """
{
"classname" "worldspawn"
}
""");
    bool rejected = false;
    try { using var invalid = NetRadiantBridge.LaunchEditor("radiant", map); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "external editor invocation refuses PATH-only or shell-resolved commands");

    rejected = false;
    try
    {
        await NetRadiantBridge.CompileImportAsync("q3map2", map, "MAP",
            Path.Combine(directory, "project"), CancellationToken.None);
    }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "compiler refuses arbitrary PATH strings and shell expansion");

    rejected = false;
    try
    {
        using var invalid = NetRadiantBridge.LaunchEditor(
            Path.Combine(directory, "missing-editor"), "not-a-map.json");
    }
    catch (FileNotFoundException) { rejected = true; }
    Check(rejected, "missing external editor fails with a clear file error");

    string? compiler = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3MAP2_EXE");
    if (!string.IsNullOrWhiteSpace(compiler) && File.Exists(compiler))
        Console.WriteLine("External q3map2 configured: run separate real BSP gamepack acceptance.");
    else
        Console.WriteLine("SKIP external q3map2 build/import: no separately installed compiler configured.");

    Console.WriteLine($"NetRadiant bridge checks: {checks} PASS.");
}
finally
{
    try { Directory.Delete(directory, true); } catch (IOException) { }
}
