using System.Diagnostics;
using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;

if (await PublicationChecks.RunChildAsync(args)) return;

if (args is ["--pin-child", var pinnedRoot, var pinnedKey, var pinReady])
{
    using var pin = MphRead.Mods.MapGen.MapDiskCache.Pin(pinnedRoot, pinnedKey);
    FixturePublication.PublishText(pinReady, "ready"); await Task.Delay(TimeSpan.FromSeconds(30)); return;
}

if (args is ["--snapshot-child", var recordingToSnapshot, var sharedSnapshotRoot, var snapshotResult])
{
    FixturePublication.PublishText(snapshotResult, StudioReplaySnapshotCache.Capture(recordingToSnapshot, sharedSnapshotRoot, default));
    Console.WriteLine("frame=1"); return;
}
if (args is ["--stdio-child", var childLoggingDirectory])
{
    var observation = StdioFixtureObservation.TryCreate(childLoggingDirectory);
    observation?.Record("Started");
    try
    {
        using var log = new ReplayExportWorkerLog(Path.Combine(childLoggingDirectory, "worker.log"));
        Console.SetOut(log); Console.SetError(log);
        FixturePublication.PublishText(Path.Combine(childLoggingDirectory, "ready"), "ready");
        observation?.Record("Ready");
        var parentExit = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(childLoggingDirectory, "parent-exited")) && parentExit.Elapsed < TimeSpan.FromSeconds(10))
        { Console.WriteLine("waiting independently for launcher exit"); await Task.Delay(10); }
        if (parentExit.Elapsed >= TimeSpan.FromSeconds(10)) throw new TimeoutException("Launcher exit was not observed.");
        observation?.Record("ParentExitSignalSeen");
        for (int i = 0; i < 100; i++)
        {
            Console.WriteLine(new string('x', 500)); Console.Error.WriteLine("teardown progress " + i);
            if (i is 0 or 25 or 50 or 75 or 99) observation?.Record("TeardownProgress", i);
            await Task.Delay(10);
        }
        FixturePublication.PublishText(Path.Combine(childLoggingDirectory, "complete"), "complete");
        observation?.Record("CompletePublished"); return;
    }
    catch (Exception ex) { observation?.Record("Threw", error: ex); throw; }
    finally { observation?.Record("ManagedFinallyReached"); }
}
if (args is ["--stdio-parent", var loggingDirectoryForParent])
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--stdio-child"); start.ArgumentList.Add(loggingDirectoryForParent);
    using var process = Process.Start(start)!;
    var ready = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(loggingDirectoryForParent, "ready")) && ready.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
    if (ready.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Independent logging child did not start.");
    FixturePublication.PublishText(Path.Combine(loggingDirectoryForParent, "parent-pid"), Environment.ProcessId.ToString());
    Console.WriteLine("frame=1"); return;
}
if (args is ["--coordinator-child", var stateFile, var cancelFile, var jobId, var peerDirectory])
{
    Guid id = Guid.Parse(jobId);
    void Publish(string state)
    {
        using var process = Process.GetCurrentProcess();
        StudioReplayStatusFile.Write(stateFile, new StudioReplayExportStatus(id, state,
            state == "Complete" ? 2 : 0, 2, null, Path.GetDirectoryName(stateFile)!, process.Id,
            process.StartTime.ToUniversalTime().Ticks, WorkerIdentity: ReplayExportWorkerIdentity.Capture(process)));
    }
    int maximum = 0;
    void ObserveChildren()
    {
        int live = 0;
        foreach (string peer in Directory.EnumerateFiles(peerDirectory, "status.json", SearchOption.AllDirectories))
        {
            try
            {
                var status = StudioReplayStatusFile.Read(peer);
                if (status is not { WorkerProcessId: { } processId }) continue;
                using var process = Process.GetProcessById(processId);
                if (ReplayExportWorkerIdentity.Matches(process, status.WorkerIdentity, status.WorkerStartUtcTicks)) live++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        if (live <= maximum) return;
        maximum = live;
        string destination = Path.Combine(Path.GetDirectoryName(stateFile)!, "maximum-live-children");
        FixturePublication.PublishText(destination, maximum.ToString());
    }
    Publish("Rendering");
    string release = Path.Combine(Path.GetDirectoryName(stateFile)!, "fixture-complete");
    var readyDeadline = Stopwatch.StartNew();
    while (true)
    {
        ObserveChildren();
        if (File.Exists(cancelFile)) { Publish("Cancelled"); return; }
        if (File.Exists(release)) break;
        if (readyDeadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Coordinator fixture was not explicitly released.");
        await Task.Delay(20);
    }
    Publish("Complete");
    for (int i = 0; i < 8; i++) { ObserveChildren(); await Task.Delay(20); }
    return;
}

if (args.Contains("--encoder-child"))
{
    Console.WriteLine("frame=17");
    for (int i = 0; i < 500; i++) Console.Error.WriteLine(new string('x', 100));
    if (args.Contains("--hold")) await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}
int checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
int evidenceAt = Array.IndexOf(args, "--coordinator-evidence");
string? coordinatorEvidence = evidenceAt >= 0 ? Path.GetFullPath(args[evidenceAt + 1]) : null;
if (coordinatorEvidence != null) Directory.CreateDirectory(coordinatorEvidence);
string root = Path.Combine(Path.GetTempPath(), "prime-offline-export-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string sourceRecording = Path.Combine(root, "external.ppdemo"), snapshotRoot = Path.Combine(root, "private-sources");
    byte[] sourceBytes = Enumerable.Range(0, 300000).Select(i => (byte)(i * 13)).ToArray(); File.WriteAllBytes(sourceRecording, sourceBytes);
    string stableRecording = StudioReplaySnapshotCache.Capture(sourceRecording, snapshotRoot, default);
    Check(Path.GetDirectoryName(stableRecording) == Path.Combine(snapshotRoot, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes)).ToLowerInvariant())
        && File.ReadAllBytes(stableRecording).AsSpan().SequenceEqual(sourceBytes), "private replay snapshots use the exact SHA identity and preserve all streamed recording bytes");
    Check(StudioReplaySnapshotCache.Capture(sourceRecording, snapshotRoot, default) == stableRecording
        && Directory.GetDirectories(snapshotRoot).Length == 1, "reopening the same source reuses its immutable bytes instead of accumulating GUID recordings");
    File.WriteAllBytes(sourceRecording, [1, 2, 3]); string changedRecording = StudioReplaySnapshotCache.Capture(sourceRecording, snapshotRoot, default); File.Delete(sourceRecording);
    Check(changedRecording != stableRecording && File.ReadAllBytes(stableRecording).AsSpan().SequenceEqual(sourceBytes)
        && File.ReadAllBytes(changedRecording).AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }), "queued private source survives external recording replacement and deletion without reusing changed bytes");
    using (var cancelledCapture = new CancellationTokenSource())
    {
        cancelledCapture.Cancel(); bool captureStopped = false;
        try { StudioReplaySnapshotCache.Capture(stableRecording, Path.Combine(root, "cancelled-sources"), cancelledCapture.Token); } catch (OperationCanceledException) { captureStopped = true; }
        Check(captureStopped && !Directory.Exists(Path.Combine(root, "cancelled-sources")), "cancelled immutable capture leaves no partial promoted recording or staging directory");
    }
    File.WriteAllBytes(changedRecording, [4]); bool corruptSnapshot = false;
    File.WriteAllBytes(sourceRecording, [1, 2, 3]);
    try { StudioReplaySnapshotCache.Capture(sourceRecording, snapshotRoot, default); } catch (InvalidDataException) { corruptSnapshot = true; }
    Check(corruptSnapshot && File.ReadAllBytes(changedRecording).AsSpan().SequenceEqual(new byte[] { 4 }), "a corrupted content-addressed snapshot is rejected without replacing bytes held by another owner");
    await CacheRetentionChecks.Run(root, Check);
    await WorkerIdentityChecks.Run(root, Check);
    await PublicationChecks.RunAsync(root, Check, coordinatorEvidence);
    foreach (int fps in new[] { 24, 30, 48, 60, 90, 120, 144 })
    {
        var sampler = new ReplayExportSampler(120, 180, fps);
        Check(sampler.Count == fps + 1, "inclusive 60 Hz range preserves output sample count");
        for (long i = 0; i < sampler.Count; i++)
        {
            var sample = sampler.At(i);
            Check(Math.Abs(sample.Frame - (120 + i * 60d / fps)) < 1e-10, "fractional sample is exact rational presentation time");
            Check(sample.SimulationFrame == (uint)Math.Ceiling(sample.Frame), "simulation remains canonical 60 Hz at every output rate");
        }
        Check(StudioOfflineAudio.SampleCount(sampler.Count, fps) == (sampler.Count * 48000 + fps - 1) / fps, "PCM duration includes the final video frame without drift");
    }
    var constant = new StudioPcmAudio(48000, 1, Enumerable.Repeat(.5f, 48000).ToArray());
    AudioAuthoringChecks.Run(root, Check);
    string mixed = Path.Combine(root, "mixed.wav");
    StudioOfflineAudio.WriteWave(mixed, 60, 60, 60,
        [new(60, StudioAudioBus.Game, constant), new(90, StudioAudioBus.Combat, constant)], new(Game: .5f, Combat: .5f));
    var read = StudioPcmAudio.ReadWave(mixed);
    Check(read.SampleRate == 48000 && read.Channels == 2 && read.Frames == 48000, "streamed PCM16 stereo WAV has the exact video duration");
    Check(Math.Abs(read.Samples[0] - .25) < .0001 && Math.Abs(read.Samples[48000] - .5) < .0001, "event start and per-bus gains mix at exact replay frame offsets");
    string repeated = Path.Combine(root, "repeat.wav");
    StudioOfflineAudio.WriteWave(repeated, 60, 60, 60,
        [new(60, StudioAudioBus.Game, constant), new(90, StudioAudioBus.Combat, constant)], new(Game: .5f, Combat: .5f));
    Check(File.ReadAllBytes(mixed).AsSpan().SequenceEqual(File.ReadAllBytes(repeated)), "offline mix is byte deterministic");
    StudioOfflineAudio.WriteWave(repeated, 60, 60, 60, [new(0, StudioAudioBus.Music, constant, Loop: true)], new(Music: .25f));
    Check(Math.Abs(StudioPcmAudio.ReadWave(repeated).Samples[0] - .125) < .0001, "looping music continues across selected in point with independent volume");
    var lowerRate = new StudioPcmAudio(24000, 1, Enumerable.Repeat(.5f, 24000).ToArray());
    StudioOfflineAudio.WriteWave(repeated, 0, 60, 60, [new(0, StudioAudioBus.Replay, lowerRate)], new(Replay: .5f));
    Check(Math.Abs(StudioPcmAudio.ReadWave(repeated).Samples[95998] - .25) < .0001, "source sample rate converts deterministically without shortening the tail");
    var pcm8 = StudioPcmAudio.FromMonoPcm(new byte[] { 0, 128, 255 }, 24000, false);
    Check(pcm8.Samples[0] == -1 && pcm8.Samples[1] == 0 && pcm8.Samples[2] == 127 / 128f, "canonical decoded unsigned PCM8 maps to signed floating samples exactly");
    var pcm16 = StudioPcmAudio.FromMonoPcm(new byte[] { 0, 128, 0, 0, 255, 127 }, 24000, true);
    Check(pcm16.Samples[0] == -1 && pcm16.Samples[1] == 0 && pcm16.Samples[2] == 32767 / 32768f, "canonical decoded little-endian PCM16 maps without byte-order or sign drift");
    StudioOfflineAudio.WriteWave(repeated, 0, 60, 60, [new(0, StudioAudioBus.Game, constant, Pan: 1, Pitch: 2)]);
    var pitched = StudioPcmAudio.ReadWave(repeated);
    Check(pitched.Samples[0] == 0 && Math.Abs(pitched.Samples[1] - .5) < .0001, "script pan applies to the correct stereo channel");
    Check(pitched.Samples[48001] == 0, "script pitch advances source time and ends at the correct output sample");
    StudioOfflineAudio.WriteWave(repeated, 0, 60, 60, [new(0, StudioAudioBus.Game, constant, Loop: true, EndFrame: 30)]);
    var stopped = StudioPcmAudio.ReadWave(repeated);
    Check(stopped.Samples[47999] != 0 && stopped.Samples[48000] == 0, "canonical script stop event trims a loop at the exact 60 Hz boundary");
    string secret = "very-private-community-credential";
    string diagnostic = StudioDiagnosticRedaction.Serialize(new { Build = "1.2.3", Map = "room", Track = "password=" + secret,
        Details = new { AccessToken = secret, RefreshToken = secret, Message = "Bearer " + secret },
        Jwt = "abcdefghijk.abcdefghijk.abcdefghijk", Url = "https://user:" + secret + "@example.invalid/path",
        Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "replay.ppdemo") });
    Check(!diagnostic.Contains(secret) && !diagnostic.Contains("abcdefghijk") && !diagnostic.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        "diagnostic allowlist sanitization removes nested auth fields, embedded credentials, JWTs and private home paths");
    Check(diagnostic.Contains("1.2.3") && diagnostic.Contains("room") && diagnostic.Contains("example.invalid/path"), "redaction preserves relevant build/map/endpoint diagnostic facts");
    Check(!StudioDiagnosticRedaction.Redact("password='quoted private phrase' refresh_token=another-secret")!.Contains("private phrase"), "quoted credentials are redacted in full, including embedded spaces");
    void Archive(string destination, params (string Name, int Attributes, string Content)[] entries)
    {
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var member = zip.CreateEntry(entry.Name); member.ExternalAttributes = entry.Attributes;
            using var writer = new StreamWriter(member.Open()); writer.Write(entry.Content);
        }
    }
    string portable = Path.Combine(root, "portable.zip"), extract = Path.Combine(root, "portable");
    Archive(portable, ("manifest.json", 0, "{}"), ("replay.ppdemo", 0, "exact replay bytes"), ("map.ppmap", 0, "exact custom map bytes"));
    StudioPortableArchive.Extract(portable, extract, default);
    Check(File.ReadAllText(Path.Combine(extract, "replay.ppdemo")) == "exact replay bytes" && File.ReadAllText(Path.Combine(extract, "map.ppmap")) == "exact custom map bytes", "bounded portable archive extraction preserves every replay and package byte");
    foreach (var attack in new[] { (Name: "../escape", Attributes: 0), (Name: "replay.ppdemo", Attributes: 0), (Name: "map.ppmap", Attributes: unchecked((int)0xa0000000)) })
    {
        string malicious = Path.Combine(root, "malicious" + Guid.NewGuid().ToString("N") + ".zip");
        Archive(malicious, ("manifest.json", 0, "{}"), ("replay.ppdemo", 0, "recording"), (attack.Name, attack.Attributes, "target"));
        string target = Path.Combine(root, "rejected" + Guid.NewGuid().ToString("N")); bool rejected = false;
        try { StudioPortableArchive.Extract(malicious, target, default); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && !Directory.EnumerateFileSystemEntries(target).Any() && !File.Exists(Path.Combine(root, "escape")), "portable archive preflight rejects traversal, duplicates or symbolic links before extraction");
    }
    string oversized = Path.Combine(root, "oversized.zip");
    Archive(oversized, ("manifest.json", 0, new string('x', 512 * 1024 + 1)), ("replay.ppdemo", 0, "recording"));
    bool budget = false; try { StudioPortableArchive.Extract(oversized, Path.Combine(root, "oversized"), default); } catch (InvalidDataException) { budget = true; }
    Check(budget, "portable sidecar expansion remains bounded even when ZIP compression is very high");
    using (var cancellation = new CancellationTokenSource())
    {
        byte[] before = File.ReadAllBytes(repeated); cancellation.Cancel(); bool observed = false;
        try { StudioOfflineAudio.WriteWave(repeated, 0, 60, 60, [], cancellation: cancellation.Token); }
        catch (OperationCanceledException) { observed = true; }
        Check(observed && before.AsSpan().SequenceEqual(File.ReadAllBytes(repeated)), "cancelled audio leaves prior output intact");
    }
    string executable = Environment.ProcessPath!;
    string snapshotPrefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? "\"" + Assembly.GetExecutingAssembly().Location + "\" " : "";
    string sharedSources = Path.Combine(root, "cross-process sources"), firstResult = Path.Combine(root, "first-snapshot"), secondResult = Path.Combine(root, "second-snapshot");
    string firstCaptureDirectory = Path.Combine(root, "first-capture"), secondCaptureDirectory = Path.Combine(root, "second-capture");
    Directory.CreateDirectory(firstCaptureDirectory); Directory.CreateDirectory(secondCaptureDirectory);
    using (var firstCapture = new ReplayEncoderJob(executable, snapshotPrefix + "--snapshot-child \"" + stableRecording + "\" \"" + sharedSources + "\" \"" + firstResult + "\"", firstCaptureDirectory))
    using (var secondCapture = new ReplayEncoderJob(executable, snapshotPrefix + "--snapshot-child \"" + stableRecording + "\" \"" + sharedSources + "\" \"" + secondResult + "\"", secondCaptureDirectory))
    {
        var results = await Task.WhenAll(firstCapture.Completion, secondCapture.Completion).WaitAsync(TimeSpan.FromSeconds(10));
        Check(results.All(result => result.ExitCode == 0), "both snapshot children exit successfully: " + string.Join(" | ", results.Select(result => result.Error + " " + result.Stderr)));
        string first = File.ReadAllText(firstResult), second = File.ReadAllText(secondResult);
        Check(results.All(result => result.ExitCode == 0) && first == second && Directory.GetDirectories(sharedSources).Length == 1
            && File.ReadAllBytes(first).AsSpan().SequenceEqual(sourceBytes), "two actual preparation processes share one exactly hashed immutable recording through the production cache lease");
    }
    string child = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? "\"" + Assembly.GetExecutingAssembly().Location + "\" --encoder-child" : "--encoder-child";
    using (var encoder = new ReplayEncoderJob(executable, child, root))
    {
        var result = await encoder.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(result.ExitCode == 0 && !result.Cancelled && encoder.Frames == 17, "canonical owned encoder job drains progress and completes a real child process");
        encoder.Dispose();
        await Task.Delay(50);
        encoder.Dispose(); encoder.Cancel();
        Check(encoder.Completion.IsCompletedSuccessfully && encoder.Completion.Result.ExitCode == 0,
            "encoder completion remains valid through repeated native teardown disposal and late cancellation");
        Check(result.Stderr.Length <= 16384, "canonical encoder diagnostics remain bounded under noisy child stderr");
    }
    using (var encoder = new ReplayEncoderJob(executable, child + " --hold", root))
    {
        await Task.Delay(150); encoder.Cancel();
        var result = await encoder.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Check(result.Cancelled, "cancellation terminates and waits the owned child independently of UI pumping");
    }
    string loggingDirectory = Path.Combine(root, "durable worker logging"); Directory.CreateDirectory(loggingDirectory);
    string loggingPrefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? "\"" + Assembly.GetExecutingAssembly().Location + "\" " : "";
    try
    {
        using (var loggingParent = new ReplayEncoderJob(executable, loggingPrefix + "--stdio-parent \"" + loggingDirectory + "\"", root))
        {
            var started = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(loggingDirectory, "parent-pid")) && started.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            int parentId = int.Parse(FixturePublication.ReadText(Path.Combine(loggingDirectory, "parent-pid")));
            bool parentExited = false;
            try { using var parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); parentExited = parent.HasExited; }
            catch (ArgumentException) { parentExited = true; }
            Check(parentId != Environment.ProcessId && parentExited && !File.Exists(Path.Combine(loggingDirectory, "complete")), "actual intermediate launcher exits while its independently logging child remains alive");
            FixturePublication.PublishText(Path.Combine(loggingDirectory, "parent-exited"), "verified parent exit");
            var result = await loggingParent.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Check(result.ExitCode == 0, "launcher diagnostic pipe draining completes after descendant inherited handles close");
        }
        var logDeadline = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(loggingDirectory, "complete")) && logDeadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Check(File.Exists(Path.Combine(loggingDirectory, "complete")), "worker-owned diagnostic writer survives actual parent process exit through final teardown log");
        string retainedLog = Path.Combine(loggingDirectory, "worker.log");
        Check(new FileInfo(retainedLog).Length <= 65536 && File.ReadAllText(retainedLog).Contains("teardown progress 99"), "worker diagnostics retain a bounded durable tail without parent-owned console pipes");
    }
    catch (Exception ex) { StdioFixtureObservation.RetainFailure(loggingDirectory, ex); throw; }
    string coordinatorRoot = Path.Combine(root, "workers"); Directory.CreateDirectory(coordinatorRoot);
    int MaximumLiveChildren() => Directory.EnumerateFiles(coordinatorRoot, "maximum-live-children", SearchOption.AllDirectories)
        .Select(path => int.Parse(FixturePublication.ReadText(path))).DefaultIfEmpty(0).Max();
    async Task LaunchFixture(string path)
    {
        // A deliberately slow launch reproduces the cold-start case which a
        // fixed 300 ms assertion could inspect before either child existed.
        await Task.Delay(600);
        var ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true })!;
        string prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? "\"" + Assembly.GetExecutingAssembly().Location + "\" " : "";
        string peers = Path.GetDirectoryName(Path.GetDirectoryName(path)!)!;
        using var job = new ReplayEncoderJob(executable, prefix + "--coordinator-child \"" + ticket.StatusFile + "\" \"" + ticket.CancelFile + "\" " + ticket.Id + " \"" + peers + "\"", Path.GetDirectoryName(path)!);
        var result = await job.Completion; if (result.ExitCode != 0) throw new IOException(result.Error);
    }
    string Ticket(int index)
    {
        string directory = Path.Combine(coordinatorRoot, index.ToString()); Directory.CreateDirectory(directory);
        var ticket = new StudioReplayExportTicket(Guid.NewGuid(), "fixture.ppdemo", directory, new(directory, 0, 1), Path.Combine(directory, "status.json"), Path.Combine(directory, "cancel"),
            new Dictionary<string, string>(), [], [], "AMHE1", "AMFE0");
        string file = Path.Combine(directory, "ticket.json"); File.WriteAllText(file, JsonSerializer.Serialize(ticket, new JsonSerializerOptions { IncludeFields = true })); return file;
    }
    static string State(string path) => StudioReplayStatusFile.Read(Path.Combine(Path.GetDirectoryName(path)!, "status.json"))!.State;
    static StudioReplayExportStatus? FixtureStatus(string path)
    {
        try { return StudioReplayStatusFile.Read(Path.Combine(Path.GetDirectoryName(path)!, "status.json")); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }
    static bool HasLiveRenderingChild(string path)
    {
        if (FixtureStatus(path) is not { State: "Rendering", WorkerProcessId: { } id } status) return false;
        try { using var process = Process.GetProcessById(id); return ReplayExportWorkerIdentity.Matches(process, status.WorkerIdentity, status.WorkerStartUtcTicks); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    static void ReleaseFixtures(IEnumerable<string> paths)
    {
        foreach (string path in paths) FixturePublication.PublishText(Path.Combine(Path.GetDirectoryName(path)!, "fixture-complete"), "complete");
    }
    async Task WaitForFixture(Func<bool> ready, StudioJobManager manager, string[] paths, string description)
    {
        var deadline = Stopwatch.StartNew();
        while (!ready() && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (MaximumLiveChildren() > 2) throw new InvalidOperationException("More than two actual export child processes were observed.");
            await Task.Delay(20);
        }
        string details = $"jobs={manager.Jobs.Count}, queued={manager.Jobs.Count(j => j.Progress.Detail?.StartsWith("Queued") == true)}, maxLive={MaximumLiveChildren()}, "
            + string.Join(", ", paths.Select(path => $"{Path.GetFileName(Path.GetDirectoryName(path))}:{FixtureStatus(path)?.State ?? "no status"}/pid={FixtureStatus(path)?.WorkerProcessId?.ToString() ?? "none"}"));
        Check(ready(), description + ": " + details);
    }
    void RetainCoordinatorEvidence(string name, StudioJobManager manager, string[] paths, TimeSpan? acknowledgedAfter = null)
    {
        if (coordinatorEvidence == null) return;
        var report = new
        {
            LegacyTimedCheckpointMilliseconds = 300,
            InjectedMinimumLauncherDelayMilliseconds = 600,
            LegacyTimedCheckpointGuaranteesReadiness = false,
            AcknowledgedAfterMilliseconds = acknowledgedAfter?.TotalMilliseconds,
            MaximumObservedActualChildren = MaximumLiveChildren(),
            LiveRenderingChildren = paths.Count(HasLiveRenderingChild),
            QueuedJobs = manager.Jobs.Count(job => job.Progress.Detail?.StartsWith("Queued") == true),
            Jobs = manager.Jobs.Select(job => new { job.Id, job.State, job.Progress, job.Error }).ToArray(),
            Workers = paths.Select(path => new { Ticket = Path.GetFileName(Path.GetDirectoryName(path)), Status = FixtureStatus(path), Live = HasLiveRenderingChild(path) }).ToArray(),
            ChildObservations = Directory.EnumerateFiles(coordinatorRoot, "maximum-live-children", SearchOption.AllDirectories)
                .Select(path => new { Ticket = Path.GetFileName(Path.GetDirectoryName(path)), MaximumActualChildren = int.Parse(FixturePublication.ReadText(path)) }).ToArray()
        };
        File.WriteAllText(Path.Combine(coordinatorEvidence, name + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }
    await using (var jobs = new StudioJobManager())
    {
        var coordinator = new ReplayExportWorkerCoordinator(jobs, LaunchFixture);
        var paths = Enumerable.Range(0, 5).Select(Ticket).ToArray();
        var startAcknowledgement = Stopwatch.StartNew();
        var pending = paths.Select(coordinator.LaunchAsync).ToArray();
        try
        {
            await WaitForFixture(() => jobs.Jobs.Count == 5 && jobs.Jobs.Count(j => j.Progress.Detail?.StartsWith("Queued") == true) >= 3
                && paths.Count(HasLiveRenderingChild) == 2 && MaximumLiveChildren() == 2, jobs, paths,
                "central jobs retain three observable queued exports while two acknowledged actual children wait for explicit release");
            RetainCoordinatorEvidence("cold-start-acknowledged", jobs, paths, startAcknowledgement.Elapsed);
            if (OperatingSystem.IsLinux())
            {
                var witnesses = paths.Select(FixtureStatus).Where(status => status?.WorkerProcessId != null).Select(status =>
                {
                    using var process = Process.GetProcessById(status!.WorkerProcessId!.Value);
                    return new { process.Id, ChildUtcTicks = status.WorkerStartUtcTicks, ObserverUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                        ExactKernelIdentityMatches = ReplayExportWorkerIdentity.Matches(process, status.WorkerIdentity), status.WorkerIdentity };
                }).ToArray();
                Console.WriteLine("Linux cross-process identity witness: " + JsonSerializer.Serialize(witnesses));
                if (coordinatorEvidence != null) File.WriteAllText(Path.Combine(coordinatorEvidence, "linux-process-identity-witness.json"), JsonSerializer.Serialize(witnesses, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { ReleaseFixtures(paths); await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(15)); }
        Check(MaximumLiveChildren() == 2 && paths.All(p => State(p) == "Complete") && jobs.Jobs.All(j => j.State == StudioJobState.Completed), "all queued actual child workers finish independently with central immutable progress history");
        RetainCoordinatorEvidence("all-queued-workers-complete", jobs, paths);
        string cancelled = Ticket(10); var stoppedTask = coordinator.LaunchAsync(cancelled);
        await WaitForFixture(() => HasLiveRenderingChild(cancelled), jobs, [cancelled], "explicit cancellation targets an acknowledged actual child rather than a pending cold launch");
        jobs.Jobs.Last().Cancel();
        try { await stoppedTask; } catch (OperationCanceledException) { }
        Check(State(cancelled) == "Cancelled" && jobs.Jobs.Last().State == StudioJobState.Cancelled, "explicit central cancellation writes the persisted signal and awaits actual child acknowledgment");
    }
    string continued = Ticket(20), continuedSecond = Ticket(21), queued = Ticket(22), queuedSecond = Ticket(23);
    var detachedJobs = new StudioJobManager();
    var detached = new ReplayExportWorkerCoordinator(detachedJobs, LaunchFixture);
    Task[] activeTasks = new[] { continued, continuedSecond }.Select(detached.LaunchAsync).ToArray();
    await WaitForFixture(() => HasLiveRenderingChild(continued) && HasLiveRenderingChild(continuedSecond), detachedJobs,
        [continued, continuedSecond], "both actual children acknowledge ownership before shutdown fixture queues excess work");
    Task[] queuedTasks = new[] { queued, queuedSecond }.Select(detached.LaunchAsync).ToArray();
    Task[] continuedTasks = activeTasks.Concat(queuedTasks).ToArray();
    await WaitForFixture(() => detachedJobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Queued") == true) == 2
        && FixtureStatus(queued)?.State == "Queued" && FixtureStatus(queuedSecond)?.State == "Queued", detachedJobs,
        [continued, continuedSecond, queued, queuedSecond], "excess tickets acknowledge their persisted queued state before shutdown");
    detached.DetachForShutdown(); await detachedJobs.DisposeAsync();
    await Task.WhenAll(continuedTasks).WaitAsync(TimeSpan.FromSeconds(2));
    Check(!File.Exists(Path.Combine(Path.GetDirectoryName(continued)!, "cancel")) && State(continued) != "Cancelled", "whole Studio shutdown detaches observation without cancelling an actual running child");
    Check(State(queued) == "Queued" && State(queuedSecond) == "Queued" && !File.Exists(Path.Combine(Path.GetDirectoryName(queued)!, "cancel")), "whole Studio close preserves excess queued tickets without launching or cancelling them");
    await using (var restoredJobs = new StudioJobManager())
    {
        var restored = new ReplayExportWorkerCoordinator(restoredJobs, LaunchFixture);
        restored.RestorePersisted(coordinatorRoot);
        await WaitForFixture(() => restoredJobs.Jobs.Count == 4 && State(queued) == "Queued" && State(queuedSecond) == "Queued"
            && HasLiveRenderingChild(continued) && HasLiveRenderingChild(continuedSecond) && MaximumLiveChildren() == 2
            && restoredJobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Queued") == true) == 2, restoredJobs,
            [continued, continuedSecond, queued, queuedSecond], "immediate Studio restart reserves both still-running child slots before resuming pending tickets");
        RetainCoordinatorEvidence("restart-live-workers-reserved", restoredJobs, [continued, continuedSecond, queued, queuedSecond]);
        ReleaseFixtures([continued, continuedSecond, queued, queuedSecond]);
        var deadline = Stopwatch.StartNew(); while (restoredJobs.Jobs.Any(j => j.State == StudioJobState.Running) && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
        Check(State(continued) == "Complete" && State(continuedSecond) == "Complete", "actual children persist terminal output after the original parent job manager has shut down");
        Check(State(queued) == "Complete" && State(queuedSecond) == "Complete" && MaximumLiveChildren() == 2, "new Studio coordinator rediscovers and resumes all persisted pending tickets without exceeding two actual children");
        RetainCoordinatorEvidence("detached-and-restored-complete", restoredJobs, [continued, continuedSecond, queued, queuedSecond]);
    }
    string historyRoot = Path.Combine(root, "finished-history"); Directory.CreateDirectory(historyRoot);
    string? laterQueued = null;
    for (int index = 0; index <= 256; index++)
    {
        string directory = Path.Combine(historyRoot, index.ToString("D4")); Directory.CreateDirectory(directory);
        Guid id = Guid.NewGuid();
        var ticket = new StudioReplayExportTicket(id, "fixture.ppdemo", directory, new(directory, 0, 1),
            Path.Combine(directory, "status.json"), Path.Combine(directory, "cancel"), new Dictionary<string, string>(), [], [], "AMHE1", "AMFE0");
        string ticketPath = Path.Combine(directory, "ticket.json"); File.WriteAllText(ticketPath, JsonSerializer.Serialize(ticket, new JsonSerializerOptions { IncludeFields = true }));
        File.WriteAllText(ticket.StatusFile, JsonSerializer.Serialize(new StudioReplayExportStatus(id, index == 256 ? "Queued" : "Complete", 0, 1, null, directory)));
        if (index == 256) laterQueued = ticketPath;
    }
    await using (var historyJobs = new StudioJobManager())
    {
        var historyCoordinator = new ReplayExportWorkerCoordinator(historyJobs, LaunchFixture);
        ReleaseFixtures([laterQueued!]);
        historyCoordinator.RestorePersisted(historyRoot);
        Check(historyJobs.Jobs.Count == 1, "256 completed historical exports do not consume the pending restoration budget");
        var deadline = Stopwatch.StartNew();
        while (historyJobs.Jobs.Any(job => job.State == StudioJobState.Running) && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Check(State(laterQueued!) == "Complete" && historyJobs.Jobs.Single().State == StudioJobState.Completed,
            "the later queued ticket resumes a real child after 256 terminal jobs are skipped");
    }
    int ffmpegAt = Array.IndexOf(args, "--ffmpeg"), ffprobeAt = Array.IndexOf(args, "--ffprobe");
    if (ffmpegAt >= 0 && ffprobeAt >= 0)
    {
        string ffmpeg = args[ffmpegAt + 1], ffprobe = args[ffprobeAt + 1];
        using (var images = new ReplayEncoderJob(ffmpeg, "-y -f lavfi -i testsrc2=size=64x64:rate=60 -frames:v 4 -start_number 0 frame_%08d.png", root))
        { var result = await images.Completion.WaitAsync(TimeSpan.FromSeconds(30)); Check(result.ExitCode == 0 && File.Exists(Path.Combine(root, "frame_00000003.png")), "real FFmpeg creates four codec fixture PNG inputs"); }
        StudioOfflineAudio.WriteWave(Path.Combine(root, "offline.wav"), 0, 4, 60, [new(0, StudioAudioBus.Game, constant)], new(Game: .5f));
        using (var mux = new ReplayEncoderJob(ffmpeg, "-y -framerate 60 -i frame_%08d.png -i offline.wav -c:a aac -shortest -c:v libx264 -pix_fmt yuv420p -progress pipe:1 replay.mp4", root))
        { var result = await mux.Completion.WaitAsync(TimeSpan.FromSeconds(30)); Check(result.ExitCode == 0 && mux.Frames == 4, "canonical owned encoder muxes actual PNG and production offline PCM streams"); }
        var start = new ProcessStartInfo(ffprobe) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in new[] { "-v", "error", "-count_frames", "-show_streams", "-of", "json", "replay.mp4" }) start.ArgumentList.Add(value);
        using var probe = Process.Start(start)!;
        Task<string> jsonTask = probe.StandardOutput.ReadToEndAsync(), errorTask = probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        string json = await jsonTask, error = await errorTask;
        Check(probe.ExitCode == 0, "independent real ffprobe decodes encoded export: " + error);
        using var info = JsonDocument.Parse(json);
        var streams = info.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");
        Check(video.GetProperty("codec_name").GetString() == "h264" && video.GetProperty("nb_read_frames").GetString() == "4" && video.GetProperty("avg_frame_rate").GetString() == "60/1", "decoded MP4 preserves all video frames at requested exact rate");
        Check(audio.GetProperty("codec_name").GetString() == "aac" && audio.GetProperty("sample_rate").GetString() == "48000" && audio.GetProperty("channels").GetInt32() == 2, "decoded MP4 carries the independent 48 kHz stereo offline mix");
    }
    Console.WriteLine($"PASS: {checks} canonical fractional sampling, PCM and encoder process assertions.");
}
finally { Directory.Delete(root, recursive: true); }
