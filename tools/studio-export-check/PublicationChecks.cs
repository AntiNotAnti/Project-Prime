using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MphRead.Mods.StudioReplay;

internal static class PublicationChecks
{
    internal static async Task<bool> RunChildAsync(string[] args)
    {
        if (args is ["--atomic-signal-child", var destination, var ready, var release])
        {
            string staging = destination + ".held-staging";
            try
            {
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await output.WriteAsync(Encoding.UTF8.GetBytes("4242"));
                    FixturePublication.PublishText(ready, "staging writer is still open");
                    var deadline = Stopwatch.StartNew();
                    while (!File.Exists(release) && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                    if (!File.Exists(release)) throw new TimeoutException("Atomic fixture writer was not released.");
                }
                File.Move(staging, destination);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            return true;
        }
        if (args is ["--replace-status-child", var destinationStatus, var resultFile, var idText])
        {
            try
            {
                FixturePublication.PublishText(destinationStatus, JsonSerializer.Serialize(
                    new StudioReplayExportStatus(Guid.Parse(idText), "Complete", 2, 2, null, Path.GetDirectoryName(destinationStatus)!)));
                FixturePublication.PublishText(resultFile, JsonSerializer.Serialize(new ReplacementResult(true, null, null)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { FixturePublication.PublishText(resultFile, JsonSerializer.Serialize(new ReplacementResult(false, error.GetType().FullName, error.HResult))); }
            return true;
        }
        return false;
    }

    internal static async Task RunAsync(string root, Action<bool, string> check, string? evidenceDirectory)
    {
        string directory = Path.Combine(root, "closed publication handshakes"); Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "parent-pid"), ready = Path.Combine(directory, "staging-ready"), release = Path.Combine(directory, "release");
        using (var writer = Start("--atomic-signal-child", destination, ready, release))
        {
            try
            {
                await WaitForFile(ready, writer);
                check(!File.Exists(destination) && File.Exists(destination + ".held-staging"),
                    "a real child holds its signal writer open without publishing a partial PID handshake");
                FixturePublication.PublishText(release, "release");
                await writer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                check(writer.ExitCode == 0 && int.Parse(FixturePublication.ReadText(destination)) == 4242,
                    "PID existence acknowledges a closed valid signal after atomic publication by an actual child");
            }
            finally { if (!writer.HasExited) { writer.Kill(entireProcessTree: true); await writer.WaitForExitAsync(); } }
        }
        Guid id = Guid.NewGuid();
        string statusFile = Path.Combine(directory, "status.json");
        var initial = new StudioReplayExportStatus(id, "Rendering", 1, 2, null, directory);
        FixturePublication.PublishText(statusFile, JsonSerializer.Serialize(initial));
        bool? oldReaderBlocked = null;
        ReplacementResult? oldReaderResult = null;
        if (OperatingSystem.IsWindows())
        {
            using var blocking = new FileStream(statusFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            ReplacementResult result = await Replace(statusFile, Path.Combine(directory, "blocked-result"), id);
            oldReaderResult = result;
            oldReaderBlocked = !result.Success;
            bool expectedType = result.ErrorType == typeof(IOException).FullName
                || result.ErrorType == typeof(UnauthorizedAccessException).FullName;
            bool expectedNativeDenial = result.HResult == unchecked((int)0x80070005) // ERROR_ACCESS_DENIED
                || result.HResult == unchecked((int)0x80070020) // ERROR_SHARING_VIOLATION
                || result.HResult == unchecked((int)0x80070021); // ERROR_LOCK_VIOLATION
            check(oldReaderBlocked == true && expectedType && expectedNativeDenial,
                "Windows old Read-only sharing demonstrably rejects a real child's atomic status replacement: "
                + result.ErrorType + " HRESULT=" + result.HResult?.ToString("X8"));
        }
        using (var snapshot = StudioReplayStatusFile.OpenSnapshot(statusFile))
        {
            ReplacementResult result = await Replace(statusFile, Path.Combine(directory, "shared-result"), id);
            check(result.Success, "the canonical status reader permits an actual child to atomically replace status while its old snapshot is open");
            var held = JsonSerializer.Deserialize<StudioReplayExportStatus>(snapshot);
            check(held is { State: "Rendering", Frames: 1 } && held.Id == id
                && StudioReplayStatusFile.Read(statusFile) is { State: "Complete", Frames: 2 } published && published.Id == id,
                "an open canonical reader retains the complete old version while a fresh reader sees the complete replacement");
        }
        FixturePublication.PublishText(statusFile, new string(' ', 65537));
        bool oversized = false;
        try { StudioReplayStatusFile.Read(statusFile); } catch (InvalidDataException) { oversized = true; }
        check(oversized, "canonical cross-process status reads enforce the 64 KiB bound on the opened snapshot");
        FixturePublication.PublishText(statusFile, "{");
        bool malformed = false;
        try { StudioReplayStatusFile.Read(statusFile); } catch (JsonException) { malformed = true; }
        check(malformed, "canonical status publication retains malformed JSON rejection");
        if (evidenceDirectory != null)
            FixturePublication.PublishText(Path.Combine(evidenceDirectory, "atomic-publication-witness.json"), JsonSerializer.Serialize(new
            {
                OperatingSystem = Environment.OSVersion.ToString(),
                HeldStagingWasNotPublished = true,
                ClosedSignalRead = 4242,
                WindowsOldReaderNegativeExecuted = OperatingSystem.IsWindows(),
                WindowsOldReaderDeniedReplacement = oldReaderBlocked,
                WindowsOldReaderDeniedException = oldReaderResult?.ErrorType,
                WindowsOldReaderDeniedHResult = oldReaderResult?.HResult,
                CanonicalReaderAllowedActualChildReplacement = true,
                HeldOldSnapshotAndFreshNewSnapshotWereComplete = true,
                StatusSnapshotLimitBytes = 65536
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }
    private static async Task WaitForFile(string path, Process child)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path) && !child.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        if (!File.Exists(path)) throw new TimeoutException("Fixture child failed to acknowledge publication: " + child.Id);
    }
    private static async Task<ReplacementResult> Replace(string statusFile, string resultFile, Guid id)
    {
        using var child = Start("--replace-status-child", statusFile, resultFile, id.ToString());
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (child.ExitCode != 0) throw new IOException("Status replacement child exited " + child.ExitCode);
            return JsonSerializer.Deserialize<ReplacementResult>(FixturePublication.ReadText(resultFile))!;
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    }
    private sealed record ReplacementResult(bool Success, string? ErrorType, int? HResult);
}
