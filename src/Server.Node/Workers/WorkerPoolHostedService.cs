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
        services.AddSingleton(new WorkerManager(new(nodeId), incarnation));
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
            logger: sp.GetRequiredService<ILogger<WorkerScheduler>>(), reports: sp.GetService<NodeReportIngestor>()));
        services.AddHostedService<WorkerPoolHostedService>();
        return services;
    }
}

/// <summary>Host shutdown first closes lobby and match admission. Graceful shutdown waits
/// for terminal matches and durable report ownership before asking children to exit.</summary>
public sealed class WorkerPoolHostedService(WorkerScheduler scheduler, WorkerAdmissionIssuer issuer,
    LobbyManager lobbies, WorkerPoolOptions options, ILogger<WorkerPoolHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (WorkerLaunchOptions launch in options.Processes)
            {
                ManagedWorker worker = await scheduler.StartWorkerAsync(launch, cancellationToken);
                if (!worker.TrySend(new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey())))
                    throw new IOException("Worker signing-key initialization failed.");
            }
        }
        catch { await scheduler.DisposeAsync(); throw; }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
    }
}
