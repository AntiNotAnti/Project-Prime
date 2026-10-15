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
foreach (string required in new[] { "basegamename", "unknowngamename", "entities", "entityclass", "entityclasstype", "brushtypes", "patchtypes", "maptypes", "shaders", "texturetypes", "archivetypes", "modeltypes", "engine_win32", "engine_linux", "engine_macos", "enginepath_win32", "enginepath_linux", "enginepath_macos" })
    Check(config.Root?.Attribute(required) != null,
        "gamepack declares required NetRadiant module/path attribute " + required);
XDocument entityDefinitions = XDocument.Load(Path.Combine(gamepack, "entities.ent"));
Check(entityDefinitions.Root?.Name.LocalName == "classes"
    && entityDefinitions.Root.Elements("point").Any(x => (string?)x.Attribute("name") == "info_player_deathmatch"),
    "entity definitions use actual NetRadiant XML .ent syntax");
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

    // The preferred external editor activates only for an explicitly
    // configured *regular* desktop binary. Tests never execute the fake file.
    string? previousEditor = Environment.GetEnvironmentVariable(
        CommunityCutoverPolicy.NetRadiantExecutableVariable);
    string? previousDefault = Environment.GetEnvironmentVariable(
        CommunityCutoverPolicy.NetRadiantDefaultVariable);
    try
    {
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantExecutableVariable, null);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, null);
        Check(!CommunityCutoverPolicy.NetRadiantDefault,
            "native Studio remains available when NetRadiant is not installed");
        string testExecutable = Path.Combine(directory, "local-radiant-tool");
        File.WriteAllText(testExecutable, "test binary locator only");
        var genericLaunch = NetRadiantBridge.CreateEditorStartInfo(testExecutable, map);
        Check(genericLaunch.ArgumentList.SequenceEqual(new[] { map }) && !genericLaunch.UseShellExecute,
            "generic external editor retains the source-only argument contract");
        Directory.CreateDirectory(Path.Combine(directory, "games"));
        Directory.CreateDirectory(Path.Combine(directory, "gamepacks", "projectprime.game"));
        File.WriteAllText(Path.Combine(directory, "games", "projectprime.game"), "trusted sibling pack marker");
        var primeLaunch = NetRadiantBridge.CreateEditorStartInfo(testExecutable, map);
        Check(primeLaunch.ArgumentList.SequenceEqual(new[] { "-global-gamefile", "projectprime.game", map }),
            "trusted installed sibling gamepack selects the pinned Prime editor configuration");
        string legacySource = Path.Combine(directory, "legacy-source.map");
        File.WriteAllText(legacySource, "// Project Prime original Quake3 .map brush authoring source\n{\n\"classname\" \"worldspawn\"\n}\n");
        bool legacyRefused = false;
        try
        {
            await NetRadiantBridge.CompileImportAsync(testExecutable, legacySource, "LEGACY",
                Path.Combine(directory, "legacy-output"), CancellationToken.None);
        }
        catch (InvalidDataException) { legacyRefused = true; }
        Check(legacyRefused && !Directory.Exists(Path.Combine(directory, "legacy-output")),
            "legacy Prime winding requires explicit native reexport before invoking a real compiler");
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantExecutableVariable, testExecutable);
        Check(CommunityCutoverPolicy.NetRadiantDefault
            && CommunityCutoverPolicy.NetRadiantExecutable == testExecutable,
            "configured fully-qualified local NetRadiant becomes the preferred editor");
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, "0");
        Check(!CommunityCutoverPolicy.NetRadiantDefault,
            "the native editor can be restored using the original NetRadiant default rollback flag");
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, null);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantExecutableVariable, "radiant");
        Check(!CommunityCutoverPolicy.NetRadiantDefault,
            "PATH-only executable strings never silently activate third-party programs");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantExecutableVariable, previousEditor);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, previousDefault);
    }

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
