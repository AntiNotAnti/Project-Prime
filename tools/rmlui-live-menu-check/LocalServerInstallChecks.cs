using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

internal static class LocalServerInstallChecks
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    internal static void Run()
    {
        IncompleteDownloadedInstallCanRetry();
        ReadyServerWaitsAndSkipsInstall();
        OverlappingRequestsInstallOnceOffThread();
        InstallFailureAndInvalidReadyAllowRetry();
        CancellationDoesNotStartOrReleaseAnotherRequestsGate();
    }

    private static void IncompleteDownloadedInstallCanRetry()
    {
        string previousDirectory = LauncherPrefs.Directory;
        string root = Path.Combine(Path.GetTempPath(),
            "project-prime-local-server-check-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "server");
        string binary = Path.Combine(directory, UpdateCheck.ServerBinaryName());
        string marker = LocalServer.IncompleteInstallMarkerPath(directory);

        try
        {
            Directory.CreateDirectory(directory);
            LauncherPrefs.Directory = root;
            File.WriteAllBytes(binary, new byte[] { 0x4D, 0x5A });
            Check(LocalServer.HasUsableDownloadedBinary(directory),
                "an existing downloaded server without a marker stays available");

            File.WriteAllText(marker, "partial install");
            Check(!LocalServer.HasUsableDownloadedBinary(directory),
                "an incomplete install marker hides a partially extracted server binary");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool cancellationPreservedMarker = false;
            try
            {
                LocalServer.CompleteInstall(directory, cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationPreservedMarker = File.Exists(marker);
            }
            Check(cancellationPreservedMarker
                && !LocalServer.HasUsableDownloadedBinary(directory),
                "cancellation during retry leaves the downloaded server unavailable");

            File.WriteAllBytes(binary, new byte[] { 0x4D, 0x5A, 0x01 });
            LocalServer.CompleteInstall(directory, CancellationToken.None);
            Check(!File.Exists(marker)
                && LocalServer.HasUsableDownloadedBinary(directory),
                "a completed retry clears the marker and restores binary availability");

            if (OperatingSystem.IsWindows()
                && !File.Exists(Path.Combine(AppContext.BaseDirectory,
                    UpdateCheck.ServerBinaryName())))
            {
                Check(LocalServer.Ready,
                    "LocalServer readiness sees the completed downloaded retry");
                File.WriteAllText(marker, "partial retry");
                Check(!LocalServer.Ready,
                    "LocalServer readiness rejects the marked downloaded candidate");
                LocalServer.CompleteInstall(directory, CancellationToken.None);
                Check(LocalServer.Ready,
                    "LocalServer readiness recovers after the retry completes");
            }
        }
        finally
        {
            LauncherPrefs.Directory = previousDirectory;
            if (File.Exists(marker)) File.Delete(marker);
            if (File.Exists(binary)) File.Delete(binary);
            if (Directory.Exists(directory)) Directory.Delete(directory);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    private static void ReadyServerWaitsAndSkipsInstall()
    {
        using var gate = new SemaphoreSlim(0, 1);
        int installs = 0, starts = 0;
        Task<(string? Error, bool Started)> pending = EnsureAndStartAsync(
            () => true,
            _ => { installs++; return "ready server should not install"; },
            gate, CancellationToken.None,
            () => starts++);

        try
        {
            Check(!pending.IsCompleted, "ready server waits for an in-progress installation gate");
            gate.Release();
            var result = Complete(pending);
            Check(result.Error == null && result.Started && installs == 0 && starts == 1,
                "ready server skips install and continues to start");
        }
        finally
        {
            if (gate.CurrentCount == 0) gate.Release();
            Drain(pending);
        }
    }

    private static void OverlappingRequestsInstallOnceOffThread()
    {
        using var gate = new SemaphoreSlim(1, 1);
        using var installCancellation = new CancellationTokenSource();
        var installEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int ready = 0, installs = 0, starts = 0;
        int callerThread = Environment.CurrentManagedThreadId;
        int installThread = -1;
        bool tokenForwarded = false;
        Task<(string? Error, bool Started)>? first = null;
        Task<(string? Error, bool Started)>? retry = null;

        try
        {
            first = EnsureAndStartAsync(
                () => Volatile.Read(ref ready) != 0,
                token =>
                {
                    installThread = Environment.CurrentManagedThreadId;
                    tokenForwarded = token == installCancellation.Token;
                    Interlocked.Increment(ref installs);
                    installEntered.TrySetResult(true);
                    finishInstall.Task.WaitAsync(s_timeout).GetAwaiter().GetResult();
                    Volatile.Write(ref ready, 1);
                    return null;
                },
                gate, installCancellation.Token,
                () => Interlocked.Increment(ref starts));

            installEntered.Task.WaitAsync(s_timeout).GetAwaiter().GetResult();
            retry = EnsureAndStartAsync(
                () => Volatile.Read(ref ready) != 0,
                _ => { Interlocked.Increment(ref installs); return "retry should reuse installed server"; },
                gate, CancellationToken.None,
                () => Interlocked.Increment(ref starts));
            Check(!retry.IsCompleted, "overlapping create waits until package extraction completes");

            finishInstall.TrySetResult(true);
            var results = Task.WhenAll(first, retry).WaitAsync(s_timeout).GetAwaiter().GetResult();
            Check(installThread != callerThread && tokenForwarded,
                "installer runs off the caller thread and receives the request cancellation token");
            Check(results[0].Error == null && results[1].Error == null
                && results[0].Started && results[1].Started && installs == 1 && starts == 2,
                "retry rechecks readiness and reuses the completed install");
        }
        finally
        {
            finishInstall.TrySetResult(true);
            Drain(first);
            Drain(retry);
        }
    }

    private static void InstallFailureAndInvalidReadyAllowRetry()
    {
        using var gate = new SemaphoreSlim(1, 1);
        int ready = 0, installs = 0, starts = 0;

        var failed = Complete(EnsureAndStartAsync(
            () => Volatile.Read(ref ready) != 0,
            _ => { installs++; return "test install failure"; },
            gate, CancellationToken.None, () => starts++));
        Check(failed.Error == "test install failure" && !failed.Started
            && starts == 0 && gate.CurrentCount == 1,
            "install failure prevents start and releases the gate");

        var notReady = Complete(EnsureAndStartAsync(
            () => Volatile.Read(ref ready) != 0,
            _ => { installs++; return null; },
            gate, CancellationToken.None, () => starts++));
        Check(notReady.Error?.Contains("usable dedicated server", StringComparison.Ordinal) == true
            && !notReady.Started && starts == 0 && gate.CurrentCount == 1,
            "reported install success without a ready binary prevents start");

        var retry = Complete(EnsureAndStartAsync(
            () => Volatile.Read(ref ready) != 0,
            _ => { installs++; Volatile.Write(ref ready, 1); return null; },
            gate, CancellationToken.None, () => starts++));
        Check(retry.Error == null && retry.Started && installs == 3 && starts == 1,
            "a failed attempt releases the gate for a later successful retry");
    }

    private static void CancellationDoesNotStartOrReleaseAnotherRequestsGate()
    {
        using var gate = new SemaphoreSlim(1, 1);
        int installs = 0, starts = 0;
        gate.Wait();

        using var waitingCancellation = new CancellationTokenSource();
        Task<(string? Error, bool Started)> waiter = EnsureAndStartAsync(
            () => false,
            _ => { installs++; return "must not install"; },
            gate, waitingCancellation.Token, () => starts++);
        Check(!waiter.IsCompleted && gate.CurrentCount == 0,
            "second create is waiting for the installation gate");
        waitingCancellation.Cancel();
        bool waiterCancelled = ThrowsCancellation(waiter);

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        bool requestCancelled = ThrowsCancellation(EnsureAndStartAsync(
            () => false,
            _ => { installs++; return "must not install"; },
            gate, preCancelled.Token, () => starts++));
        Check(waiterCancelled && requestCancelled && installs == 0 && starts == 0
            && gate.CurrentCount == 0,
            "cancelled waiters and pre-cancelled requests neither install nor release the held gate");
        gate.Release();
        Check(gate.CurrentCount == 1, "the owning create can release the installation gate normally");

        using var installCancellation = new CancellationTokenSource();
        bool tokenForwarded = false;
        bool installCancelled = ThrowsCancellation(EnsureAndStartAsync(
            () => false,
            token =>
            {
                tokenForwarded = token == installCancellation.Token;
                installCancellation.Cancel();
                // LocalServer.Install converts a cancelled download into a
                // failure result, which is why the production helper checks the token again.
                return "download cancelled";
            },
            gate, installCancellation.Token, () => starts++));
        Check(installCancelled && tokenForwarded && starts == 0 && gate.CurrentCount == 1,
            "cancellation returned as an install failure is rethrown before server start");
    }

    private static async Task<(string? Error, bool Started)> EnsureAndStartAsync(
        Func<bool> isReady, Func<CancellationToken, string?> install,
        SemaphoreSlim gate, CancellationToken token, Action start)
    {
        string? error = await LobbyCreationService.EnsureLocalServerReadyAsync(
            isReady, install, gate, token).ConfigureAwait(false);
        if (error != null) return (error, false);
        start();
        return (null, true);
    }

    private static (string? Error, bool Started) Complete(Task<(string? Error, bool Started)> task)
        => task.WaitAsync(s_timeout).GetAwaiter().GetResult();

    private static bool ThrowsCancellation(Task task)
    {
        try
        {
            task.WaitAsync(s_timeout).GetAwaiter().GetResult();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static void Drain(Task? task)
    {
        if (task == null) return;
        try { task.WaitAsync(s_timeout).GetAwaiter().GetResult(); }
        catch (Exception) { }
    }

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new InvalidOperationException(name);
        Console.WriteLine("LOCAL SERVER INSTALL PASS " + name);
    }
}
