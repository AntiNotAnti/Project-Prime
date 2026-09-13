using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Reporting;
using ProjectPrime.Server.Shared;
using MphRead.Reporting;

namespace ProjectPrime.Server.Node.Workers;

public sealed class WorkerPoolOptions
{
    public WorkerLaunchOptions[] Processes { get; set; } = [];
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool ForceAfterDrainDeadline { get; set; }
    internal Action<ManagedWorker>? BeforeSigningInitialization { get; init; }
}

public static class WorkerPoolRegistration
{
    public static IServiceCollection AddNodeWorkerPool(this IServiceCollection services, IConfiguration configuration, Guid nodeId)
    {
        var options = configuration.GetSection("Node:Workers").Get<WorkerPoolOptions>() ?? new();
        if (options.Processes.Length > 64 || options.DrainTimeout <= TimeSpan.Zero || options.DrainTimeout > TimeSpan.FromHours(24))
            throw new ArgumentException("Invalid worker pool limits.");
        foreach (var launch in options.Processes) launch.Validate();
        TimeSpan shutdownBudget = options.Processes.Select(p => p.ShutdownTimeout).DefaultIfEmpty(TimeSpan.FromSeconds(10)).Max();
        services.Configure<HostOptions>(host => host.ShutdownTimeout = options.DrainTimeout + shutdownBudget + TimeSpan.FromSeconds(5));
        Guid incarnation = Guid.NewGuid();
        services.AddSingleton(options);
        services.AddSingleton(new WorkerManager(new(nodeId), incarnation,
            Math.Max(1, options.Processes.Length + 1)));
        services.AddSingleton<IWorkerManager>(sp => sp.GetRequiredService<WorkerManager>());
        services.AddSingleton(new WorkerAdmissionIssuer("node-" + incarnation.ToString("N")[..12]));
        ServerReportingOptions? reporting = ServerReportingOptions.FromEnvironment();
        if (reporting != null)
        {
            if (reporting.ServerId != nodeId) throw new ArgumentException("Reporting server identity must equal the configured Node identity.");
            services.AddSingleton<IMatchReportTransport>(_ => reporting.CreateTransport());
            services.AddSingleton(sp => new MatchReportOutbox(reporting.Outbox, sp.GetRequiredService<IMatchReportTransport>()));
            services.AddSingleton(sp => new NodeReportIngestor(sp.GetRequiredService<MatchReportOutbox>(),
                Path.GetFullPath(Path.Combine(reporting.Outbox.Directory, "node-receipts"))));
        }
        services.AddSingleton(sp => new WorkerScheduler(sp.GetRequiredService<WorkerManager>(),
            logger: sp.GetRequiredService<ILogger<WorkerScheduler>>(),
            reports: sp.GetService<NodeReportIngestor>(),
            clock: sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<WorkerPoolHostedService>();
        return services;
    }
}

/// <summary>Host shutdown first closes lobby and match admission. Graceful shutdown waits
/// for terminal matches and durable report ownership before asking children to exit.</summary>
public sealed class WorkerPoolHostedService(WorkerScheduler scheduler, WorkerAdmissionIssuer issuer,
    LobbyManager lobbies, WorkerPoolOptions options, TimeProvider clock,
    ILogger<WorkerPoolHostedService> logger) : IHostedService
{
    private sealed class Slot(WorkerLaunchOptions launch)
    {
        public WorkerLaunchOptions Launch { get; } = launch;
        public ManagedWorker? Worker;
        public Queue<DateTimeOffset> Attempts { get; } = [];
        public DateTimeOffset NextAttempt;
        public int ConsecutiveFailures;
    }

    private readonly Slot[] _slots = options.Processes.Select(launch => new Slot(launch)).ToArray();
    private readonly CancellationTokenSource _maintenanceStop = new();
    private Task _maintenance = Task.CompletedTask;
    private const double RecycleThreshold = 0.80;
    private const int MaximumReplacementAttempts = 5;
    private static readonly TimeSpan ReplacementWindow = TimeSpan.FromMinutes(10);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (Slot slot in _slots)
                slot.Worker = await StartReadyWorkerAsync(slot.Launch, cancellationToken);
            _maintenance = MaintainAsync(_maintenanceStop.Token);
        }
        catch { await scheduler.DisposeAsync(); throw; }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _maintenanceStop.Cancel();
        try { await _maintenance.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { }
        lobbies.CloseAdmission();
        scheduler.Drain("Node draining.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.DrainTimeout);
        try { await scheduler.WaitForDrainAsync(deadline.Token); }
        catch (OperationCanceledException) when (options.ForceAfterDrainDeadline && !cancellationToken.IsCancellationRequested)
        { logger.LogWarning("Worker drain deadline reached; forcing explicit shutdown."); }
        catch (Exception error)
        {
            logger.LogCritical(error, "Graceful Worker drain failed. Durable completion is unconfirmed; host teardown may interrupt remaining matches.");
            throw;
        }
        // Without the explicit force option, a deadline fails shutdown rather than claiming durable completion.
        await scheduler.ShutdownAsync("Node shutdown.", cancellationToken);
        _maintenanceStop.Dispose();
    }

    internal async Task<ManagedWorker> StartReadyWorkerAsync(WorkerLaunchOptions launch,
        CancellationToken cancellationToken)
    {
        ManagedWorker worker = await scheduler.StartWorkerAsync(launch,
            requireSigningInitialization: true, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            options.BeforeSigningInitialization?.Invoke(worker);
            await scheduler.InitializeSigningKeyAsync(worker,
                new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey()),
                cancellationToken).ConfigureAwait(false);
            return worker;
        }
        catch
        {
            scheduler.QuarantineWorker(worker.Id,
                "Worker signing-key initialization failed.");
            using var cleanup = new CancellationTokenSource(
                launch.ShutdownTimeout + TimeSpan.FromSeconds(5));
            try
            {
                await scheduler.ForceRetireWorkerAsync(worker.Id, cleanup.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                logger.LogCritical(cleanupError,
                    "Failed to clean up a Worker after signing-key initialization failed.");
            }
            throw;
        }
    }

    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (Slot slot in _slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await MaintainSlotAsync(slot, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    RegisterFailure(slot);
                    logger.LogError(error,
                        "Worker pool replacement attempt failed; the slot remains bounded by restart backoff.");
                }
            }
        }
    }

