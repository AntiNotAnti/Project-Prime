using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher;

namespace MphRead.AvaloniaShared;

/// <summary>Read-only extracted asset configuration shared by desktop creator hosts.</summary>
public static class StudioGameAssets
{
    public static void InitializeAuthoringProcess() => MphRead.Mods.MapGen.CustomRooms.DeferInitialRegistration = true;
    /// <summary>Read the game's local presentation preferences without migrating or writing its settings file.</summary>
    public static void LoadPresentationSettings()
    {
        string folder=Path.Combine(GameFiles.Root,"Savedata"),file=Path.Combine(folder,"settings.json");
        var settings=new MenuSettings();
        if(File.Exists(file))
        {
            using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if(json.RootElement.TryGetProperty("MenuSettings",out var menu))
                settings=System.Text.Json.JsonSerializer.Deserialize<MenuSettings>(menu.GetRawText(),
                    new System.Text.Json.JsonSerializerOptions{Converters={new SceneGameState.ByteArrayConverter()}})??settings;
            if(json.RootElement.TryGetProperty("Features",out var features))
                Features.Load(System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,string>>(features.GetRawText())??new());
        }
        Mods.SettingsMigration.Apply(settings,out _);
        Mods.Render.Hud.HudProfiles.Load(Path.Combine(folder,"hud-profiles"));
        Mods.GameSettings.Apply(settings);
    }
    public static string? PathsFile { get; private set; }
    public static string? Problem { get; private set; }
    public static bool Ready
    {
        get
        {
            try { Apply(); return ConfiguredRootProblem() is null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Problem=ex.Message; return false; }
        }
    }
    private static string? ConfiguredRootProblem() => string.IsNullOrWhiteSpace(Paths.FileSystem)||!Directory.Exists(Paths.FileSystem)
        ? "The configured extracted game directory is missing." : null;
    public static void Configure(string pathsFile)
    {
        InitializeAuthoringProcess();
        pathsFile=Path.GetFullPath(pathsFile);
        if (!File.Exists(pathsFile)) throw new FileNotFoundException("Choose an existing Project Prime extraction paths.txt file.",pathsFile);
        string[] lines=File.ReadAllLines(pathsFile);
        if (lines.Length==0 || !Version.TryParse(lines[0].Trim(),out var version) || version < new Version(0,19,0,0)) throw new IOException("The extraction paths file is from an unsupported version.");
        PathsFile=pathsFile;
        Apply();
        Problem=ConfiguredRootProblem();
    }
    public static void TryConfigureDefault()
    {
        InitializeAuthoringProcess();
        if (PathsFile is not null) return;
        foreach (string path in new[] { Path.Combine(GameFiles.Root,"paths.txt"),Path.Combine(AppContext.BaseDirectory,"paths.txt"),Path.GetFullPath("paths.txt") })
        {
            if (!File.Exists(path)) continue;
            try { Configure(path); return; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Problem=ex.Message; }
        }
    }
    public static void Apply()
    {
        if (PathsFile is null) throw new IOException("Choose Project Prime's extracted game paths in Studio's File menu.");
        // Resolve configured roots without changing the process working directory.
        // The canonical parser exposes SetPath; unknown keys stay ignored.
        var known=Paths.AllPaths.Keys.ToHashSet(StringComparer.Ordinal);
        foreach(string key in known)Paths.SetPath(key, "");
        string root=Path.GetDirectoryName(PathsFile)!;
        foreach(string line in File.ReadLines(PathsFile))
        {
            int split=line.IndexOf('=');if(split<0)continue;
            string key=line[..split].Trim(),value=line[(split+1)..].Trim();
            if(!known.Contains(key)||value.Length==0)continue;
            Paths.SetPath(key,Path.IsPathRooted(value)?value:Path.GetFullPath(Path.Combine(root,value)));
        }
        Paths.ChooseMphPath(); Paths.ChooseFhPath();
    }
}
