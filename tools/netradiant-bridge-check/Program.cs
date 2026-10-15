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

    // Resolve simulated platform layouts under a private tree; never execute fixtures.
    string products = Path.Combine(directory, "installed products");
    string studioMac = Path.Combine(products, "Project Prime Studio.app", "Contents", "MacOS");
    string radiantMac = Path.Combine(products, "Project Prime Radiant.app", "Contents", "MacOS");
    string resourcesMac = Path.Combine(products, "Project Prime Radiant.app", "Contents", "Resources", "netradiant");
    Directory.CreateDirectory(studioMac);Directory.CreateDirectory(radiantMac);
    string macEditor = Path.Combine(radiantMac, "radiant"), macCompiler = Path.Combine(radiantMac, "q3map2");
    File.WriteAllText(macEditor, "locator fixture");File.WriteAllText(macCompiler, "locator fixture");
    var discovered = NetRadiantToolDiscovery.Resolve(studioMac, NetRadiantPlatform.MacOS);
    Check(discovered.EditorExecutable == macEditor && discovered.CompilerExecutable == macCompiler
        && discovered.ResourceDirectory == resourcesMac,
        "Mac Studio discovers only the fixed sibling Radiant app and Resources/netradiant root");
    Check(NetRadiantToolDiscovery.Resolve(Path.Combine(products,"Unrelated.app","Contents","MacOS"), NetRadiantPlatform.MacOS).EditorExecutable == null,
        "unrelated Mac app paths do not gain sibling editor discovery");
    foreach (var platform in new[] { NetRadiantPlatform.Windows, NetRadiantPlatform.Linux })
    {
        string flat = Path.Combine(products, platform.ToString());
        string product = Path.Combine(flat, "Project Prime Radiant");Directory.CreateDirectory(product);
        string suffix = platform == NetRadiantPlatform.Windows ? ".exe" : "";
        string editor = Path.Combine(product,"radiant"+suffix), compilerPath = Path.Combine(product,"q3map2"+suffix);
        File.WriteAllText(editor,"locator fixture");File.WriteAllText(compilerPath,"locator fixture");
        var found = NetRadiantToolDiscovery.Resolve(flat,platform);
        Check(found.EditorExecutable == editor && found.CompilerExecutable == compilerPath && found.ResourceDirectory == product,
            platform + " discovers the fixed separate Radiant product directory");
        var overridden = NetRadiantToolDiscovery.Resolve(flat,platform,macEditor,macCompiler);
        Check(overridden.EditorExecutable == macEditor && overridden.CompilerExecutable == macCompiler,
            platform + " explicit absolute tool overrides take precedence");
        foreach (string invalid in new[] { "radiant", "", Path.Combine(directory,"missing-tool") })
        {
            var refused = NetRadiantToolDiscovery.Resolve(flat,platform,invalid,invalid);
            Check(refused.EditorExecutable == null && refused.CompilerExecutable == null
                && refused.EditorError != null && refused.CompilerError != null,
                platform + " malformed overrides refuse packaged fallback: " + (invalid.Length == 0 ? "empty" : Path.GetFileName(invalid)));
        }
        File.Delete(editor);
        Check(NetRadiantToolDiscovery.Resolve(flat,platform).EditorExecutable == null,
            platform + " missing packaged editor retains native fallback");
    }
    Check(NetRadiantToolDiscovery.Resolve(studioMac,NetRadiantPlatform.Unsupported,macEditor,macCompiler) is {EditorExecutable:null,CompilerExecutable:null},
        "Android/unsupported platforms cannot activate external tools even with overrides");
    string gameDescription = Path.Combine(resourcesMac,"gamepacks","games","projectprime.game");
    string primePack = Path.Combine(resourcesMac,"gamepacks","projectprime.game");
    Directory.CreateDirectory(Path.GetDirectoryName(gameDescription)!);Directory.CreateDirectory(primePack);
    File.WriteAllText(gameDescription,"trusted fixed pack fixture");
    var macLaunch = NetRadiantBridge.CreateEditorStartInfo(macEditor,map);
    Check(macLaunch.FileName == macEditor && !macLaunch.UseShellExecute && macLaunch.WorkingDirectory == directory
        && macLaunch.ArgumentList.SequenceEqual(new[] {"-global-gamefile","projectprime.game","-projectprime.game-EnginePath",primePack+Path.DirectorySeparatorChar,map}),
        "Mac launch selects trusted resource gamepack and engine root with exact shell-free arguments");
    File.Delete(gameDescription);
    Check(NetRadiantBridge.CreateEditorStartInfo(macEditor,map).ArgumentList.SequenceEqual(new[]{map}),
        "missing canonical gamepack never borrows configuration from source or working directory");
    string linkedEditor = Path.Combine(directory,"linked-editor");
    try
    {
        File.CreateSymbolicLink(linkedEditor,macEditor);
        Check(NetRadiantToolDiscovery.Resolve(studioMac,NetRadiantPlatform.MacOS,linkedEditor).EditorExecutable == null,
            "symbolic executable override is refused without falling back to packaged tools");
    }
    catch(Exception ex) when(OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
    { Console.WriteLine("SKIP symbolic override fixture: Windows symlink creation is unavailable."); }

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