    private async Task MaintainSlotAsync(Slot slot, CancellationToken cancellationToken)
    {
        ManagedWorker? current = slot.Worker;
        if (current == null)
        {
            if (!CanAttempt(slot)) return;
            RegisterAttempt(slot);
            slot.Worker = await StartReadyWorkerAsync(slot.Launch, cancellationToken)
                .ConfigureAwait(false);
            ResetFailures(slot);
            return;
        }

        WorkerSnapshot snapshot = current.Snapshot();
        bool faulted = snapshot.Status is WorkerStatus.Faulted or WorkerStatus.Stopped
            || current.Completion.IsCompleted && snapshot.Status != WorkerStatus.Draining;
        bool exhausted = snapshot.IdentityHistoryCapacity > 0
            && snapshot.IdentityHistoryUsed >= Math.Ceiling(
                snapshot.IdentityHistoryCapacity * RecycleThreshold);
        if (exhausted && snapshot.Status == WorkerStatus.Ready)
        {
            scheduler.QuarantineWorker(current.Id,
                "Worker identity history reached the rolling recycle threshold.");
            snapshot = current.Snapshot();
        }

        bool active = snapshot.Matches.Values.Any(status => status is
            MatchStatus.Starting or MatchStatus.Ready or MatchStatus.Running);
        if (!faulted && !(snapshot.Status == WorkerStatus.Draining && !active)) return;
        if (!CanAttempt(slot)) return;

        RegisterAttempt(slot);
        if (faulted)
        {
            await scheduler.ForceRetireWorkerAsync(current.Id, cancellationToken)
                .ConfigureAwait(false);
            slot.Worker = null;
            slot.Worker = await StartReadyWorkerAsync(slot.Launch, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Start and initialize the replacement before retiring the empty
            // draining Worker, preserving the configured ready capacity.
            slot.Worker = await ReplaceDrainedWorkerAsync(current, slot.Launch,
                cancellationToken).ConfigureAwait(false);
        }
        ResetFailures(slot);
    }

    internal async Task<ManagedWorker> ReplaceDrainedWorkerAsync(
        ManagedWorker current, WorkerLaunchOptions launch,
        CancellationToken cancellationToken)
    {
        ManagedWorker? replacement = null;
        bool adopted = false;
        try
        {
            replacement = await StartReadyWorkerAsync(launch, cancellationToken)
                .ConfigureAwait(false);
            bool retired = await scheduler.RetireDrainedWorkerAsync(current.Id,
                "Worker rolling recycle completed.", cancellationToken)
                .ConfigureAwait(false);
            if (!retired)
                throw new IOException("The draining Worker could not be retired.");
            adopted = true;
            return replacement;
        }
        finally
        {
            if (replacement != null && !adopted)
            {
                scheduler.QuarantineWorker(replacement.Id,
                    "Rolling recycle did not adopt the replacement Worker.");
                try
                {
                    await scheduler.ForceRetireWorkerAsync(replacement.Id,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    logger.LogCritical(cleanupError,
                        "Failed to clean up an unadopted replacement Worker.");
                }
            }
        }
    }

    private bool CanAttempt(Slot slot)
    {
        DateTimeOffset now = clock.GetUtcNow();
        while (slot.Attempts.TryPeek(out DateTimeOffset attempted)
            && now - attempted >= ReplacementWindow) slot.Attempts.Dequeue();
        return slot.Attempts.Count < MaximumReplacementAttempts && now >= slot.NextAttempt;
    }

    private void RegisterAttempt(Slot slot) => slot.Attempts.Enqueue(clock.GetUtcNow());

    private void RegisterFailure(Slot slot)
    {
        slot.ConsecutiveFailures = Math.Min(slot.ConsecutiveFailures + 1, 6);
        double seconds = Math.Min(30, Math.Pow(2, slot.ConsecutiveFailures - 1));
        slot.NextAttempt = clock.GetUtcNow() + TimeSpan.FromSeconds(seconds);
    }

    private static void ResetFailures(Slot slot)
    {
        slot.ConsecutiveFailures = 0;
        slot.NextAttempt = default;
    }
}
