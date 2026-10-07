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
        if (args is [var publication, var destinationStatus, var resultFile, var idText]
            && publication is "--replace-status-child" or "--legacy-move-status-child")
        {
            try
            {
                var status = new StudioReplayExportStatus(Guid.Parse(idText), "Complete", 2, 2, null,
                    Path.GetDirectoryName(destinationStatus)!);
                if (publication == "--legacy-move-status-child")
                    FixturePublication.PublishText(destinationStatus, JsonSerializer.Serialize(status));
                else
                    StudioReplayStatusFile.Write(destinationStatus, status);
                FixturePublication.PublishText(resultFile, JsonSerializer.Serialize(new ReplacementResult(true, null, null)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { FixturePublication.PublishText(resultFile, JsonSerializer.Serialize(new ReplacementResult(false, error.GetType().FullName, error.HResult, error.Message))); }
            return true;
        }
        return false;
    }

    internal static async Task RunAsync(string root, Action<bool, string> check, string? evidenceDirectory)
    {
        string directory = Path.Combine(root, "closed publication handshakes"); Directory.CreateDirectory(directory);
        string? retainedEvidence = evidenceDirectory;
        if (retainedEvidence is null && Environment.GetEnvironmentVariable("RUNNER_TEMP") is { Length: > 0 } runnerTemporary)
            retainedEvidence = Path.Combine(runnerTemporary, "studio-ui", "export-publication");
        if (retainedEvidence is not null) Directory.CreateDirectory(retainedEvidence);
        var attempts = new List<object>();
        var witness = new Dictionary<string, object?>
        {
            ["Scope"] = "Actual closed status publication and opened immutable snapshots; this diagnostic alone does not claim UI or paired-publish completion.",
            ["OperatingSystem"] = Environment.OSVersion.ToString(),
            ["WindowsOldReaderNegativeExecuted"] = OperatingSystem.IsWindows(),
            ["Attempts"] = attempts
        };
        void Record(string stage, ReplacementResult? result = null)
        {
            witness["Stage"] = stage;
            var attempt = new { Stage = stage, Result = result, HResultHex = result?.HResult?.ToString("X8") };
            attempts.Add(attempt);
            Console.WriteLine("Status publication witness: " + JsonSerializer.Serialize(attempt));
            if (retainedEvidence is not null)
                FixturePublication.PublishText(Path.Combine(retainedEvidence, "atomic-publication-witness.json"),
                    JsonSerializer.Serialize(witness, new JsonSerializerOptions { WriteIndented = true }));
        }
        Record("Started");
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
        StudioReplayStatusFile.Write(statusFile, initial);
        check(StudioReplayStatusFile.Read(statusFile) == initial,
            "the canonical publisher creates a complete initial status without an existing target");
        bool? oldReaderBlocked = null;
        ReplacementResult? oldReaderResult = null;
        if (OperatingSystem.IsWindows())
        {
            string legacyStatus = Path.Combine(directory, "legacy-move-status.json");
            StudioReplayStatusFile.Write(legacyStatus, initial);
            using (var legacySnapshot = StudioReplayStatusFile.OpenSnapshot(legacyStatus))
            {
                ReplacementResult legacy = await Replace(legacyStatus, Path.Combine(directory, "legacy-result"), id, legacyMove: true);
                witness["WindowsLegacyMoveResult"] = legacy;
                Record("WindowsLegacyMoveUnderCanonicalReader", legacy);
                check(IsNativeWindowsDenial(legacy),
                    "Windows legacy MoveFileEx replacement is denied despite canonical delete sharing: " + Describe(legacy));
                check(JsonSerializer.Deserialize<StudioReplayExportStatus>(legacySnapshot) == initial
                    && StudioReplayStatusFile.Read(legacyStatus) == initial,
                    "failed legacy replacement preserves both held and fresh complete old versions");
            }
            using var blocking = new FileStream(statusFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            ReplacementResult result = await Replace(statusFile, Path.Combine(directory, "blocked-result"), id);
            oldReaderResult = result;
            oldReaderBlocked = !result.Success;
            witness["WindowsOldReaderDeniedReplacement"] = oldReaderBlocked;
            witness["WindowsOldReaderResult"] = result;
            Record("CanonicalPublisherUnderOldReadOnlySharing", result);
            check(IsNativeWindowsDenial(result),
                "Windows old Read-only sharing demonstrably rejects a real child's canonical atomic status replacement: " + Describe(result));
            check(StudioReplayStatusFile.Read(statusFile) == initial,
                "denied canonical publication preserves the complete initial status");
        }
        using (var snapshot = StudioReplayStatusFile.OpenSnapshot(statusFile))
        {
            ReplacementResult result = await Replace(statusFile, Path.Combine(directory, "shared-result"), id);
            witness["CanonicalReaderResult"] = result;
            Record("CanonicalPublisherUnderCanonicalReader", result);
            check(result.Success, "the canonical status reader permits an actual child to atomically replace status while its old snapshot is open: " + Describe(result));
            var held = JsonSerializer.Deserialize<StudioReplayExportStatus>(snapshot);
            check(held is { State: "Rendering", Frames: 1 } && held.Id == id
                && StudioReplayStatusFile.Read(statusFile) is { State: "Complete", Frames: 2 } published && published.Id == id,
                "an open canonical reader retains the complete old version while a fresh reader sees the complete replacement");
        }
        byte[] previous = File.ReadAllBytes(statusFile);
        bool rejectedWrite = false;
        try { StudioReplayStatusFile.Write(statusFile, initial with { Error = new string('x', 65536) }); }
        catch (InvalidDataException) { rejectedWrite = true; }
        check(rejectedWrite && File.ReadAllBytes(statusFile).AsSpan().SequenceEqual(previous),
            "oversized canonical publication is rejected before replacing the previous good status");
        check(!Directory.EnumerateFiles(directory, "*.status.*").Any(),
            "successful and denied canonical publications leave no owned status staging files");
        FixturePublication.PublishText(statusFile, new string(' ', 65537));
        bool oversized = false;
        try { StudioReplayStatusFile.Read(statusFile); } catch (InvalidDataException) { oversized = true; }
        check(oversized, "canonical cross-process status reads enforce the 64 KiB bound on the opened snapshot");
        FixturePublication.PublishText(statusFile, "{");
        bool malformed = false;
        try { StudioReplayStatusFile.Read(statusFile); } catch (JsonException) { malformed = true; }
        check(malformed, "canonical status publication retains malformed JSON rejection");
        witness["HeldStagingWasNotPublished"] = true;
        witness["ClosedSignalRead"] = 4242;
        witness["WindowsOldReaderDeniedReplacement"] = oldReaderBlocked;
        witness["WindowsOldReaderDeniedException"] = oldReaderResult?.ErrorType;
        witness["WindowsOldReaderDeniedHResult"] = oldReaderResult?.HResult;
        witness["CanonicalReaderAllowedActualChildReplacement"] = true;
        witness["HeldOldSnapshotAndFreshNewSnapshotWereComplete"] = true;
        witness["OversizedWritePreservedPreviousStatus"] = true;
        witness["OwnedStatusStagingFiles"] = 0;
        witness["StatusSnapshotLimitBytes"] = 65536;
        Record("Passed");
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
    private static async Task<ReplacementResult> Replace(string statusFile, string resultFile, Guid id, bool legacyMove = false)
    {
        using var child = Start(legacyMove ? "--legacy-move-status-child" : "--replace-status-child", statusFile, resultFile, id.ToString());
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (child.ExitCode != 0) throw new IOException("Status replacement child exited " + child.ExitCode);
            return JsonSerializer.Deserialize<ReplacementResult>(FixturePublication.ReadText(resultFile))!;
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    }
    private static bool IsNativeWindowsDenial(ReplacementResult result)
        => !result.Success && (result.ErrorType == typeof(IOException).FullName
                || result.ErrorType == typeof(UnauthorizedAccessException).FullName)
            && (result.HResult == unchecked((int)0x80070005) // ERROR_ACCESS_DENIED
                || result.HResult == unchecked((int)0x80070020) // ERROR_SHARING_VIOLATION
                || result.HResult == unchecked((int)0x80070021)); // ERROR_LOCK_VIOLATION

    private static string Describe(ReplacementResult result)
        => JsonSerializer.Serialize(new { Result = result, HResultHex = result.HResult?.ToString("X8") });

    private sealed record ReplacementResult(bool Success, string? ErrorType, int? HResult, string? ErrorMessage = null);
}
