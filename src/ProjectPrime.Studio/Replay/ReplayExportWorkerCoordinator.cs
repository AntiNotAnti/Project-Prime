using System.Text.Json;
using System.Diagnostics;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Jobs;

namespace ProjectPrime.Studio.Replay;

/// <summary>One Studio window owns observable exports; persisted native children continue after that window exits.</summary>
public sealed class ReplayExportWorkerCoordinator(StudioJobManager jobs, Func<string, Task> launchWorker)
{
    private readonly SemaphoreSlim _slots = new(2);
    private readonly CancellationTokenSource _observationStop = new();
    private readonly object _gate = new();
    private readonly HashSet<Guid> _observed = [];
    private bool _detaching;
    public Task LaunchAsync(string ticketPath) => ObserveAsync(ticketPath, launch: true);
    /// <summary>Call before disposing the window's job manager. No cancellation signal is sent to a child.</summary>
    public void DetachForShutdown()
    {
        lock (_gate) { if (_detaching) return; _detaching = true; }
        _observationStop.Cancel();
    }
    /// <summary>Queued tickets resume; live worker statuses are observed without starting another writer.</summary>
    public void RestorePersisted(string exportsDirectory)
    {
        if (!Directory.Exists(exportsDirectory)) return;
        var pending = new List<(string Path, bool Launch)>();
        foreach (string path in Directory.EnumerateDirectories(exportsDirectory).Take(4096).OrderBy(directory => directory, StringComparer.Ordinal)
            .Select(directory => Path.Combine(directory, "ticket.json")).Where(File.Exists))
        {
            try
            {
                var ticket = ReadTicket(path);
                var state = ReadStatus(ticket);
                if (state?.State is "Complete" or "Failed" or "Cancelled" && !IsRetainedWorkerAlive(state)) continue;
                if (pending.Count == 256) break;
                pending.Add((path, state == null || state.State == "Queued"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        }
        // Existing children reserve their slots synchronously before any queued
        // work is submitted. A fast restart must not add two new children beside
        // the two which are still rendering from the previous Studio process.
        foreach (var item in pending.OrderBy(item => item.Launch))
        {
            Task task = ObserveAsync(item.Path, item.Launch);
            _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
    private Task ObserveAsync(string path, bool launch)
    {
        var ticket = ReadTicket(path);
        bool reserved;
        IDisposable? references = null;
        lock (_gate)
        {
            if (_detaching) return Task.CompletedTask;
            if (!_observed.Add(ticket.Id)) return Task.CompletedTask;
            reserved = !launch && _slots.Wait(0);
        }
        int slotHeld = reserved ? 1 : 0;
        Task observation = jobs.RunAsync("Replay export " + Path.GetFileName(ticket.Request.OutputName), async (progress, cancellation) =>
        {
            using var persistedCancellation = new CancellationTokenSource();
            using var cancelSignal = new Timer(_ =>
            {
                if (File.Exists(ticket.CancelFile))
                    try { persistedCancellation.Cancel(); } catch (ObjectDisposedException) { }
            }, null, 0, 100);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _observationStop.Token, persistedCancellation.Token);
            using var cancel = cancellation.Register(() =>
            {
                try { File.WriteAllText(ticket.CancelFile, "cancel"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            });
            bool acquired = reserved;
            try
            {
                if (launch) WriteStatus(ticket, new(ticket.Id, "Queued", 0, 0, null, ticket.Request.Directory));
                progress.Report(new(0, launch ? "Queued · waiting for an export slot" : "Observing existing export worker"));
                if (File.Exists(ticket.CancelFile)) persistedCancellation.Cancel();
                CancellationToken acquisition = launch ? wait.Token : _observationStop.Token;
                acquisition.ThrowIfCancellationRequested();
                var retained = ReadStatus(ticket);
                if (retained?.OriginCaptured != true && retained?.State is not ("Complete" or "Failed" or "Cancelled"))
                    references = StudioReplayCachePins.PinReferences(ticket, acquisition);
                if (!acquired) { await _slots.WaitAsync(acquisition).ConfigureAwait(false); acquired = true; Interlocked.Exchange(ref slotHeld, 1); }
                if (_observationStop.IsCancellationRequested) return true;
                Task? child = null;
                if (launch)
                {
                    if (File.Exists(ticket.CancelFile)) persistedCancellation.Cancel();
                    wait.Token.ThrowIfCancellationRequested();
                    WriteStatus(ticket, new(ticket.Id, "Worker starting", 0, 0, null, ticket.Request.Directory));
                    child = launchWorker(path);
                    _ = child.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                }
                while (true)
                {
                    if (_observationStop.IsCancellationRequested) return true;
                    var status = ReadStatus(ticket);
                    if (status?.OriginCaptured == true) references?.Dispose();
                    progress.Report(new(status is { TotalFrames: > 0 } ? status.Frames / (double)status.TotalFrames : 0,
                        status == null ? "Waiting for worker status" : status.State + $" · {status.Frames}/{status.TotalFrames} frames"));
                    if (status?.State is "Complete" or "Cancelled" or "Failed")
                    {
                        if (child != null) await child.ConfigureAwait(false);
                        else await WaitForRetainedWorkerExitAsync(status).ConfigureAwait(false);
                        if (status.State == "Failed") throw new IOException(status.Error ?? "Replay export failed.");
                        if (status.State == "Cancelled") throw new OperationCanceledException(cancellation);
                        return true;
                    }
                    if (child?.IsCompleted == true)
                    {
                        await child.ConfigureAwait(false);
                        throw new IOException("Export worker exited without a terminal result.");
                    }
                    if (child == null && File.Exists(ticket.StatusFile)
                        && DateTime.UtcNow - File.GetLastWriteTimeUtc(ticket.StatusFile) > TimeSpan.FromMinutes(2))
                        throw new IOException("The persisted export worker stopped updating; its ticket and partial output are retained for review.");
                    // Cancellation sends a signal, then continues observing until the child acknowledges it.
                    await Task.Delay(100, _observationStop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_observationStop.IsCancellationRequested) { return true; }
            catch (OperationCanceledException)
            {
                if (ReadStatus(ticket)?.State != "Cancelled")
                    WriteStatus(ticket, new(ticket.Id, "Cancelled", 0, 0, null, ticket.Request.Directory));
                throw;
            }
            catch (Exception ex)
            { WriteStatus(ticket, new(ticket.Id, "Failed", 0, 0, ex.Message, ticket.Request.Directory)); throw; }
            finally { if (Interlocked.Exchange(ref slotHeld, 0) == 1) _slots.Release(); }
        }, continueOnShutdown: true);
        return ReleaseReferencesAsync();
        async Task ReleaseReferencesAsync()
        {
            try { await observation.ConfigureAwait(false); }
            finally
            {
                references?.Dispose();
                if (Interlocked.Exchange(ref slotHeld, 0) == 1) _slots.Release();
            }
        }
    }
    private async Task WaitForRetainedWorkerExitAsync(StudioReplayExportStatus status)
    {
        if (status is not { WorkerProcessId: { } id, WorkerStartUtcTicks: { } start }) return;
        try
        {
            using var process = Process.GetProcessById(id);
            if (process.StartTime.ToUniversalTime().Ticks == start)
                await process.WaitForExitAsync(_observationStop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    private static bool IsRetainedWorkerAlive(StudioReplayExportStatus status)
    {
        if (status is not { WorkerProcessId: { } id, WorkerStartUtcTicks: { } start }) return false;
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == start;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static StudioReplayExportTicket ReadTicket(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 1024 * 1024) throw new InvalidDataException("Export ticket exceeds its size limit.");
        var ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(input, new JsonSerializerOptions { IncludeFields = true })
            ?? throw new InvalidDataException("Export ticket is empty.");
        StudioReplayCachePins.ValidateTicket(ticket);
        return ticket;
    }
    private static StudioReplayExportStatus? ReadStatus(StudioReplayExportTicket ticket)
    {
        try
        {
            if (!File.Exists(ticket.StatusFile) || new FileInfo(ticket.StatusFile).Length > 65536) return null;
            var state = JsonSerializer.Deserialize<StudioReplayExportStatus>(File.ReadAllText(ticket.StatusFile));
            return state?.Id == ticket.Id ? state : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    private static void WriteStatus(StudioReplayExportTicket ticket, StudioReplayExportStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ticket.StatusFile)!);
        string staging = ticket.StatusFile + ".observer." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(staging, JsonSerializer.Serialize(status)); File.Move(staging, ticket.StatusFile, overwrite: true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
