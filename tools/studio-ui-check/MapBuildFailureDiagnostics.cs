using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    // Canonical Build reports compiler failures through diagnostics and publication
    // failures through the screen/job owner. Preserve both before cleanup removes
    // the isolated profile, rather than losing the cause behind a missing-file check.
    private static string CaptureMapBuildFailure(StudioWindow window, MapStudioDocument document,
        MapDocument canonical, StudioPaths paths, string output)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object? screen = document.Host.GetType().GetField("_screen", fields)?.GetValue(document.Host);
        string? status = screen?.GetType().GetField("_status", fields)?.GetValue(screen) is TextBlock text ? text.Text : null;
        var build = screen?.GetType().GetField("_lastBuild", fields)?.GetValue(screen) as MapBuildResult;
        string runtime = Path.Combine(paths.UserDataDirectory, "runtime");
        string DescribeIdentity()
        {
            try { return MapPublicationLease.ResolveRuntimeDirectoryAliases(runtime); }
            catch (Exception error) { return error.ToString(); }
        }
        object[] files;
        try
        {
            files = Directory.Exists(paths.UserDataDirectory)
                ? Directory.EnumerateFiles(paths.UserDataDirectory, "*", SearchOption.AllDirectories).Take(300)
                    .Select(file => (object)new { path = Path.GetRelativePath(paths.UserDataDirectory, file), bytes = new FileInfo(file).Length }).ToArray()
                : [];
        }
        catch (Exception error) { files = [new { listingError = error.ToString() }]; }
        string diagnostic = JsonSerializer.Serialize(new
        {
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            status, canonical.Diagnostics, build,
            jobs = window.Jobs.Jobs.Select(job => new { job.Title, job.State, job.Error, job.Progress, job.Elapsed }),
            paths.InstallationDirectory, paths.UserDataDirectory, runtime, runtimeExists = Directory.Exists(runtime),
            runtimeIdentity = DescribeIdentity(), files
        }, new JsonSerializerOptions { WriteIndented = true });
        string report = Path.Combine(output, "map-studio-runtime-build-failure.json");
        try { File.WriteAllText(report, diagnostic); }
        catch (Exception error) { diagnostic += "\nUnable to persist diagnostics: " + error; }
        try
        {
            PumpLayout(window);
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(output, "map-studio-runtime-build-failure.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        catch (Exception error) { diagnostic += "\nUnable to capture failed build: " + error; }
        Console.Error.WriteLine("Map runtime build failure diagnostics: " + report + "\n" + diagnostic);
        return diagnostic;
    }
}
