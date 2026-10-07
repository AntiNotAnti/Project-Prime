using System;
using System.IO;
using MphRead.Mods.Launcher;

namespace MphRead.AvaloniaShared;

/// <summary>Read-only extracted asset configuration shared by desktop creator hosts.</summary>
public static class StudioGameAssets
{
    public static void InitializeAuthoringProcess() => MphRead.Mods.MapGen.CustomRooms.DeferInitialRegistration = true;
    public static string? PathsFile { get; private set; }
    public static string? Problem { get; private set; }
    public static bool Ready
    {
        get
        {
            try { Apply(); return GameFiles.CoreRootProblem(Paths.FileSystem) is null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Problem=ex.Message; return false; }
        }
    }
    public static void Configure(string pathsFile)
    {
        InitializeAuthoringProcess();
        pathsFile=Path.GetFullPath(pathsFile);
        if (!File.Exists(pathsFile)) throw new FileNotFoundException("Choose an existing Project Prime extraction paths.txt file.",pathsFile);
        string[] lines=File.ReadAllLines(pathsFile);
        if (lines.Length==0 || !Version.TryParse(lines[0].Trim(),out var version) || version < new Version(0,19,0,0)) throw new IOException("The extraction paths file is from an unsupported version.");
        PathsFile=pathsFile;
        Apply();
        Problem=GameFiles.CoreRootProblem(Paths.FileSystem);
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
        Paths.UpdatePaths(PathsFile); Paths.ChooseMphPath(); Paths.ChooseFhPath();
    }
}
