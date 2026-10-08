using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Uses the existing Studio launch/IPC contract and OS picker without a toolkit window.</summary>
public sealed class LauncherStudioBackend : IStudioEntryBackend
{
    public StudioAvailability Availability()
    {
#if !ANDROID && !MPHREAD_SERVER
        bool installed = StudioIntegration.StudioApplicationLauncher.FindExecutable(AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_PATH")) != null;
        return new(true, installed, NativeFilePicker.Available, installed
            ? "Project Prime Studio is installed. Map and Replay editing open in the paired application."
            : "Project Prime Studio is unavailable. Install the complete desktop release or configure PROJECT_PRIME_STUDIO_PATH.");
#else
        return new(false, false, false, "Map and Replay authoring require the paired desktop Project Prime Studio application.");
#endif
    }

    public StudioPathResult ValidatePath(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return new(false, Error: "Enter a map project, replay or clip file path.");
        string canonical;
        try
        {
            string value = path.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            canonical = Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        { return new(false, Error: "That file path is invalid. Enter a local path to an existing document."); }
        string extension = Path.GetExtension(canonical).ToLowerInvariant();
        StudioDocumentKind? kind = extension switch
        {
            ".json" or ".ppmap" => StudioDocumentKind.Map,
            ".ppdemo" => StudioDocumentKind.Replay,
            ".ppclip" => StudioDocumentKind.Clip,
            _ => null
        };
        if (kind == null) return new(false, Error: "Choose a .json map project, .ppmap map package, .ppdemo replay or .ppclip clip.");
        if (!File.Exists(canonical)) return new(false, Error: "The selected document is missing or cannot be read. Check the path and file permissions.");
        try
        {
            using var readable = File.Open(canonical, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(false, Error: "The selected document cannot be read. Check its permissions or close the application locking it."); }
        return new(true, canonical, kind.Value);
    }

    public StudioActionResult Launch(string? documentPath, bool recover)
    {
#if !ANDROID && !MPHREAD_SERVER
        return StudioIntegration.StudioApplicationLauncher.TryOpen(documentPath, recover, out string? error)
            ? new(true) : new(false, error ?? "Project Prime Studio could not start.");
#else
        return new(false, "Map and Replay authoring require the paired desktop Project Prime Studio application.");
#endif
    }

    public async Task<StudioPickResult> PickMapAsync(CancellationToken cancellationToken)
    {
#if !ANDROID && !MPHREAD_SERVER
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeFilePicker.Available)
            return new(Error: "The system file picker is unavailable. Enter a map project path below.");
        // The existing picker owns its OS dialog. Cancellation retires its
        // result; it cannot close the shared platform dialog programmatically.
        string? chosen = await NativeFilePicker.OpenFile("Open map project", "Project Prime map project", "json")
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(chosen);
#else
        await Task.CompletedTask;
        return new(Error: "Use the paired desktop Studio application to open map projects.");
#endif
    }
}
