using System.Diagnostics;
using System.Text.Json;
using ProjectPrime.Studio.Replay;

/// <summary>Bounded observations for the pure stdio fixture; observations never change its result.</summary>
internal sealed class StdioFixtureObservation(string directory)
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly int _processId = Environment.ProcessId;
    private readonly string? _identity = CurrentIdentity();
    private string? _lastPhase;
    private readonly List<object> _events = [];
    private int? _lastProgress;
    private string? _errorType;
    private string? _error;

    internal static StdioFixtureObservation? TryCreate(string directory)
    {
        try { return new StdioFixtureObservation(directory); }
        catch (Exception) { return null; }
    }

    internal void Record(string phase, int? progress = null, Exception? error = null)
    {
        // Five progress checkpoints, not one extra synchronous publication per log write.
        try
        {
            _lastPhase = phase; _lastProgress = progress ?? _lastProgress;
            if (_events.Count < 12) _events.Add(new { Phase = phase, Progress = progress, ElapsedMilliseconds = _elapsed.Elapsed.TotalMilliseconds });
            if (error != null) { _errorType = error.GetType().FullName; _error = Limit(error.ToString(), 8192); }
            Publish(Path.Combine(directory, "child-observation.json"), JsonSerializer.Serialize(new
            {
                Scope = "pure export stdio fixture observation only", ProcessId = _processId,
                WorkerIdentity = _identity, StartedUtc = _started, ElapsedMilliseconds = _elapsed.Elapsed.TotalMilliseconds,
                Phase = _lastPhase, LastProgress = _lastProgress, Events = _events, ErrorType = _errorType, Error = _error,
                ExitMarkerMeaning = "Self-reported managed finally only; not an observed OS exit code."
            }));
        }
        catch (Exception) { /* Observation cannot interrupt the original fixture. */ }
    }

    internal static void RetainFailure(string directory, Exception failure)
    {
        // Fixed names bound retention across repeated runs and fit the existing CI always-upload scope.
        try
        {
            string? runnerTemporary = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            string destination = Path.Combine(string.IsNullOrWhiteSpace(runnerTemporary) ? Path.GetTempPath() : runnerTemporary,
                "studio-ui", "export-logging");
            var files = new List<object>();
            Directory.CreateDirectory(destination);
            foreach (string name in new[] { "worker.log", "child-observation.json", "ready", "parent-pid", "parent-exited", "complete" })
            {
                string source = Path.Combine(directory, name);
                try
                {
                    if (!File.Exists(source))
                    {
                        string oldCopy = Path.Combine(destination, name);
                        if (File.Exists(oldCopy)) File.Delete(oldCopy);
                        files.Add(new { Name = name, Exists = false }); continue;
                    }
                    using var snapshot = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    long originalLength = snapshot.Length;
                    int length = (int)Math.Min(originalLength, 65536);
                    snapshot.Seek(-length, SeekOrigin.End);
                    byte[] bytes = new byte[length]; snapshot.ReadExactly(bytes);
                    string copy = Path.Combine(destination, name);
                    PublishBytes(copy, bytes);
                    files.Add(new { Name = name, Exists = true, OriginalBytes = originalLength, RetainedBytes = length });
                }
                catch (Exception ex) { files.Add(new { Name = name, ObservationError = Limit(ex.Message, 1024) }); }
            }
            string? presence = null;
            try
            {
                string observation = Path.Combine(directory, "child-observation.json");
                if (File.Exists(observation))
                {
                    using var value = JsonDocument.Parse(FixturePublication.ReadText(observation));
                    int processId = value.RootElement.GetProperty("ProcessId").GetInt32();
                    string? identity = value.RootElement.GetProperty("WorkerIdentity").GetString();
                    try
                    {
                        using var process = Process.GetProcessById(processId);
                        presence = ReplayExportWorkerIdentity.Assess(process, identity).ToString();
                    }
                    catch (ArgumentException) { presence = "Exited (PID absent at observation)"; }
                }
            }
            catch (Exception ex) { presence = "Observation unavailable: " + Limit(ex.Message, 1024); }
            string report = JsonSerializer.Serialize(new
            {
                Scope = "pure export stdio fixture failure; no native/UI acceptance", CapturedUtc = DateTime.UtcNow,
                ActualGithubSha = EnvironmentValue("GITHUB_SHA"), ActualGithubRunId = EnvironmentValue("GITHUB_RUN_ID"),
                FailureType = failure.GetType().FullName, Failure = Limit(failure.ToString(), 8192),
                OriginalCompletionDeadlineSeconds = 5, OriginalLogIterations = 100, OriginalIterationDelayMilliseconds = 10,
                ChildPresenceAtFailure = presence, ActualChildExitCode = (int?)null,
                ActualChildExitCodeMeaning = "Unavailable; a self-reported child-finally marker is not an OS exit code.", Files = files
            });
            Publish(Path.Combine(destination, "failure-observation.json"), report);
            Console.Error.WriteLine("Stdio fixture failure observation: " + report);
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine("Stdio fixture failure observation unavailable: " + Limit(ex.Message, 1024)); }
            catch (Exception) { }
        }
    }

    private static string? CurrentIdentity()
    {
        try { using var process = Process.GetCurrentProcess(); return ReplayExportWorkerIdentity.Capture(process); }
        catch (Exception) { return null; }
    }
    private static string? EnvironmentValue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value == null ? null : Limit(value, 128);
    }
    private static string Limit(string text, int limit) => text.Length <= limit ? text : text[^limit..];
    private static void Publish(string path, string text) => PublishBytes(path, System.Text.Encoding.UTF8.GetBytes(text));
    private static void PublishBytes(string path, byte[] bytes)
    {
        string staging = path + ".observation." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(staging, bytes);
            if (OperatingSystem.IsWindows() && File.Exists(path)) File.Replace(staging, path, null);
            else File.Move(staging, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (Exception) { /* An observation cannot mask the original assertion or cleanup. */ }
        }
    }
}
